/**
 * index.js — AnyDownloader Web Server
 *
 * Session-based architecture:
 *  - Each WebSocket connection = one session with one download slot
 *  - Global concurrency limit via SessionManager queue
 *  - Files stored in /tmp/anydownloader-sessions/<sessionId>/
 *  - All files auto-deleted on disconnect
 *  - One-time token for safe file delivery
 */

import express from 'express';
import cors from 'cors';
import { WebSocketServer } from 'ws';
import { createServer } from 'http';
import { existsSync, rmSync } from 'fs';
import { basename, dirname } from 'path';
import { SessionManager } from './sessionManager.js';
import { fetchVideoInfo, extractFormats, startSessionDownload } from './downloader.js';
import { validateUrl } from './utils/validateUrl.js';
import { InfoCache } from './utils/infoCache.js';
import {
  globalRateLimit,
  rateLimitMiddleware,
  limiters,
  getClientIp,
} from './middleware/rateLimit.js';

const PORT = parseInt(process.env.PORT ?? '3001', 10);
const FRONTEND_ORIGIN = process.env.FRONTEND_ORIGIN ?? 'http://localhost:3000';

// Build allowed origins: configured frontend + HTTP variant + direct IP access
const ALLOWED_ORIGINS = new Set([FRONTEND_ORIGIN]);
// Also allow http:// variant (Cloudflare Flexible: user sees HTTPS, origin sees HTTP)
if (FRONTEND_ORIGIN.startsWith('https://')) {
  ALLOWED_ORIGINS.add(FRONTEND_ORIGIN.replace('https://', 'http://'));
}
// Allow direct IP access for testing
if (process.env.SERVER_IP) {
  ALLOWED_ORIGINS.add(`http://${process.env.SERVER_IP}`);
  ALLOWED_ORIGINS.add(`https://${process.env.SERVER_IP}`);
}

// Memory guard — reject new expensive operations when RSS exceeds this limit.
// Default: 750 MB (leaves ~250 MB for OS, yt-dlp child processes, /tmp buffers
// within a 1 GB total RAM budget).
const MEMORY_LIMIT_BYTES =
  parseInt(process.env.MEMORY_LIMIT_MB ?? '750', 10) * 1024 * 1024;

function memoryGuard(req, res, next) {
  const rss = process.memoryUsage().rss;
  if (rss > MEMORY_LIMIT_BYTES) {
    return res.status(503).json({
      error: 'Server is under memory pressure. Please try again in a moment.',
      retryAfter: 30,
    });
  }
  next();
}

// ---------------------------------------------------------------------------
// App bootstrap
// ---------------------------------------------------------------------------

const app = express();
const httpServer = createServer(app);
const sessions = new SessionManager();
const infoCache = new InfoCache();

/** Normalize thumbnail URLs: protocol-relative (//) and plain http: → https: */
function normalizeThumbnailUrl(url) {
  if (!url) return null;
  if (url.startsWith('//')) return 'https:' + url;
  if (url.startsWith('http:')) return 'https:' + url.slice(5);
  return url;
}

// Trust first proxy hop so req.ip reflects the real client IP (Nginx → Node)
app.set('trust proxy', 1);

app.use(cors({
  origin: (origin, cb) => {
    // Allow: same-origin (no origin header), any configured origin, browser extensions
    if (
      !origin ||
      ALLOWED_ORIGINS.has(origin) ||
      /^chrome-extension:\/\//.test(origin) ||
      /^moz-extension:\/\//.test(origin)
    ) {
      cb(null, true);
    } else {
      cb(new Error(`CORS: origin not allowed — ${origin}`));
    }
  },
  methods: ['GET', 'POST', 'DELETE', 'OPTIONS'],
  allowedHeaders: ['Content-Type', 'X-Session-Id'],
}));

// Tight body size — URLs and short JSON payloads never need more than 8 KB
app.use(express.json({ limit: '8kb' }));

// ── Global rate limit: 300 req / 1 min per IP ─────────────────────────────
app.use(globalRateLimit);

// ---------------------------------------------------------------------------
// WebSocket: create a session on connect, clean it up on disconnect
// ---------------------------------------------------------------------------

const wss = new WebSocketServer({ server: httpServer });

wss.on('connection', (ws) => {
  const sessionId = sessions.createSession(ws);
  // Send the session ID so the client can include it in HTTP headers
  ws.send(JSON.stringify({ type: 'session', sessionId }));

  ws.on('close', () => {
    sessions.cleanupSession(sessionId);
    console.log(`[session] cleaned up: ${sessionId.slice(0, 8)}`);
  });

  ws.on('error', (err) => {
    console.error(`[ws] error for ${sessionId.slice(0, 8)}:`, err.message);
    sessions.cleanupSession(sessionId);
  });

  console.log(`[session] created: ${sessionId.slice(0, 8)}`);
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Extract and validate the X-Session-Id header. Returns session or sends 4xx. */
function requireSession(req, res) {
  const id = req.headers['x-session-id'];
  if (!id) {
    res.status(400).json({ error: 'Missing X-Session-Id header. Open WebSocket first.' });
    return null;
  }
  const session = sessions.getSession(id);
  if (!session) {
    res.status(404).json({ error: 'Session not found. Reconnect WebSocket.' });
    return null;
  }
  return session;
}

// ---------------------------------------------------------------------------
// POST /api/info — fetch video metadata (no session required)
// ---------------------------------------------------------------------------

app.post('/api/info',
  rateLimitMiddleware(limiters.info, 'video info requests (yt-dlp is expensive — try again in a few minutes)'),
  memoryGuard,
  async (req, res) => {
  // Hard request deadline — must return a response before Cloudflare's 100s
  // origin timeout (Free/Pro plans) kicks in and serves an HTML 524 page.
  // 90s leaves 10s of headroom for TLS handshake + nginx buffering.
  req.setTimeout(90_000, () => {
    if (!res.headersSent) {
      res.status(504).json({ error: 'Video info fetch took too long — please try again.' });
    }
  });

  const { url } = req.body ?? {};
  let validatedUrl;
  try { validatedUrl = validateUrl(url); }
  catch (err) { return res.status(400).json({ error: err.message }); }

  // Return cached result if available
  const cached = infoCache.get(validatedUrl);
  if (cached) return res.json(cached);

  try {
    const raw = await fetchVideoInfo(validatedUrl);
    const { videoFormats, audioFormats, hasSeparateStreams } = extractFormats(raw);
    const result = {
      title:     raw.title    || null,
      thumbnail: normalizeThumbnailUrl(raw.thumbnail || null),
      duration:  raw.duration || null,
      uploader:  raw.uploader || raw.channel || null,
      videoFormats,
      audioFormats,
      hasSeparateStreams,
    };
    infoCache.set(validatedUrl, result);
    return res.json(result);
  } catch (err) {
    return res.status(500).json({ error: err.message });
  }
});

// ---------------------------------------------------------------------------
// POST /api/download — enqueue a download for the session
// ---------------------------------------------------------------------------

app.post('/api/download',
  rateLimitMiddleware(limiters.download, 'download requests (limit: 8/hour per IP)'),
  memoryGuard,
  async (req, res) => {
  const session = requireSession(req, res);
  if (!session) return;

  const { url, formatStr, container } = req.body ?? {};

  let validatedUrl;
  try { validatedUrl = validateUrl(url); }
  catch (err) { return res.status(400).json({ error: err.message }); }

  if (!formatStr || typeof formatStr !== 'string') {
    return res.status(400).json({ error: 'formatStr is required' });
  }

  const enqueued = sessions.enqueueDownload(
    session.id,
    (sess) => startSessionDownload(
      sess,
      { url: validatedUrl, formatStr, container: container ?? 'mp4' },
      sessions
    )
  );

  if (!enqueued) {
    return res.status(409).json({ error: 'Session already has an active or queued download.' });
  }

  return res.status(202).json({ ok: true });
});

// ---------------------------------------------------------------------------
// DELETE /api/download — cancel the current download for the session
// ---------------------------------------------------------------------------

app.delete('/api/download', (req, res) => {
  const session = requireSession(req, res);
  if (!session) return;
  sessions.cancelDownload(session.id);
  return res.json({ ok: true });
});

// ---------------------------------------------------------------------------
// GET /api/file/:token — one-time file download; deletes file after serving
// ---------------------------------------------------------------------------

app.get('/api/file/:token',
  rateLimitMiddleware(limiters.file, 'file download requests'),
  (req, res) => {
  const entry = sessions.consumeToken(req.params.token);
  if (!entry) return res.status(404).json({ error: 'Token not found or already used.' });
  if (!existsSync(entry.filePath)) return res.status(404).json({ error: 'File missing on disk.' });

  const filename = basename(entry.filePath);
  res.download(entry.filePath, filename, (err) => {
    if (err && !res.headersSent) console.error('[file] download error:', err.message);
    // Delete the output directory regardless of success/failure
    const dir = dirname(entry.filePath);
    try { rmSync(dir, { recursive: true, force: true }); } catch {}
  });
});

// ---------------------------------------------------------------------------
// GET /api/thumbnail?url=<encoded> — server-side thumbnail proxy
//
// Required because Bilibili's CDN (hdslb.com) enforces hotlink protection:
// it checks the HTTP Referer header and blocks requests not coming from
// bilibili.com. The browser would send the wrong Referer, so we fetch
// server-side with the correct one and stream the image back.
// ---------------------------------------------------------------------------

/** Map CDN hostname → Referer to send when fetching thumbnails. */
function thumbnailReferer(hostname) {
  if (/hdslb\.com$/.test(hostname) || /bilibili\.com$/.test(hostname)) {
    return 'https://www.bilibili.com/';
  }
  if (/ytimg\.com$/.test(hostname) || /youtube\.com$/.test(hostname)) {
    return 'https://www.youtube.com/';
  }
  if (/tiktokcdn\.com$/.test(hostname) || /tiktok\.com$/.test(hostname)) {
    return 'https://www.tiktok.com/';
  }
  // Generic fallback — return the origin of the image itself
  return `https://${hostname}/`;
}

/** RFC-1918 / loopback check (simple version — full check is in validateUrl.js) */
function isPrivateHost(hostname) {
  return (
    hostname === 'localhost' ||
    hostname.startsWith('127.') ||
    hostname.startsWith('10.') ||
    hostname.startsWith('192.168.') ||
    hostname === '::1'
  );
}

app.get('/api/thumbnail',
  rateLimitMiddleware(limiters.thumbnail, 'thumbnail requests'),
  async (req, res) => {
  const raw = req.query.url;
  if (!raw || typeof raw !== 'string') {
    return res.status(400).json({ error: 'Missing url parameter.' });
  }

  let parsed;
  try { parsed = new URL(raw); } catch {
    return res.status(400).json({ error: 'Invalid URL.' });
  }

  // Only proxy https image URLs; block private network access (SSRF)
  if (parsed.protocol !== 'https:') {
    return res.status(400).json({ error: 'Only https thumbnail URLs are supported.' });
  }
  if (isPrivateHost(parsed.hostname)) {
    return res.status(403).json({ error: 'Private network addresses are not allowed.' });
  }

  try {
    const upstream = await fetch(raw, {
      headers: {
        Referer:       thumbnailReferer(parsed.hostname),
        'User-Agent':  'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36',
        Accept:        'image/webp,image/avif,image/*,*/*;q=0.8',
      },
      signal: AbortSignal.timeout(10_000),
    });

    if (!upstream.ok) {
      return res.status(upstream.status).end();
    }

    const contentType = upstream.headers.get('content-type') ?? 'image/jpeg';
    res.setHeader('Content-Type', contentType);
    res.setHeader('Cache-Control', 'public, max-age=3600, immutable');

    const buf = await upstream.arrayBuffer();
    res.send(Buffer.from(buf));
  } catch {
    // Thumbnail fetch failed — return 502 so the frontend can show a placeholder
    res.status(502).end();
  }
});

// ---------------------------------------------------------------------------
// GET /api/health
// ---------------------------------------------------------------------------

app.get('/api/health', (_req, res) => {
  const mem = process.memoryUsage();
  const rss = mem.rss;
  res.json({
    status: rss < MEMORY_LIMIT_BYTES ? 'ok' : 'degraded',
    uptime_s: Math.round(process.uptime()),
    memory: {
      rss_mb:        Math.round(rss / 1024 / 1024),
      heap_used_mb:  Math.round(mem.heapUsed / 1024 / 1024),
      heap_total_mb: Math.round(mem.heapTotal / 1024 / 1024),
      limit_mb:      Math.round(MEMORY_LIMIT_BYTES / 1024 / 1024),
      pct:           Math.round(rss / MEMORY_LIMIT_BYTES * 100),
    },
  });
});

// ---------------------------------------------------------------------------
// 404 fallback
// ---------------------------------------------------------------------------

app.use((_req, res) => res.status(404).json({ error: 'Not found' }));

// ---------------------------------------------------------------------------
// JSON error handler — MUST be last. Without this, any thrown error (CORS
// rejection, JSON parse error, unhandled route exception) triggers Express's
// default handler, which returns an HTML stack-trace page. The frontend then
// sees "Unexpected token '<', <!DOCTYPE..." when it calls res.json(). Forcing
// a JSON envelope here keeps the API contract intact on every code path.
// ---------------------------------------------------------------------------
// eslint-disable-next-line no-unused-vars
app.use((err, req, res, _next) => {
  const status = err?.status || err?.statusCode || 500;
  const message = err?.message || 'Internal server error';
  console.error(`[api-error] ${req.method} ${req.originalUrl} → ${status}: ${message}`);
  if (res.headersSent) return;
  res.status(status).json({ error: message });
});

// ---------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------

httpServer.listen(PORT, () => {
  console.log(`AnyDownloader web server →  http://localhost:${PORT}`);
  console.log(`WebSocket endpoint       →  ws://localhost:${PORT}`);
  console.log(`Allowed frontend origin  →  ${FRONTEND_ORIGIN}`);
});

process.on('SIGTERM', () => httpServer.close(() => process.exit(0)));
process.on('SIGINT',  () => httpServer.close(() => process.exit(0)));

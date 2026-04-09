/**
 * downloader.js - session-aware video download execution
 */

import { spawn } from 'child_process';
import { mkdirSync, existsSync, readdirSync } from 'fs';
import { join } from 'path';
import os from 'os';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const YTDLP_HOSTS = new Set([
  'youtube.com', 'www.youtube.com', 'youtu.be', 'm.youtube.com',
  'tiktok.com', 'www.tiktok.com', 'vm.tiktok.com',
  'bilibili.com', 'www.bilibili.com',
  'douyin.com', 'www.douyin.com',
  'vimeo.com', 'www.vimeo.com',
  'twitch.tv', 'www.twitch.tv',
  'twitter.com', 'x.com', 'www.twitter.com',
  'instagram.com', 'www.instagram.com',
  'facebook.com', 'www.facebook.com',
  'nicovideo.jp', 'www.nicovideo.jp',
  'soundcloud.com', 'www.soundcloud.com',
  'dailymotion.com', 'www.dailymotion.com',
]);

const SESSION_BASE = join(os.tmpdir(), 'anydownloader-sessions');

// ---------------------------------------------------------------------------
// URL classification
// ---------------------------------------------------------------------------

/** @returns {'ytdlp' | 'http'} */
export function classifyURL(url) {
  let parsed;
  try { parsed = new URL(url.trim()); } catch { return 'http'; }
  if (YTDLP_HOSTS.has(parsed.hostname.toLowerCase())) return 'ytdlp';
  if (!['http:', 'https:'].includes(parsed.protocol)) return 'ytdlp';
  return 'http';
}

// ---------------------------------------------------------------------------
// Environment helpers
// ---------------------------------------------------------------------------

function enrichedEnv() {
  const env = { ...process.env };
  const brewBin = existsSync('/opt/homebrew/bin') ? '/opt/homebrew/bin' : '/usr/local/bin';
  const current = env.PATH ?? '/usr/bin:/bin:/usr/sbin:/sbin';
  if (!current.includes(brewBin)) env.PATH = `${brewBin}:/usr/local/bin:${current}`;
  return env;
}

function findYtDlp() {
  for (const p of ['/opt/homebrew/bin/yt-dlp', '/usr/local/bin/yt-dlp', '/usr/bin/yt-dlp']) {
    if (existsSync(p)) return p;
  }
  return null;
}

// ---------------------------------------------------------------------------
// Progress parsing
// ---------------------------------------------------------------------------

const PROGRESS_RE = /\[download\]\s+([\d.]+)%.*?at\s+([\d.]+)(KiB|MiB|GiB)\/s.*?ETA\s+([\d:]+)/;

function parseProgress(line) {
  if (!line.includes('[download]') || !line.includes('%')) return null;
  const m = PROGRESS_RE.exec(line);
  if (!m) return null;

  const mult = m[3] === 'KiB' ? 1024 : m[3] === 'MiB' ? 1_048_576 : 1_073_741_824;
  const etaParts = m[4].split(':').map(Number).filter(n => !isNaN(n));
  const eta = etaParts.length === 2
    ? etaParts[0] * 60 + etaParts[1]
    : etaParts.length === 3
      ? etaParts[0] * 3600 + etaParts[1] * 60 + etaParts[2]
      : null;

  return { progress: parseFloat(m[1]) / 100, speed: Math.round(parseFloat(m[2]) * mult), eta };
}

// ---------------------------------------------------------------------------
// fetchVideoInfo
// ---------------------------------------------------------------------------

/**
 * Run yt-dlp --dump-json and return parsed JSON.
 *
 * Hard wall-clock timeout: Cloudflare's Free/Pro plans hard-cap origin
 * responses at 100s (524 error). On a 1-CPU/1-GB VPS, slow upstreams +
 * retries can blow past that, causing the user to see an HTML 524 page
 * instead of a JSON error. We kill yt-dlp at 85s so Express can return a
 * clean JSON error well before Cloudflare gives up.
 *
 * @param {string} url
 * @param {object} [opts]
 * @param {number} [opts.timeoutMs=85000] wall-clock timeout
 * @returns {Promise<object>}
 */
export function fetchVideoInfo(url, opts = {}) {
  const timeoutMs = opts.timeoutMs ?? 85_000;
  return new Promise((resolve, reject) => {
    const ytdlp = findYtDlp();
    if (!ytdlp) return reject(new Error('yt-dlp not found. Install with: brew install yt-dlp'));

    const args = [
      '--dump-json', '--no-playlist', '--no-warnings',
      // Fail-fast on slow upstreams so we stay under Cloudflare's 100s cap.
      '--socket-timeout', '15',
      '--retries', '1',
      '--extractor-retries', '1',
      '--no-call-home',
      '--no-check-certificate',
      '--user-agent', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36',
      url,
    ];

    const proc = spawn(ytdlp, args, { env: enrichedEnv(), stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '';
    let stderr = '';
    let timedOut = false;
    let settled = false;

    const settle = (fn) => (v) => { if (settled) return; settled = true; clearTimeout(killTimer); fn(v); };
    const resolveOnce = settle(resolve);
    const rejectOnce  = settle(reject);

    // Wall-clock kill: SIGTERM first, then SIGKILL 2s later if it refuses.
    const killTimer = setTimeout(() => {
      timedOut = true;
      try { proc.kill('SIGTERM'); } catch {}
      setTimeout(() => { try { proc.kill('SIGKILL'); } catch {} }, 2000);
    }, timeoutMs);

    proc.stdout.on('data', c => { stdout += c.toString(); });
    proc.stderr.on('data', c => { stderr += c.toString(); });
    proc.on('error', err => rejectOnce(new Error(`Failed to spawn yt-dlp: ${err.message}`)));
    proc.on('close', code => {
      if (timedOut) {
        return rejectOnce(new Error(
          `Video info fetch timed out after ${Math.round(timeoutMs / 1000)}s. ` +
          `This URL or the host CDN is too slow for our 1-CPU server — please try again or try a different URL.`
        ));
      }
      if (code !== 0) return rejectOnce(new Error(`yt-dlp: ${stderr.trim() || `exit code ${code}`}`));
      const line = stdout.split('\n').find(l => l.trim().startsWith('{'));
      if (!line) return rejectOnce(new Error('yt-dlp returned no JSON output'));
      try { resolveOnce(JSON.parse(line)); }
      catch { rejectOnce(new Error('Could not parse yt-dlp JSON output')); }
    });
  });
}

// ---------------------------------------------------------------------------
// extractFormats  (mirrors macOS VideoInfo format processing)
// ---------------------------------------------------------------------------

/**
 * Process raw yt-dlp formats into structured video and audio format lists.
 *
 * Video formats: streams that have a video track (may or may not have audio).
 * Audio formats: streams that have audio only.
 *
 * Each returned format includes the exact `formatId` from yt-dlp so the
 * download step can use it directly (no filter expressions = no exit code 2).
 *
 * @param {object} raw  raw yt-dlp JSON
 * @returns {{
 *   videoFormats: Array<{formatId,qualityLabel,container,codec,filesize,hasAudio}>,
 *   audioFormats: Array<{formatId,label,container,bitrateKbps,filesize}>,
 *   hasSeparateStreams: boolean,
 * }}
 */
export function extractFormats(raw) {
  const all = Array.isArray(raw.formats) ? raw.formats : [];

  // ── Video formats ─────────────────────────────────────────────────────────
  // Prefer video-only streams (hasAudio=false) — same as macOS sortedVideoFormats
  const rawVideoOnly = all.filter(f =>
    f.vcodec && f.vcodec !== 'none' && f.height &&
    (!f.acodec || f.acodec === 'none')
  );

  // If no video-only streams, fall back to combined (some sites only have merged streams)
  const rawVideoSrc = rawVideoOnly.length > 0
    ? rawVideoOnly
    : all.filter(f => f.vcodec && f.vcodec !== 'none' && f.height);

  // Sort by height desc, then tbr desc for same height
  rawVideoSrc.sort((a, b) => (b.height - a.height) || ((b.tbr || 0) - (a.tbr || 0)));

  // Deduplicate by height: one chip per resolution (take highest-bitrate)
  const seenHeights = new Set();
  const videoFormats = [];
  for (const f of rawVideoSrc) {
    if (seenHeights.has(f.height)) continue;
    seenHeights.add(f.height);
    videoFormats.push({
      formatId: f.format_id,
      qualityLabel: `${f.height}p`,
      container: f.ext || 'mp4',
      codec: (f.vcodec || '').split('.')[0],
      filesize: f.filesize || f.filesize_approx || null,
      hasAudio: !!(f.acodec && f.acodec !== 'none'),
    });
  }

  // ── Audio formats ─────────────────────────────────────────────────────────
  const rawAudio = all
    .filter(f => f.acodec && f.acodec !== 'none' && (!f.vcodec || f.vcodec === 'none'))
    .sort((a, b) => (b.abr || 0) - (a.abr || 0));

  // Deduplicate by (bitrateKbps, ext) to avoid listing near-duplicates
  const seenAudio = new Set();
  const audioFormats = [];
  for (const f of rawAudio) {
    const kbps = f.abr ? Math.round(f.abr) : 0;
    const key = `${kbps}-${f.ext}`;
    if (seenAudio.has(key) || audioFormats.length >= 5) continue;
    seenAudio.add(key);
    audioFormats.push({
      formatId: f.format_id,
      label: kbps > 0 ? `${kbps}kbps ${(f.ext || 'audio').toUpperCase()}` : (f.format_note || f.format_id),
      container: f.ext || 'm4a',
      bitrateKbps: kbps || null,
      filesize: f.filesize || f.filesize_approx || null,
    });
  }

  return {
    videoFormats,
    audioFormats,
    // True when the site provides separate video-only and audio-only tracks.
    // When false, video formats already include audio — merging is N/A.
    hasSeparateStreams: rawVideoOnly.length > 0 && audioFormats.length > 0,
  };
}

// ---------------------------------------------------------------------------
// Format string builder
// ---------------------------------------------------------------------------

/**
 * Build the yt-dlp --format string and the output container from user choices.
 *
 * @param {{
 *   includeVideo: boolean,
 *   includeAudio: boolean,
 *   merge: boolean,
 *   videoFormatId: string | null,
 *   audioFormatId: string | null,
 * }} opts
 * @returns {{ formatStr: string, container: string }}
 */
export function buildFormatString({ includeVideo, includeAudio, merge, videoFormatId, audioFormatId }) {
  const vid = videoFormatId ?? 'bestvideo';
  const aud = audioFormatId ?? 'bestaudio';

  if (includeVideo && includeAudio) {
    // Always merge in the webapp (single-file output required)
    return { formatStr: `${vid}+${aud}/${vid}/best`, container: 'mp4' };
  }
  if (includeVideo) {
    return { formatStr: `${vid}/bestvideo/best`, container: 'mp4' };
  }
  if (includeAudio) {
    return { formatStr: `${aud}/bestaudio/best`, container: 'm4a' };
  }
  // Fallback (nothing selected — shouldn't happen)
  return { formatStr: 'bestvideo+bestaudio/best', container: 'mp4' };
}

// ---------------------------------------------------------------------------
// startSessionDownload
// ---------------------------------------------------------------------------

/**
 * Execute a yt-dlp download in a session-scoped temp directory.
 *
 * @param {object} session
 * @param {{ url: string, formatStr: string, container: string }} opts
 * @param {import('./sessionManager.js').SessionManager} manager
 * @returns {Promise<void>}
 */
export function startSessionDownload(session, { url, formatStr, container }, manager) {
  return new Promise((resolve, reject) => {
    const ytdlp = findYtDlp();
    if (!ytdlp) return reject(new Error('yt-dlp not found on this server.'));

    const outputDir = join(SESSION_BASE, session.id);
    mkdirSync(outputDir, { recursive: true });
    session.outputDir = outputDir;

    // Sanitize container to prevent argument injection
    const safeContainer = /^[a-z0-9]+$/i.test(container) ? container : 'mp4';

    const args = [
      '--format', formatStr,
      '--merge-output-format', safeContainer,
      // When merging to MP4, force audio transcoding to AAC.
      // VP9+OPUS (common on YouTube) can't be muxed into MP4 — ffmpeg falls
      // back to MKV unless we explicitly re-encode the audio track to AAC.
      // Video is copied as-is (H.264/VP9 both work in MP4 players today).
      ...(safeContainer === 'mp4' ? ['--postprocessor-args', 'Merger+ffmpeg:-c:v copy -c:a aac'] : []),
      '--output', join(outputDir, '%(title).100B.%(ext)s'),
      '--progress', '--newline',
      '--no-playlist',
      '--concurrent-fragments', '2',
      '--retries', '5',
      '--fragment-retries', '5',
      '--extractor-retries', '3',
      '--socket-timeout', '30',
      '--no-warnings',
      '--user-agent', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36',
      url,
    ];

    const proc = spawn(ytdlp, args, {
      env: enrichedEnv(),
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    session.process = proc;

    let resolvedPath = null;
    let stderrBuf = '';

    const handleLine = (line) => {
      // Final merged file path — also signal the UI to show "Merging…"
      const mergerMatch = /\[Merger\] Merging formats into "(.+)"/.exec(line);
      if (mergerMatch) {
        resolvedPath = mergerMatch[1];
        manager.notifyState(session.id, 'merging');
        return;
      }

      // MoveFiles: post-processing rename
      const moveMatch = /\[MoveFiles\] Moving file from ".*" to "(.+)"/.exec(line);
      if (moveMatch) { resolvedPath = moveMatch[1]; return; }

      // Destination: single-stream or audio-only download
      if (!resolvedPath && line.includes('[download] Destination:')) {
        const dest = line.replace('[download] Destination:', '').trim();
        if (dest && !dest.endsWith('.part')) resolvedPath = dest;
      }

      const update = parseProgress(line);
      if (update) manager.notifyProgress(session.id, update);
    };

    proc.stdout.on('data', chunk => chunk.toString().split('\n').forEach(handleLine));
    proc.stderr.on('data', chunk => {
      const s = chunk.toString();
      stderrBuf += s;
      s.split('\n').forEach(handleLine);
    });
    proc.on('error', err => reject(err));

    proc.on('close', code => {
      session.process = null;

      // If cancelled/disconnected, state won't be 'downloading' anymore — exit silently
      const sess = manager.getSession(session.id);
      if (!sess || sess.state !== 'downloading') { resolve(); return; }

      if (code !== 0 && code !== null) {
        const reason = stderrBuf.trim().split('\n').pop() || `yt-dlp exited with code ${code}`;
        reject(new Error(reason));
        return;
      }

      // Find output file: prefer parsed path, fall back to directory scan
      const candidate = (resolvedPath && existsSync(resolvedPath))
        ? resolvedPath
        : (() => {
            try {
              const files = readdirSync(outputDir).filter(
                f => !f.endsWith('.part') && !f.startsWith('.') && !/\.f\d+\./.test(f)
              );
              return files.length > 0 ? join(outputDir, files[0]) : null;
            } catch { return null; }
          })();

      if (candidate) {
        manager.notifyCompleted(session.id, candidate);
        resolve();
      } else {
        reject(new Error('Download completed but no output file was found.'));
      }
    });
  });
}

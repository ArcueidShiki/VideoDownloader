/**
 * downloader.js - session-aware video download execution
 */

import { spawn } from 'child_process';
import { mkdirSync, mkdtempSync, rmSync, statSync } from 'fs';
import { join, resolve as resolvePath, relative, isAbsolute, sep } from 'path';
import { createInterface } from 'readline';
import os from 'os';
import {
  enrichedEnv, findYtDlp, commonArgs, progressArgs, parseProgress,
  FILE_PREFIX, POSTPROCESS_PREFIX, spawnError, failureMessage, normalizeVideoInfo,
} from './utils/ytdlp.js';

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
// fetchVideoInfo
// ---------------------------------------------------------------------------

/**
 * Run yt-dlp --dump-single-json and return parsed JSON.
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
    const args = [
      ...commonArgs(), '--dump-single-json',
      // Fail-fast on slow upstreams so we stay under Cloudflare's 100s cap.
      '--socket-timeout', '15',
      '--retries', '1',
      '--extractor-retries', '1',
      '--', url,
    ];

    const proc = spawn(ytdlp, args, { env: enrichedEnv(), stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
    let stdout = '';
    let stderr = '';
    let timedOut = false;
    let settled = false;
    let forceKillTimer;

    const settle = (fn) => (v) => {
      if (settled) return;
      settled = true;
      clearTimeout(killTimer);
      clearTimeout(forceKillTimer);
      fn(v);
    };
    const resolveOnce = settle(resolve);
    const rejectOnce  = settle(reject);

    // Wall-clock kill: SIGTERM first, then SIGKILL 2s later if it refuses.
    const killTimer = setTimeout(() => {
      timedOut = true;
      try { proc.kill('SIGTERM'); } catch {}
      forceKillTimer = setTimeout(() => { try { proc.kill('SIGKILL'); } catch {} }, 2000);
    }, timeoutMs);

    proc.stdout.setEncoding('utf8');
    proc.stderr.setEncoding('utf8');
    proc.stdout.on('data', c => { stdout += c; });
    proc.stderr.on('data', c => { stderr = (stderr + c).slice(-16_384); });
    proc.on('error', err => rejectOnce(spawnError(err)));
    proc.on('close', (code, signal) => {
      if (timedOut) {
        return rejectOnce(new Error(
          `Video info fetch timed out after ${Math.round(timeoutMs / 1000)}s. ` +
          `This URL or the host CDN is too slow for our 1-CPU server — please try again or try a different URL.`
        ));
      }
      if (code !== 0) return rejectOnce(new Error(failureMessage(stderr, code, signal)));
      const line = stdout.split('\n').find(l => l.trim().startsWith('{'));
      if (!line) return rejectOnce(new Error('yt-dlp returned no JSON output'));
      let raw;
      try { raw = JSON.parse(line); }
      catch { return rejectOnce(new Error('Could not parse yt-dlp JSON output')); }
      try { resolveOnce(normalizeVideoInfo(raw)); }
      catch (error) { rejectOnce(error); }
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
  raw = normalizeVideoInfo(raw);
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

    mkdirSync(SESSION_BASE, { recursive: true });
    const outputDir = mkdtempSync(join(SESSION_BASE, `${session.id}-`));
    session.outputDir = outputDir;

    // Sanitize container to prevent argument injection
    const safeContainer = /^[a-z0-9]+$/i.test(container) ? container : 'mp4';

    const args = [
      ...commonArgs(), ...progressArgs(),
      '--format', formatStr,
      '--merge-output-format', safeContainer,
      // When merging to MP4, force audio transcoding to AAC.
      // VP9+OPUS (common on YouTube) can't be muxed into MP4 — ffmpeg falls
      // back to MKV unless we explicitly re-encode the audio track to AAC.
      // Video is copied as-is (H.264/VP9 both work in MP4 players today).
      ...(safeContainer === 'mp4' ? ['--postprocessor-args', 'Merger+ffmpeg:-c:v copy -c:a aac'] : []),
      '--output', join(outputDir, '%(title).100B.%(ext)s'),
      '--concurrent-fragments', '2',
      '--retries', '5',
      '--fragment-retries', '5',
      '--extractor-retries', '3',
      '--socket-timeout', '30',
      '--', url,
    ];

    const proc = spawn(ytdlp, args, {
      env: enrichedEnv(),
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });
    session.process = proc;

    let resolvedPath = null;
    let stderrBuf = '';
    let launchError = null;
    // Cancellation may immediately start another process on the same session.
    const isCurrent = () => manager.getSession(session.id) === session &&
      session.state === 'downloading' && session.process === proc;

    const handleLine = (line) => {
      if (!isCurrent()) return;
      // Stable machine-readable output; human log wording is not an API.
      if (line.startsWith(FILE_PREFIX)) {
        try {
          const path = JSON.parse(line.slice(FILE_PREFIX.length));
          if (typeof path === 'string') resolvedPath = path;
        } catch { /* completion will report the missing final path */ }
        return;
      }
      if (line.startsWith(POSTPROCESS_PREFIX)) {
        manager.notifyState(session.id, 'merging');
        return;
      }

      const update = parseProgress(line);
      if (update) manager.notifyProgress(session.id, update);
    };

    createInterface({ input: proc.stdout, crlfDelay: Infinity }).on('line', handleLine);
    createInterface({ input: proc.stderr, crlfDelay: Infinity }).on('line', handleLine);
    proc.stderr.setEncoding('utf8');
    proc.stderr.on('data', chunk => {
      stderrBuf = (stderrBuf + chunk).slice(-16_384);
    });
    proc.on('error', err => { launchError = spawnError(err); });

    proc.on('close', (code, signal) => {
      if (!isCurrent()) {
        // The old child may have recreated a partial file after cancellation cleanup.
        try { rmSync(outputDir, { recursive: true, force: true }); } catch {}
        resolve();
        return;
      }
      session.process = null;

      if (launchError) { reject(launchError); return; }
      if (code !== 0) {
        reject(new Error(failureMessage(stderrBuf, code, signal)));
        return;
      }

      // Only after_move identifies the completed artifact after postprocessor renames.
      let candidate = null;
      if (resolvedPath) {
        const fullPath = resolvePath(resolvedPath);
        const rel = relative(outputDir, fullPath);
        try {
          if (rel && rel !== '..' && !rel.startsWith(`..${sep}`) && !isAbsolute(rel) && statSync(fullPath).isFile()) {
            candidate = fullPath;
          }
        } catch { /* missing final artifact */ }
      }

      if (candidate) {
        manager.notifyCompleted(session.id, candidate);
        resolve();
      } else {
        reject(new Error(`No completed output file was found. ${failureMessage(stderrBuf, code, signal)}`));
      }
    });
  });
}

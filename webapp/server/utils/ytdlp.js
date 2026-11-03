import { existsSync } from 'node:fs';
import { delimiter } from 'node:path';

export const FILE_PREFIX = '__ANY_FILE__';
export const PROGRESS_PREFIX = '__ANY_PROGRESS__';
export const POSTPROCESS_PREFIX = '__ANY_POSTPROCESS__';

export function enrichedEnv() {
  const env = { ...process.env };
  if (process.platform !== 'win32') {
    env.PATH = ['/opt/homebrew/bin', '/usr/local/bin', env.PATH ?? '/usr/bin:/bin'].join(delimiter);
  }
  return env;
}

// An explicit override wins; otherwise let spawn use the platform's PATH lookup.
export function findYtDlp() {
  if (process.env.YTDLP_PATH) return process.env.YTDLP_PATH;
  for (const path of ['/opt/homebrew/bin/yt-dlp', '/usr/local/bin/yt-dlp', '/usr/bin/yt-dlp']) {
    if (existsSync(path)) return path;
  }
  return process.platform === 'win32' ? 'yt-dlp.exe' : 'yt-dlp';
}

export function commonArgs() {
  return [
    '--ignore-config', '--no-playlist', '--playlist-items', '1', '--encoding', 'utf-8',
    // Node is already running this server, but yt-dlp only enables Deno by default.
    '--js-runtimes', `node:${process.execPath}`,
  ];
}

// Some multi-video extractors return an entries envelope even with --no-playlist.
// The same --playlist-items 1 policy is used for both metadata and downloads.
export function normalizeVideoInfo(raw) {
  for (let depth = 0; depth < 16; depth++) {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
      throw new Error('yt-dlp returned invalid video metadata or an unavailable first video.');
    }
    if (Object.hasOwn(raw, 'entries')) {
      if (!Array.isArray(raw.entries) || raw.entries.length === 0) {
        throw new Error('yt-dlp returned no playable first video in this collection.');
      }
      raw = raw.entries[0];
      continue;
    }
    if (raw._type === 'playlist' || raw._type === 'multi_video') {
      throw new Error('yt-dlp returned a video collection without entries.');
    }
    if (Object.hasOwn(raw, 'formats') && (!Array.isArray(raw.formats) ||
        raw.formats.some(f => !f || typeof f !== 'object' || Array.isArray(f)))) {
      throw new Error('yt-dlp returned invalid video formats.');
    }
    if (!raw.id && !raw.title && !raw.url && !Array.isArray(raw.formats)) {
      throw new Error('yt-dlp returned incomplete video metadata.');
    }
    return raw;
  }
  throw new Error('yt-dlp returned excessively nested video collections.');
}

export function progressArgs() {
  return [
    '--no-simulate', '--progress', '--newline', '--no-colors', '--abort-on-error',
    // Missing fields bypass JSON conversion; the default bare NA is not valid JSON.
    '--output-na-placeholder', 'null',
    '--progress-template', `download:${PROGRESS_PREFIX}{"downloaded":%(progress.downloaded_bytes)j,"total":%(progress.total_bytes)j,"estimate":%(progress.total_bytes_estimate)j,"speed":%(progress.speed)j,"eta":%(progress.eta)j}`,
    '--progress-template', `postprocess:${POSTPROCESS_PREFIX}%(progress.status)s`,
    '--print', `after_move:${FILE_PREFIX}%(filepath)j`,
  ];
}

export function parseProgress(line) {
  if (!line.startsWith(PROGRESS_PREFIX)) return null;
  try {
    const p = JSON.parse(line.slice(PROGRESS_PREFIX.length));
    const numeric = n => typeof n === 'number' && Number.isFinite(n) && n >= 0;
    const total = numeric(p.total) && p.total > 0 ? p.total : p.estimate;
    return {
      progress: numeric(total) && total > 0 && numeric(p.downloaded)
        ? Math.min(p.downloaded / total, 1) : 0,
      speed: numeric(p.speed) ? Math.round(p.speed) : 0,
      eta: numeric(p.eta) ? Math.round(p.eta) : null,
    };
  } catch { return null; }
}

export function spawnError(error) {
  if (error.code === 'ENOENT') {
    return new Error('yt-dlp could not be started. Install yt-dlp and its required Python runtime (for the Unix launcher), or set YTDLP_PATH to a working executable.');
  }
  return new Error(`Could not start yt-dlp: ${error.message}`);
}

export function failureMessage(stderr, code, signal) {
  const details = stderr.trim();
  let hint = '';
  if (/no such option: --js-runtimes/i.test(details)) {
    hint = 'Update yt-dlp; this version does not support --js-runtimes. ';
  } else if (/ffmpeg.*not (found|installed)|ffprobe.*not (found|installed)/i.test(details)) {
    hint = 'Install ffmpeg and ffprobe on the server PATH, then retry. ';
  } else if (/JavaScript runtime|challenge solving failed|yt-dlp-ejs/i.test(details)) {
    hint = 'Check Node 22+ and update yt-dlp with its matching yt-dlp-ejs scripts. ';
  }
  const status = signal ? `terminated by ${signal}` : `exited with code ${code}`;
  return `${hint}yt-dlp ${status}${details ? `:\n${details}` : '.'}`;
}

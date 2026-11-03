/**
 * Strict URL / input validation for the AnyDownloader API.
 *
 * Protects against:
 *   - Empty / whitespace-only input
 *   - Oversized input (DoS / memory exhaustion)
 *   - Null-byte injection
 *   - Shell-injection metacharacters (backtick, $ braces, semicolons, pipes …)
 *   - Dangerous URL schemes (file://, javascript:, data:, ftp://, magnet:)
 *   - Torrent file downloads (.torrent URLs)
 *   - SSRF via private / loopback / link-local network addresses
 *
 * Note: magnet links and .torrent downloads are blocked — web app supports
 * HTTP/HTTPS URLs only (video sites via yt-dlp).
 */

const MAX_URL_LENGTH = 2048;

/**
 * Characters that have no valid place in a URL but signal shell-injection attempts.
 * Note: & = ? # % are all legitimate URL characters and are NOT blocked.
 */
const SHELL_DANGER_RE = /[`${}|;]/;

const ALLOWED_SCHEMES = new Set(['https:', 'http:']);

/** Reject .torrent file downloads regardless of scheme. */
const TORRENT_FILE_RE = /\.torrent(\?.*)?$/i;

/** RFC-1918 / loopback / link-local patterns for SSRF protection. */
const PRIVATE_HOST_RE = [
  /^localhost(\.localdomain)?$/i,
  /^127\./,
  /^10\./,
  /^192\.168\./,
  /^172\.(1[6-9]|2\d|3[01])\./,
  /^169\.254\./,          // AWS metadata, link-local
  /^::1$/,                // IPv6 loopback
  /^fc[0-9a-f]{2}:/i,    // IPv6 unique-local fc00::/7
  /^fd[0-9a-f]{2}:/i,
];

/**
 * Validates a raw user-supplied URL string.
 *
 * @param {string} raw  - The raw input from the user.
 * @returns {string}    - The trimmed, validated URL ready for use.
 * @throws {Error}      - Descriptive error suitable for showing to the user.
 */
export function validateUrl(raw) {
  if (raw === null || raw === undefined || typeof raw !== 'string') {
    throw new Error('Input must be a non-empty string.');
  }

  const trimmed = raw.trim();

  if (trimmed.length === 0) {
    throw new Error('Please enter a video URL.');
  }

  if (trimmed.length > MAX_URL_LENGTH) {
    throw new Error(`Input exceeds the maximum allowed length of ${MAX_URL_LENGTH} characters.`);
  }

  if (trimmed.includes('\0')) {
    throw new Error('Input contains invalid characters (null bytes).');
  }

  if (SHELL_DANGER_RE.test(trimmed)) {
    throw new Error('Input contains unsafe characters that are not allowed in a URL.');
  }

  // ── Block magnet links ─────────────────────────────────────────────────────
  if (trimmed.toLowerCase().startsWith('magnet:')) {
    throw new Error('Magnet links are not supported. Please use a direct video URL (YouTube, TikTok, Bilibili, etc.).');
  }

  // ── Reject triple-slash (missing host) before WHATWG parser ───────────────
  // new URL('https:///file.zip') parses as hostname:"file.zip" in Node.js,
  // so the empty-hostname check below would miss it — catch it here first.
  if (/^https?:\/\/\//.test(trimmed)) {
    throw new Error('Malformed URL: missing hostname.');
  }

  // ── Standard URL parsing ───────────────────────────────────────────────────
  let parsed;
  try {
    parsed = new URL(trimmed);
  } catch {
    throw new Error('Malformed URL: could not be parsed. Please check it and try again.');
  }

  const scheme = parsed.protocol.toLowerCase();
  if (!ALLOWED_SCHEMES.has(scheme)) {
    const name = scheme.replace(/:$/, '');
    throw new Error(
      `'${name}' is not a supported URL scheme. Please use an https or http video URL.`
    );
  }

  // ── Block .torrent file downloads ─────────────────────────────────────────
  if (TORRENT_FILE_RE.test(parsed.pathname)) {
    throw new Error('Torrent file downloads are not supported. Please use a direct video URL.');
  }

  // ── Require a non-empty host for http/https ──────────────────────────────
  if ((scheme === 'https:' || scheme === 'http:') && !parsed.hostname) {
    throw new Error('Malformed URL: missing hostname.');
  }

  // ── SSRF protection ────────────────────────────────────────────────────────
  const host = parsed.hostname.toLowerCase();
  if (PRIVATE_HOST_RE.some((re) => re.test(host))) {
    throw new Error('Downloading from private or internal network addresses is not allowed.');
  }

  return trimmed;
}

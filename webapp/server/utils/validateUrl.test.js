/**
 * Tests for validateUrl() — uses Node.js built-in test runner (node:test).
 * Run with:  node --experimental-vm-modules --test server/utils/validateUrl.test.js
 *       or:  npm test
 */

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { validateUrl } from './validateUrl.js';

// ── Helpers ─────────────────────────────────────────────────────────────────

function expectValid(url, expectedResult = url.trim()) {
  const result = validateUrl(url);
  assert.equal(result, expectedResult, `Expected '${url}' to be valid`);
}

function expectInvalid(url, msgFragment = '') {
  assert.throws(
    () => validateUrl(url),
    (err) => {
      if (msgFragment) {
        assert.ok(
          err.message.toLowerCase().includes(msgFragment.toLowerCase()),
          `Error for '${url}' should mention '${msgFragment}', got: ${err.message}`
        );
      }
      return true;
    },
    `Expected '${url}' to be rejected`
  );
}

// ── Valid inputs ─────────────────────────────────────────────────────────────

describe('valid inputs', () => {
  it('accepts HTTPS URL', () => expectValid('https://example.com/file.zip'));
  it('accepts HTTP URL',  () => expectValid('http://example.com/file.mp4'));
  it('accepts HTTPS with query string', () =>
    expectValid('https://example.com/file.zip?token=abc123&v=2'));
  it('accepts YouTube URL', () =>
    expectValid('https://www.youtube.com/watch?v=dQw4w9WgXcQ'));
  it('accepts Bilibili URL', () =>
    expectValid('https://www.bilibili.com/video/BV1xx411c7mD'));
  it('accepts well-formed magnet link', () =>
    expectValid('magnet:?xt=urn:btih:da39a3ee5e6b4b0d3255bfef95601890afd80709&dn=test&tr=udp://tracker.example.com:6969'));
  it('trims surrounding whitespace', () =>
    expectValid('  https://example.com/file.zip  ', 'https://example.com/file.zip'));
});

// ── Empty / null / undefined ─────────────────────────────────────────────────

describe('empty / null inputs', () => {
  it('rejects empty string',      () => expectInvalid('', 'enter'));
  it('rejects whitespace only',   () => expectInvalid('   ', 'enter'));
  it('rejects null',              () => expectInvalid(null));
  it('rejects undefined',         () => expectInvalid(undefined));
  it('rejects number type',       () => expectInvalid(42));
  it('rejects array type',        () => expectInvalid(['https://example.com']));
});

// ── Length limits ─────────────────────────────────────────────────────────────

describe('length limits', () => {
  it('rejects URL over 2048 chars', () => {
    const huge = 'https://example.com/' + 'a'.repeat(2050);
    expectInvalid(huge, 'maximum');
  });

  it('accepts URL exactly at 2048 chars', () => {
    const base = 'https://example.com/';
    const url = base + 'a'.repeat(2048 - base.length);
    expectValid(url);
  });
});

// ── Null-byte injection ───────────────────────────────────────────────────────

describe('null-byte injection', () => {
  it('rejects URL with embedded null byte', () =>
    expectInvalid('https://example.com/\0file.zip', 'null'));
  it('rejects null byte at start', () =>
    expectInvalid('\0https://example.com/', 'null'));
  it('rejects null byte at end', () =>
    expectInvalid('https://example.com/file\0', 'null'));
});

// ── Shell-injection characters ────────────────────────────────────────────────

describe('shell injection', () => {
  it('rejects backtick',        () => expectInvalid('https://example.com/`whoami`', 'unsafe'));
  it('rejects dollar sign',     () => expectInvalid('https://example.com/${HOME}', 'unsafe'));
  it('rejects semicolon',       () => expectInvalid('https://example.com/;rm -rf /', 'unsafe'));
  it('rejects pipe',            () => expectInvalid('https://example.com/|cat /etc/passwd', 'unsafe'));
  it('rejects opening brace',   () => expectInvalid('https://example.com/{evil}', 'unsafe'));
  // These should NOT be rejected (legitimate URL chars)
  it('allows & in query string', () => expectValid('https://example.com/?a=1&b=2'));
  it('allows = in query string', () => expectValid('https://example.com/?key=value'));
  it('allows # fragment',        () => expectValid('https://example.com/page#section'));
  it('allows % encoding',        () => expectValid('https://example.com/file%20name.zip'));
});

// ── Dangerous schemes ─────────────────────────────────────────────────────────

describe('dangerous schemes', () => {
  it('rejects javascript: (XSS)',  () => expectInvalid('javascript:alert(1)', 'scheme'));
  it('rejects file:// (LFI)',      () => expectInvalid('file:///etc/passwd', 'scheme'));
  it('rejects data: (injection)',  () =>
    expectInvalid('data:text/html,<script>alert(1)</script>', 'scheme'));
  it('rejects ftp://',             () => expectInvalid('ftp://files.example.com/file.zip', 'scheme'));
  it('rejects blob://',            () => expectInvalid('blob:https://example.com/...', 'scheme'));
});

// ── SSRF — private network ────────────────────────────────────────────────────

describe('SSRF protection', () => {
  it('rejects loopback 127.0.0.1',          () => expectInvalid('https://127.0.0.1/', 'private'));
  it('rejects loopback 127.1.2.3',          () => expectInvalid('https://127.1.2.3/secret', 'private'));
  it('rejects localhost',                   () => expectInvalid('https://localhost/', 'private'));
  it('rejects RFC-1918 192.168.x.x',        () => expectInvalid('https://192.168.0.1/', 'private'));
  it('rejects RFC-1918 10.x.x.x',           () => expectInvalid('https://10.0.0.1/', 'private'));
  it('rejects RFC-1918 172.16.x.x',         () => expectInvalid('https://172.16.0.1/', 'private'));
  it('rejects RFC-1918 172.31.x.x',         () => expectInvalid('https://172.31.255.255/', 'private'));
  it('rejects link-local 169.254.169.254',  () =>
    expectInvalid('https://169.254.169.254/latest/meta-data/', 'private'));
});

// ── Malformed URLs ────────────────────────────────────────────────────────────

describe('malformed URLs', () => {
  it('rejects bare text',          () => expectInvalid('not a url at all'));
  it('rejects bare domain',        () => expectInvalid('example.com/file.zip'));
  it('rejects missing host (triple slash)',  () => expectInvalid('https:///file.zip', 'hostname'));
});

// ── Magnet URI edge cases ─────────────────────────────────────────────────────

describe('magnet URI validation', () => {
  it('rejects magnet with no xt', () =>
    expectInvalid('magnet:?dn=somefilm', 'xt=urn:btih:'));
  it('rejects magnet with wrong urn', () =>
    expectInvalid('magnet:?xt=urn:sha1:abc123', 'xt=urn:btih:'));
  it('rejects magnet with no hex hash (empty after colon)', () =>
    expectInvalid('magnet:?xt=urn:btih:', 'info-hash'));
  it('rejects magnet with hash too short', () =>
    expectInvalid('magnet:?xt=urn:btih:abc', 'info-hash'));
  it('accepts magnet with full 40-char SHA-1 hash', () =>
    expectValid('magnet:?xt=urn:btih:' + 'a'.repeat(40) + '&dn=test'));
});

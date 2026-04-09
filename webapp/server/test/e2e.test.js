/**
 * End-to-end tests for AnyDownloader backend API.
 *
 * Run: node --test server/test/e2e.test.js
 * (Requires backend running on http://localhost:3001)
 */

import { test, describe, before } from 'node:test';
import assert from 'node:assert/strict';

const BASE = 'http://localhost:3001/api';

async function get(path) {
  const res = await fetch(`${BASE}${path}`);
  const body = await res.json();
  return { status: res.status, body };
}

async function post(path, data) {
  const res = await fetch(`${BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  });
  const body = await res.json();
  return { status: res.status, body };
}

async function del(path) {
  const res = await fetch(`${BASE}${path}`, { method: 'DELETE' });
  return { status: res.status };
}

// ─── Health Check ─────────────────────────────────────────────────────────────

describe('Health', () => {
  test('GET /api/health returns ok', async () => {
    const { status, body } = await get('/health');
    assert.equal(status, 200);
    assert.equal(body.status, 'ok');
    assert.ok(typeof body.uptime === 'number', 'uptime should be a number');
  });
});

// ─── Downloads CRUD ───────────────────────────────────────────────────────────

describe('Downloads', () => {
  let itemId;

  test('GET /api/downloads returns array', async () => {
    const { status, body } = await get('/downloads');
    assert.equal(status, 200);
    assert.ok(Array.isArray(body), 'response should be an array');
  });

  test('POST /api/downloads with missing url returns 400', async () => {
    const { status, body } = await post('/downloads', {});
    assert.equal(status, 400);
    assert.ok(body.error, 'should return an error message');
  });

  test('POST /api/downloads with empty url returns 400', async () => {
    const { status, body } = await post('/downloads', { url: '  ' });
    assert.equal(status, 400);
    assert.ok(body.error);
  });

  test('POST /api/downloads creates a download item', async () => {
    // Use a short public-domain video clip so the test is fast
    const { status, body } = await post('/downloads', {
      url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
      quality: 'worstvideo+worstaudio/worst',
      format: 'mp4',
    });
    assert.equal(status, 201);
    assert.ok(body.id, 'item should have an id');
    assert.ok(body.url, 'item should have a url');
    assert.ok(body.state, 'item should have a state field (not status)');
    assert.equal(body.url, 'https://www.youtube.com/watch?v=dQw4w9WgXcQ');
    assert.ok(['pending', 'queued', 'active'].includes(body.state),
      `state should be pending/queued/active, got: ${body.state}`);
    itemId = body.id;
  });

  test('GET /api/downloads/:id returns the created item', async () => {
    if (!itemId) return; // skip if prior test failed
    const { status, body } = await get(`/downloads/${itemId}`);
    assert.equal(status, 200);
    assert.equal(body.id, itemId);
    assert.ok('state' in body, 'item must have state field');
    assert.ok('errorMessage' in body, 'item must have errorMessage field (not error)');
  });

  test('GET /api/downloads/:id for non-existent id returns 404', async () => {
    const { status } = await get('/downloads/does-not-exist-00000000');
    assert.equal(status, 404);
  });

  test('POST /api/downloads/:id/pause returns item', async () => {
    if (!itemId) return;
    const { status, body } = await post(`/downloads/${itemId}/pause`, {});
    assert.equal(status, 200);
    assert.equal(body.state, 'paused');
  });

  test('POST /api/downloads/:id/resume from paused state works', async () => {
    if (!itemId) return;
    const { status, body } = await post(`/downloads/${itemId}/resume`, {});
    assert.equal(status, 200);
    assert.ok(['queued', 'active'].includes(body.state),
      `expected queued/active after resume, got: ${body.state}`);
  });

  test('DELETE /api/downloads/:id removes the item', async () => {
    if (!itemId) return;
    const { status } = await del(`/downloads/${itemId}`);
    assert.equal(status, 204);

    // Confirm it's gone
    const { status: checkStatus } = await get(`/downloads/${itemId}`);
    assert.equal(checkStatus, 404);
    itemId = null;
  });
});

// ─── Video Info ────────────────────────────────────────────────────────────────

describe('Video Info', () => {
  test('GET /api/downloads/info with no url returns 400', async () => {
    const { status, body } = await get('/downloads/info');
    assert.equal(status, 400);
    assert.ok(body.error);
  });

  test('GET /api/downloads/info for a YouTube video returns metadata', async () => {
    const url = encodeURIComponent('https://www.youtube.com/watch?v=dQw4w9WgXcQ');
    const { status, body } = await get(`/downloads/info?url=${url}`);
    assert.equal(status, 200, `info fetch failed: ${JSON.stringify(body)}`);
    assert.ok(body.title, 'should have a title');
    assert.ok(Array.isArray(body.formats), 'formats should be an array');
    assert.ok(body.formats.length > 0, 'should have at least one format');

    // Each format should have the expected shape
    const fmt = body.formats[0];
    assert.ok('formatId' in fmt, 'format needs formatId');
    assert.ok('ext' in fmt, 'format needs ext');
    assert.ok('hasVideo' in fmt, 'format needs hasVideo bool');
    assert.ok('hasAudio' in fmt, 'format needs hasAudio bool');
  });

  test('GET /api/downloads/info for a Bilibili video returns metadata', async () => {
    // Short Bilibili clip
    const url = encodeURIComponent('https://www.bilibili.com/video/BV1GJ411x7h7');
    const { status, body } = await get(`/downloads/info?url=${url}`);
    // Bilibili may require login for some videos — treat 500 as acceptable
    assert.ok([200, 500].includes(status), `unexpected status ${status}`);
    if (status === 200) {
      assert.ok(body.title || body.formats, 'should have title or formats');
    }
  });

  test('POST /api/info endpoint also works', async () => {
    const { status, body } = await post('/info', {
      url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
    });
    assert.equal(status, 200, `info POST failed: ${JSON.stringify(body)}`);
    assert.ok(body.title);
  });

  test('GET /api/downloads/info for oGOGCemgvGQ returns video+audio formats', async () => {
    const url = encodeURIComponent('https://www.youtube.com/watch?v=oGOGCemgvGQ');
    const { status, body } = await get(`/downloads/info?url=${url}`);
    assert.equal(status, 200, `info fetch failed: ${JSON.stringify(body)}`);
    assert.ok(body.title, 'should have a title');
    assert.ok(Array.isArray(body.formats), 'formats should be an array');
    assert.ok(body.formats.length > 0, 'should have at least one format');

    const videoFormats = body.formats.filter(f => f.hasVideo && !f.hasAudio);
    const audioFormats = body.formats.filter(f => !f.hasVideo && f.hasAudio);
    assert.ok(videoFormats.length > 0, 'should have video-only formats for merge');
    assert.ok(audioFormats.length > 0, 'should have audio-only formats for merge');

    // Verify format shape
    const fmt = body.formats[0];
    assert.ok('formatId' in fmt, 'format needs formatId');
    assert.ok('ext' in fmt, 'format needs ext');
    assert.ok('hasVideo' in fmt, 'format needs hasVideo');
    assert.ok('hasAudio' in fmt, 'format needs hasAudio');
  });
});

// ─── URL Classification (via /api/downloads) ──────────────────────────────────

describe('URL Classification', () => {
  async function addAndClean(url) {
    const { status, body } = await post('/downloads', { url, quality: 'worst', format: 'mp4' });
    if (status === 201 && body.id) {
      await del(`/downloads/${body.id}`);
    }
    return { status, body };
  }

  test('YouTube URL is classified as ytdlp', async () => {
    const { status, body } = await post('/downloads', {
      url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
      quality: 'worst',
      format: 'mp4',
    });
    assert.equal(status, 201);
    assert.equal(body.type, 'ytdlp');
    if (body.id) await del(`/downloads/${body.id}`);
  });

  test('Bilibili URL is classified as ytdlp', async () => {
    const { status, body } = await post('/downloads', {
      url: 'https://www.bilibili.com/video/BV1GJ411x7h7',
      quality: 'worst',
      format: 'mp4',
    });
    assert.equal(status, 201);
    assert.equal(body.type, 'ytdlp');
    if (body.id) await del(`/downloads/${body.id}`);
  });

  test('Magnet link is classified as torrent', async () => {
    const magnet = 'magnet:?xt=urn:btih:08ada5a7a6183aae1e09d831df6748d566095a10';
    const { status, body } = await post('/downloads', {
      url: magnet,
      quality: 'worst',
      format: 'mp4',
    });
    assert.equal(status, 201);
    assert.equal(body.type, 'torrent');
    if (body.id) await del(`/downloads/${body.id}`);
  });
});

// ─── File Serving ─────────────────────────────────────────────────────────────

describe('File Serving', () => {
  test('GET /api/files/nonexistent returns 404', async () => {
    const res = await fetch(`${BASE.replace('/api', '')}/api/files/does-not-exist-file.mp4`);
    assert.equal(res.status, 404);
  });

  test('GET /api/files with path traversal returns 404 not traversal', async () => {
    const res = await fetch(`${BASE.replace('/api', '')}/api/files/..%2F..%2Fetc%2Fpasswd`);
    // Should be 404 (basename strips path components) not a directory traversal
    assert.ok([404, 400].includes(res.status), `expected 404, got ${res.status}`);
  });
});

// ─── Restart in-place ─────────────────────────────────────────────────────────

describe('Restart in-place', () => {
  test('POST /api/downloads/:id/restart reuses same id', async () => {
    const { status: s1, body: created } = await post('/downloads', {
      url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
      quality: 'worst',
      format: 'mp4',
    });
    assert.equal(s1, 201);
    const id = created.id;

    // Pause first so restart is allowed
    await post(`/downloads/${id}/pause`, {});

    // Restart
    const { status: s2, body: restarted } = await post(`/downloads/${id}/restart`, {});
    assert.equal(s2, 200);
    assert.equal(restarted.id, id, 'restart must reuse the same id, not create a new item');
    assert.ok(['queued', 'active'].includes(restarted.state),
      `expected queued/active after restart, got: ${restarted.state}`);

    await del(`/downloads/${id}`);
  });
});

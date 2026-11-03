// Opt in: set YTDLP_PATH to the official executable, then npm run test:ytdlp-live.
// Public CC0 sample, no accounts/cookies. Cancellation uses our own local server.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { readFileSync, existsSync, rmSync, copyFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { fetchVideoInfo, startSessionDownload } from '../downloader.js';
import { SessionManager } from '../sessionManager.js';

async function until(predicate, timeout = 30_000) {
  const deadline = Date.now() + timeout;
  while (!predicate()) {
    if (Date.now() > deadline) throw new Error('Timed out waiting for download event');
    await delay(20);
  }
}

test('real yt-dlp: CC0 download, cancel/retry, one-time delivery, missing binary',
  { skip: !process.env.YTDLP_PATH, timeout: 120_000 }, async t => {
    const url = 'https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4';
    let sample;
    await t.test('public CC0 media: metadata and complete artifact', async () => {
      const info = await fetchVideoInfo(url, { timeoutMs: 30_000 });
      assert.ok(info.title);
      const session = { id: crypto.randomUUID(), state: 'downloading' };
      const progress = [];
      let artifact;
      const manager = {
        getSession: () => session,
        notifyState() {},
        notifyProgress: (_, p) => progress.push(p),
        notifyCompleted: (_, path) => { artifact = path; },
      };
      try {
        await startSessionDownload(session, { url, formatStr: 'best', container: 'mp4' }, manager);
        assert.ok(artifact);
        sample = readFileSync(artifact);
        assert.ok(sample.length > 100_000);
        assert.equal(sample.subarray(4, 8).toString(), 'ftyp');
        assert.ok(progress.some(p => p.progress === 1));
        if (process.env.YTDLP_EVIDENCE_DIR) copyFileSync(artifact, join(process.env.YTDLP_EVIDENCE_DIR, basename(artifact)));
        console.log(`CC0 artifact: ${basename(artifact)}, ${sample.length} bytes; ${progress.length} progress events`);
      } finally {
        if (session.process) session.process.kill();
        rmSync(session.outputDir, { recursive: true, force: true });
      }
    });

    await t.test('multi-video HTML metadata and download both select the first CC0 clip', async () => {
      assert.ok(sample, 'public sample must have downloaded');
      const requested = [];
      const server = createServer((req, res) => {
        requested.push(req.url);
        if (req.url === '/post') {
          res.writeHead(200, { 'content-type': 'text/html' });
          res.end('<html><title>Two clips</title><video src="/first.mp4"></video><video src="/second.mp4"></video></html>');
        } else {
          res.writeHead(200, { 'content-type': 'video/mp4', 'content-length': sample.length });
          res.end(req.method === 'HEAD' ? undefined : sample);
        }
      });
      server.listen(0, '127.0.0.1'); await once(server, 'listening');
      const url = `http://127.0.0.1:${server.address().port}/post`;
      const session = { id: crypto.randomUUID(), state: 'downloading' };
      let artifact;
      try {
        const info = await fetchVideoInfo(url, { timeoutMs: 30_000 });
        assert.ok(info.formats.some(f => f.url.endsWith('/first.mp4')));
        assert.equal(info.entries, undefined);
        const manager = { getSession: () => session, notifyState() {}, notifyProgress() {},
          notifyCompleted: (_, path) => { artifact = path; } };
        await startSessionDownload(session, { url, formatStr: 'best', container: 'mp4' }, manager);
        assert.deepEqual(readFileSync(artifact), sample);
        assert.ok(!requested.includes('/second.mp4'), 'the second clip must never be fetched');
      } finally {
        if (session.process) session.process.kill();
        if (session.outputDir) rmSync(session.outputDir, { recursive: true, force: true });
        server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
      }
    });

    await t.test('cancel and retry on the same session delivers only the new artifact', async () => {
      assert.ok(sample, 'public sample must have downloaded');
      const server = createServer((req, res) => {
        res.writeHead(200, { 'content-type': 'video/mp4', 'content-length': sample.length });
        if (req.method === 'HEAD') { res.end(); return; }
        if (req.url !== '/slow.mp4') { res.end(sample); return; }
        let offset = 0;
        const timer = setInterval(() => {
          res.write(sample.subarray(offset, offset + 8192)); offset += 8192;
          if (offset >= sample.length) { clearInterval(timer); res.end(); }
        }, 25);
        res.on('close', () => clearInterval(timer));
      });
      server.listen(0, '127.0.0.1'); await once(server, 'listening');
      const base = `http://127.0.0.1:${server.address().port}`;
      const manager = new SessionManager();
      const messages = [];
      const id = manager.createSession({ readyState: 1, send: text => messages.push(JSON.parse(text)) });
      const session = manager.getSession(id);
      let firstRun;
      try {
        manager.enqueueDownload(id, s => firstRun = startSessionDownload(s,
          { url: `${base}/slow.mp4`, formatStr: 'best', container: 'mp4' }, manager));
        await until(() => messages.some(m => m.type === 'progress'));
        const oldDir = session.outputDir;
        manager.cancelDownload(id);
        assert.equal(manager.enqueueDownload(id, s => startSessionDownload(s,
          { url: `${base}/sample.mp4`, formatStr: 'best', container: 'mp4' }, manager)), true);
        assert.notEqual(session.outputDir, oldDir);
        await until(() => messages.some(m => m.type === 'completed' || m.type === 'error'));
        await firstRun;
        assert.equal(messages.filter(m => m.type === 'error').length, 0, JSON.stringify(messages));
        const completed = messages.filter(m => m.type === 'completed');
        assert.equal(completed.length, 1);
        const entry = manager.consumeToken(completed[0].token);
        assert.ok(entry);
        assert.equal(entry.filename, 'sample.mp4');
        assert.deepEqual(readFileSync(entry.filePath), sample);
        assert.equal(manager.consumeToken(completed[0].token), null);
        assert.equal(existsSync(oldDir), false);
        console.log('Cancel/retry: one completion, matching bytes, old attempt cleaned, token single-use');
      } finally {
        manager.cleanupSession(id);
        server.closeAllConnections();
        await new Promise(resolve => server.close(resolve));
      }
    });

    await t.test('missing executable reports setup guidance', async () => {
      const original = process.env.YTDLP_PATH;
      process.env.YTDLP_PATH = join(process.cwd(), 'missing-yt-dlp-executable');
      try { await assert.rejects(fetchVideoInfo(url), /YTDLP_PATH/); }
      finally { process.env.YTDLP_PATH = original; }
    });
  });

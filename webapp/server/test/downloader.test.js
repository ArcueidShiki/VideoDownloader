import { test } from 'node:test';
import assert from 'node:assert/strict';
import childProcess from 'node:child_process';
import fs from 'node:fs';
import { syncBuiltinESMExports } from 'node:module';
import { EventEmitter } from 'node:events';
import { PassThrough } from 'node:stream';
import { join } from 'node:path';
import { fetchVideoInfo, extractFormats, startSessionDownload } from '../downloader.js';

// Model real child-process events, including pipe chunks that split UTF-8/lines.
function child(t) {
  t.after(() => { t.mock.restoreAll(); syncBuiltinESMExports(); });
  const proc = new EventEmitter();
  proc.stdout = new PassThrough();
  proc.stderr = new PassThrough();
  proc.kill = t.mock.fn(() => true);
  const exists = fs.existsSync;
  t.mock.method(fs, 'existsSync', p => p === '/opt/homebrew/bin/yt-dlp' || exists(p));
  const spawn = t.mock.method(childProcess, 'spawn', () => proc);
  syncBuiltinESMExports();
  return { proc, spawn, close(code = 0, signal = null) {
    proc.stdout.end(); proc.stderr.end(); proc.emit('close', code, signal);
  } };
}

function download(t) {
  const fake = child(t);
  const session = { id: `regression-${crypto.randomUUID()}`, state: 'downloading' };
  const manager = {
    getSession: () => session,
    notifyProgress: t.mock.fn(), notifyState: t.mock.fn(), notifyCompleted: t.mock.fn(),
  };
  const promise = startSessionDownload(session,
    { url: 'https://example.com/sample.mp4', formatStr: 'best', container: 'mp4' }, manager);
  t.after(() => fs.rmSync(session.outputDir, { recursive: true, force: true }));
  return { ...fake, session, manager, promise };
}

test('metadata enables the running Node executable and isolates config', async t => {
  const fake = child(t);
  const result = fetchVideoInfo('https://example.com/sample.mp4');
  const [exe, args] = fake.spawn.mock.calls[0].arguments;
  assert.ok(exe);
  assert.ok(args.includes('--ignore-config'));
  assert.equal(args[args.indexOf('--js-runtimes') + 1], `node:${process.execPath}`);
  assert.ok(!args.includes('--no-warnings'));
  assert.ok(!args.includes('--no-check-certificate'));
  fake.proc.stdout.write('{"title":"sample"}\n'); fake.close();
  assert.equal((await result).title, 'sample');
});

test('metadata retains the actual error and dependency warning', async t => {
  const fake = child(t);
  const result = fetchVideoInfo('https://example.com/sample.mp4');
  fake.proc.stderr.write('WARNING: No supported JavaScript runtime could be found\nERROR: Requested format is not available\n');
  fake.close(1);
  await assert.rejects(result, /JavaScript.*\n.*Requested format/s);
});

test('metadata unwraps the first video in nested envelopes and download selects the same entry', async t => {
  const fake = child(t);
  const first = { id: 'first', title: 'First clip', formats: [
    { format_id: 'clip-one', vcodec: 'avc1', acodec: 'aac', height: 720, ext: 'mp4' },
  ] };
  const second = { id: 'second', title: 'Second clip', formats: [] };
  const result = fetchVideoInfo('https://example.com/post');
  const infoArgs = fake.spawn.mock.calls[0].arguments[1];
  fake.proc.stdout.write(JSON.stringify({ _type: 'multi_video', entries: [
    { _type: 'playlist', entries: [first, second] }, second,
  ] }) + '\n');
  fake.close();
  const info = await result;
  assert.equal(info.id, 'first');
  assert.equal(extractFormats(info).videoFormats[0].formatId, 'clip-one');
  assert.equal(infoArgs[infoArgs.indexOf('--playlist-items') + 1], '1');
  const d = download(t);
  const downloadArgs = d.spawn.mock.calls.at(-1).arguments[1];
  assert.equal(downloadArgs[downloadArgs.indexOf('--playlist-items') + 1], '1');
  d.session.state = 'idle'; d.close(null, 'SIGTERM'); await d.promise;
});

test('metadata accepts a one-entry envelope', async t => {
  const fake = child(t);
  const result = fetchVideoInfo('https://example.com/post');
  fake.proc.stdout.write(JSON.stringify({ entries: [{ id: 'only', title: 'Only video', formats: [] }] }) + '\n');
  fake.close();
  assert.equal((await result).id, 'only');
});

for (const [name, raw] of Object.entries({
  empty: { entries: [] }, unavailable: { entries: [null, { title: 'do not silently choose another clip' }] },
  malformedEntries: { entries: 'wrong' }, scalar: 12, incomplete: {}, missingEntries: { _type: 'playlist' },
  malformedFormats: { title: 'bad', formats: [null] },
})) {
  test(`metadata rejects ${name} with a useful error`, async t => {
    const fake = child(t);
    const result = fetchVideoInfo('https://example.com/post');
    fake.proc.stdout.write(JSON.stringify(raw) + '\n'); fake.close();
    await assert.rejects(result, /yt-dlp returned (invalid|no playable|incomplete|a video collection|no JSON)/);
  });
}

test('metadata timeout terminates the child and reports timeout', async t => {
  const fake = child(t);
  const result = fetchVideoInfo('https://example.com/sample.mp4', { timeoutMs: 10 });
  await new Promise(resolve => setTimeout(resolve, 25));
  assert.equal(fake.proc.kill.mock.callCount(), 1);
  fake.close(null, 'SIGTERM');
  await assert.rejects(result, /timed out/);
});

test('download uses structured final path across chunks and split UTF-8', async t => {
  const d = download(t);
  const args = d.spawn.mock.calls[0].arguments[1];
  assert.equal(args[args.indexOf('--js-runtimes') + 1], `node:${process.execPath}`);
  assert.ok(args.includes('--no-simulate'));
  assert.ok(args.includes('--abort-on-error'));
  assert.equal(args[args.indexOf('--output-na-placeholder') + 1], 'null');
  const path = join(d.session.outputDir, 'sample 雪.mp4');
  fs.writeFileSync(path, 'complete media');
  const line = Buffer.from(`__ANY_FILE__${JSON.stringify(path)}\n`);
  for (const byte of line) d.proc.stdout.write(Buffer.from([byte]));
  d.close();
  await d.promise;
  assert.equal(d.manager.notifyCompleted.mock.calls[0].arguments[1], path);
});

test('structured progress supports unknown totals, byte speeds and split lines', async t => {
  const d = download(t);
  d.proc.stdout.write('__ANY_PRO');
  d.proc.stdout.write('GRESS__{"downloaded":50,"total":null,"estimate":100,"speed":12,"eta":null}\n');
  assert.deepEqual(d.manager.notifyProgress.mock.calls[0]?.arguments[1], { progress: 0.5, speed: 12, eta: null });
  d.session.state = 'idle'; d.close(null, 'SIGTERM'); await d.promise;
});

test('signalled process must not report an intermediate file as completed', async t => {
  const d = download(t);
  const path = join(d.session.outputDir, 'intermediate.mp4');
  fs.writeFileSync(path, 'incomplete');
  d.proc.stdout.write(`[download] Destination: ${path}\n`);
  d.close(null, 'SIGTERM');
  await assert.rejects(d.promise, /SIGTERM/);
  assert.equal(d.manager.notifyCompleted.mock.callCount(), 0);
});

test('successful exit without final output does not deliver a stray file', async t => {
  const d = download(t);
  fs.writeFileSync(join(d.session.outputDir, 'thumbnail.jpg'), 'not the video');
  d.close();
  await assert.rejects(d.promise, /output file/);
  assert.equal(d.manager.notifyCompleted.mock.callCount(), 0);
});

test('download error retains ERROR instead of the last unrelated warning', async t => {
  const d = download(t);
  d.proc.stderr.write('ERROR: ffmpeg not found\nWARNING: another diagnostic\n');
  d.close(1);
  await assert.rejects(d.promise, /ffmpeg not found/);
});

test('missing final output retains dependency diagnostics even on exit zero', async t => {
  const d = download(t);
  d.proc.stderr.write('WARNING: ffmpeg is not installed. The formats will not be merged\n');
  d.close();
  await assert.rejects(d.promise, /Install ffmpeg and ffprobe/);
});

test('cancel then immediate retry ignores stale output and close', async t => {
  const d = download(t);
  const replacement = { pid: 42 };
  d.session.process = replacement; // SessionManager has cancelled and started another child.
  d.proc.stdout.write('__ANY_PROGRESS__{"downloaded":50,"total":100,"speed":12,"eta":1}\n');
  d.close(1);
  await d.promise;
  assert.equal(d.session.process, replacement);
  assert.equal(d.manager.notifyProgress.mock.callCount(), 0);
  assert.equal(d.manager.notifyCompleted.mock.callCount(), 0);
});

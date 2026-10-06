import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { syncBuiltinESMExports } from 'node:module';
import { findYtDlp, enrichedEnv, parseProgress, failureMessage, spawnError } from './ytdlp.js';

test('explicit executable override and ordinary PATH lookup are supported', t => {
  const saved = process.env.YTDLP_PATH;
  t.after(() => {
    if (saved === undefined) delete process.env.YTDLP_PATH;
    else process.env.YTDLP_PATH = saved;
    t.mock.restoreAll(); syncBuiltinESMExports();
  });
  process.env.YTDLP_PATH = 'C:\\Tools With Spaces\\yt-dlp.exe';
  assert.equal(findYtDlp(), process.env.YTDLP_PATH);
  delete process.env.YTDLP_PATH;
  t.mock.method(fs, 'existsSync', () => false); syncBuiltinESMExports();
  assert.equal(findYtDlp(), process.platform === 'win32' ? 'yt-dlp.exe' : 'yt-dlp');
});

test('Windows PATH is preserved without Unix colon prefixes', { skip: process.platform !== 'win32' }, () => {
  assert.equal(enrichedEnv().PATH, process.env.PATH);
});

test('progress tolerates unavailable values and ignores unrelated or malformed lines', () => {
  assert.equal(parseProgress('human log output'), null);
  assert.equal(parseProgress('__ANY_PROGRESS__{invalid'), null);
  assert.deepEqual(parseProgress('__ANY_PROGRESS__{"downloaded":1,"total":"NA","estimate":"NA","speed":null,"eta":"NA"}'),
    { progress: 0, speed: 0, eta: null });
  assert.deepEqual(parseProgress('__ANY_PROGRESS__{"downloaded":120,"total":100,"speed":1.6,"eta":2.2}'),
    { progress: 1, speed: 2, eta: 2 });
});

test('missing/old tools produce actionable diagnostics', () => {
  assert.match(spawnError({ code: 'ENOENT' }).message, /YTDLP_PATH/);
  assert.match(failureMessage('yt-dlp: error: no such option: --js-runtimes', 2), /Update yt-dlp/);
  assert.match(failureMessage('ERROR: ffmpeg not found', 1), /Install ffmpeg and ffprobe/);
  assert.match(failureMessage('WARNING: challenge solving failed', 1), /Node 22\+/);
  assert.match(failureMessage('', null, 'SIGKILL'), /SIGKILL/);
});

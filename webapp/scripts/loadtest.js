#!/usr/bin/env node
/**
 * loadtest.js — AnyDownloader server load & memory test
 *
 * Tests that the server stays stable and under the 1 GB RAM budget under
 * concurrent HTTP and WebSocket connections.
 *
 * Usage (run from webapp root — uses server/node_modules/ws):
 *   node --experimental-vm-modules scripts/loadtest.js [options]
 *   # OR: cd server && node ../scripts/loadtest.js [options]
 *
 * If 'ws' is not found, install it: pnpm add -D ws
 *
 * Options:
 *   --host  <url>   Base URL of the backend  (default: http://localhost:3001)
 *   --conc  <n>     Concurrent HTTP workers  (default: 50)
 *   --reqs  <n>     Total requests per phase (default: 500)
 *   --ws    <n>     Concurrent WS connections to hold open (default: 30)
 *
 * Exit codes:
 *   0 — all checks passed, memory within budget
 *   1 — test failed or memory exceeded limit
 */

import http from 'node:http'
import https from 'node:https'
import { parseArgs } from 'node:util'
// ws is a server dependency — resolve from server/node_modules if needed
let WebSocket
try {
  ;({ WebSocket } = await import('ws'))
} catch {
  const { createRequire } = await import('node:module')
  const { fileURLToPath } = await import('node:url')
  const { dirname, resolve } = await import('node:path')
  const __dir = dirname(fileURLToPath(import.meta.url))
  const req = createRequire(resolve(__dir, '../server/index.js'))
  ;({ WebSocket } = req('ws'))
}

// ── CLI args ──────────────────────────────────────────────────────────────────
const { values: args } = parseArgs({
  options: {
    host: { type: 'string',  default: 'http://localhost:3001' },
    conc: { type: 'string',  default: '50' },
    reqs: { type: 'string',  default: '500' },
    ws:   { type: 'string',  default: '30' },
  },
  strict: false,
})

const BASE     = args.host.replace(/\/$/, '')
const CONC     = parseInt(args.conc,  10)
const TOTAL    = parseInt(args.reqs,  10)
const WS_CONC  = parseInt(args.ws,    10)
const MEM_WARN = 750  // MB — warn if server RSS exceeds this
const MEM_FAIL = 900  // MB — fail test if server RSS exceeds this

// ── HTTP helpers ──────────────────────────────────────────────────────────────
function httpGet(url) {
  return new Promise((resolve, reject) => {
    const mod = url.startsWith('https') ? https : http
    const req = mod.get(url, { timeout: 10_000 }, (res) => {
      let body = ''
      res.on('data', d => body += d)
      res.on('end', () => resolve({ status: res.statusCode, body }))
    })
    req.on('error', reject)
    req.on('timeout', () => { req.destroy(); reject(new Error('timeout')) })
  })
}

function httpPost(url, payload) {
  return new Promise((resolve, reject) => {
    const data = JSON.stringify(payload)
    const parsed = new URL(url)
    const mod = url.startsWith('https') ? https : http
    const req = mod.request({
      hostname: parsed.hostname,
      port:     parsed.port || (url.startsWith('https') ? 443 : 80),
      path:     parsed.pathname,
      method:   'POST',
      headers:  { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(data) },
      timeout:  10_000,
    }, (res) => {
      let body = ''
      res.on('data', d => body += d)
      res.on('end', () => resolve({ status: res.statusCode, body }))
    })
    req.on('error', reject)
    req.on('timeout', () => { req.destroy(); reject(new Error('timeout')) })
    req.write(data)
    req.end()
  })
}

// ── Get server memory from /api/health ────────────────────────────────────────
async function getServerMemory() {
  try {
    const { body } = await httpGet(`${BASE}/api/health`)
    const data = JSON.parse(body)
    return data?.memory?.rss_mb ?? null
  } catch {
    return null
  }
}

// ── Concurrent request runner ─────────────────────────────────────────────────
async function runConcurrent(label, fn, total, concurrency) {
  console.log(`\n[${label}] Starting ${total} requests, concurrency=${concurrency}`)
  const start    = Date.now()
  let done       = 0
  let errors     = 0
  let nonOk      = 0
  let inFlight   = 0
  let dispatched = 0

  await new Promise((resolve) => {
    function dispatch() {
      while (inFlight < concurrency && dispatched < total) {
        inFlight++
        dispatched++
        fn()
          .then(({ status }) => {
            if (status >= 400 && status !== 429 && status !== 503) nonOk++
          })
          .catch(() => errors++)
          .finally(() => {
            done++
            inFlight--
            if (done === total) { resolve(undefined); return }
            dispatch()
          })
      }
    }
    dispatch()
  })

  const elapsed = ((Date.now() - start) / 1000).toFixed(2)
  const rps     = (total / parseFloat(elapsed)).toFixed(0)
  console.log(`[${label}] Done in ${elapsed}s — ${rps} req/s — errors: ${errors} — non-2xx: ${nonOk}`)
  return { errors, nonOk }
}

// ── WebSocket hold-open test ──────────────────────────────────────────────────
function openWebSockets(count) {
  const wsUrl = BASE.replace(/^http/, 'ws')
  const sockets = []
  let opened = 0

  return new Promise((resolve) => {
    for (let i = 0; i < count; i++) {
      const ws = new WebSocket(`${wsUrl}/ws`)
      ws.on('open',  () => { opened++; if (opened === count) resolve(sockets) })
      ws.on('error', () => { opened++; if (opened === count) resolve(sockets) })
      sockets.push(ws)
    }
  })
}

function closeWebSockets(sockets) {
  for (const ws of sockets) {
    try { ws.close() } catch {}
  }
}

// ── Main ──────────────────────────────────────────────────────────────────────
async function main() {
  console.log('='.repeat(60))
  console.log('AnyDownloader Load Test')
  console.log(`Target: ${BASE}`)
  console.log(`Concurrency: ${CONC} HTTP | ${WS_CONC} WebSocket`)
  console.log('='.repeat(60))

  // ── 1. Verify server is up ─────────────────────────────────────────────────
  console.log('\n[preflight] Checking server...')
  try {
    const { status, body } = await httpGet(`${BASE}/api/health`)
    if (status !== 200) { console.error(`[preflight] FAIL — /api/health returned ${status}`); process.exit(1) }
    const data = JSON.parse(body)
    console.log(`[preflight] OK — uptime=${data.uptime_s}s, RSS=${data.memory?.rss_mb}MB`)
  } catch (err) {
    console.error(`[preflight] FAIL — ${err.message}`)
    console.error('Is the server running? Start it with: node server/index.js')
    process.exit(1)
  }

  const baseline = await getServerMemory()
  console.log(`[memory] Baseline RSS: ${baseline}MB`)

  let allPassed = true

  // ── 2. Phase 1: GET /api/health flood ─────────────────────────────────────
  const p1 = await runConcurrent(
    'Phase 1 — health endpoint',
    () => httpGet(`${BASE}/api/health`),
    TOTAL,
    CONC,
  )
  allPassed = allPassed && p1.errors === 0

  const afterP1 = await getServerMemory()
  console.log(`[memory] After Phase 1: ${afterP1}MB (Δ ${afterP1 - baseline}MB)`)
  if (afterP1 > MEM_WARN) console.warn(`[memory] WARNING: RSS ${afterP1}MB > ${MEM_WARN}MB threshold`)
  if (afterP1 > MEM_FAIL) { console.error(`[memory] FAIL: RSS ${afterP1}MB exceeds ${MEM_FAIL}MB limit`); allPassed = false }

  // ── 3. Phase 2: POST /api/info with an invalid URL (validation path only) ──
  //    This exercises the request parsing and CORS/auth stack without spawning yt-dlp.
  const p2 = await runConcurrent(
    'Phase 2 — info validation (fast path)',
    () => httpPost(`${BASE}/api/info`, { url: 'not-a-valid-url' }),
    TOTAL,
    CONC,
  )
  // Expect all to be 400 (validation rejection) — those are counted as nonOk but not errors
  allPassed = allPassed && p2.errors === 0

  const afterP2 = await getServerMemory()
  console.log(`[memory] After Phase 2: ${afterP2}MB (Δ ${afterP2 - baseline}MB)`)
  if (afterP2 > MEM_WARN) console.warn(`[memory] WARNING: RSS ${afterP2}MB > ${MEM_WARN}MB threshold`)
  if (afterP2 > MEM_FAIL) { console.error(`[memory] FAIL: RSS ${afterP2}MB exceeds ${MEM_FAIL}MB limit`); allPassed = false }

  // ── 4. Phase 3: WebSocket connections ─────────────────────────────────────
  console.log(`\n[Phase 3 — WebSocket] Opening ${WS_CONC} concurrent connections...`)
  const t0 = Date.now()
  const sockets = await openWebSockets(WS_CONC)
  console.log(`[Phase 3 — WebSocket] ${sockets.length} sockets opened in ${Date.now() - t0}ms`)

  // Hold for 3s then close
  await new Promise(r => setTimeout(r, 3000))
  const afterWS = await getServerMemory()
  console.log(`[memory] With ${WS_CONC} open WS: ${afterWS}MB (Δ ${afterWS - baseline}MB)`)
  if (afterWS > MEM_FAIL) { console.error(`[memory] FAIL: RSS ${afterWS}MB exceeds ${MEM_FAIL}MB limit`); allPassed = false }

  closeWebSockets(sockets)
  await new Promise(r => setTimeout(r, 1000))

  // ── 5. Phase 4: Combined HTTP + WS ────────────────────────────────────────
  console.log('\n[Phase 4 — combined] Running HTTP flood while WS connections held open...')
  const sockets2 = await openWebSockets(WS_CONC)
  const p4 = await runConcurrent(
    'Phase 4 — combined load',
    () => httpGet(`${BASE}/api/health`),
    TOTAL,
    CONC,
  )
  allPassed = allPassed && p4.errors === 0
  closeWebSockets(sockets2)

  const afterP4 = await getServerMemory()
  console.log(`[memory] After Phase 4 combined: ${afterP4}MB (Δ ${afterP4 - baseline}MB)`)
  if (afterP4 > MEM_FAIL) { console.error(`[memory] FAIL: RSS ${afterP4}MB exceeds ${MEM_FAIL}MB limit`); allPassed = false }

  // ── Summary ────────────────────────────────────────────────────────────────
  console.log('\n' + '='.repeat(60))
  console.log('SUMMARY')
  console.log('='.repeat(60))
  console.log(`Baseline RSS  : ${baseline}MB`)
  console.log(`Peak RSS      : ${Math.max(afterP1, afterP2, afterWS, afterP4)}MB`)
  console.log(`Memory limit  : ${MEM_FAIL}MB`)
  console.log(`Result        : ${allPassed ? '✓ PASS' : '✗ FAIL'}`)
  console.log('='.repeat(60))

  process.exit(allPassed ? 0 : 1)
}

main().catch((err) => {
  console.error('[fatal]', err.message)
  process.exit(1)
})

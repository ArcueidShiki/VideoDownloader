/**
 * infoCache.js — In-memory LRU + TTL cache for video info responses.
 *
 * Eviction strategy (in priority order):
 *   1. Expired entries (TTL exceeded) — evicted lazily on access + proactively by a periodic sweep
 *   2. LRU order when entry count exceeds INFO_CACHE_MAX_ENTRIES
 *   3. LRU order when estimated memory footprint exceeds INFO_CACHE_MAX_MB
 *
 * Why TTL matters: yt-dlp returns signed stream URLs (YouTube, Bilibili, etc.)
 * that expire within hours. A 30-minute TTL keeps responses fresh enough to avoid
 * broken streams while still saving expensive yt-dlp re-invocations.
 *
 * Environment variables:
 *   INFO_CACHE_MAX_ENTRIES  — max cached URLs (default: 100)
 *   INFO_CACHE_MAX_MB       — max estimated RAM for cached JSON (default: 20)
 *   INFO_CACHE_TTL_SECONDS  — TTL per entry in seconds (default: 1800 = 30 min)
 */

const MAX_ENTRIES   = parseInt(process.env.INFO_CACHE_MAX_ENTRIES   ?? '100',  10);
const MAX_BYTES     = parseInt(process.env.INFO_CACHE_MAX_MB         ?? '20',   10) * 1024 * 1024;
const TTL_MS        = parseInt(process.env.INFO_CACHE_TTL_SECONDS    ?? '1800', 10) * 1000;

// Proactive expired-entry sweep interval (5 minutes)
const SWEEP_INTERVAL_MS = 5 * 60 * 1000;

/**
 * Rough estimate of a cached entry's RAM footprint.
 * JSON.stringify gives byte count in UTF-8; we add 1 KB for Map node overhead.
 * @param {string} key
 * @param {object} value
 * @returns {number} bytes
 */
function estimateBytes(key, value) {
  try {
    return (key.length + JSON.stringify(value).length) * 2 + 1024;
  } catch {
    return 4096; // conservative fallback
  }
}

export class InfoCache {
  /** @type {Map<string, { value: object, expiresAt: number, bytes: number }>} */
  #map = new Map();
  #totalBytes = 0;
  #sweepTimer = null;

  constructor() {
    // Schedule proactive TTL sweeps so stale entries don't accumulate indefinitely
    this.#sweepTimer = setInterval(() => this.#sweepExpired(), SWEEP_INTERVAL_MS);
    // Don't block process exit on this timer
    this.#sweepTimer.unref?.();
  }

  /**
   * Retrieve a cached entry. Returns null if missing or expired.
   * On hit, moves the entry to the MRU position (LRU promotion).
   * @param {string} key
   * @returns {object | null}
   */
  get(key) {
    const entry = this.#map.get(key);
    if (!entry) return null;

    if (Date.now() >= entry.expiresAt) {
      this.#evictOne(key, entry);
      return null;
    }

    // LRU promotion: delete + re-insert moves key to end of Map insertion order
    this.#map.delete(key);
    this.#map.set(key, entry);
    return entry.value;
  }

  /**
   * Store a value. Evicts LRU entries as needed to stay within limits.
   * @param {string} key
   * @param {object} value
   */
  set(key, value) {
    // If key already exists, remove old entry first
    const existing = this.#map.get(key);
    if (existing) this.#evictOne(key, existing);

    const bytes     = estimateBytes(key, value);
    const expiresAt = Date.now() + TTL_MS;
    this.#map.set(key, { value, expiresAt, bytes });
    this.#totalBytes += bytes;

    this.#enforceLimit();
  }

  /** Number of currently cached entries. */
  get size() { return this.#map.size; }

  /** Estimated bytes used by cached data. */
  get bytesUsed() { return this.#totalBytes; }

  /** Remove all entries. */
  clear() {
    this.#map.clear();
    this.#totalBytes = 0;
  }

  // ---------------------------------------------------------------------------
  // Private helpers
  // ---------------------------------------------------------------------------

  #evictOne(key, entry) {
    this.#map.delete(key);
    this.#totalBytes = Math.max(0, this.#totalBytes - entry.bytes);
  }

  /**
   * Evict the least-recently-used entry (the first key in Map iteration order).
   */
  #evictLRU() {
    const lruKey = this.#map.keys().next().value;
    if (lruKey === undefined) return;
    const entry = this.#map.get(lruKey);
    this.#evictOne(lruKey, entry);
  }

  /**
   * Evict expired entries proactively.
   */
  #sweepExpired() {
    const now = Date.now();
    for (const [key, entry] of this.#map) {
      if (now >= entry.expiresAt) this.#evictOne(key, entry);
    }
  }

  /**
   * Evict LRU entries until both count and memory constraints are satisfied.
   */
  #enforceLimit() {
    while (this.#map.size > MAX_ENTRIES || this.#totalBytes > MAX_BYTES) {
      if (this.#map.size === 0) break;
      this.#evictLRU();
    }
  }
}

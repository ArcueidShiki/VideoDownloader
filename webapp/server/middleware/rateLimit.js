/**
 * rateLimit.js — Sliding-window, per-IP rate limiter
 *
 * No external dependencies. Single-process in-memory store.
 * For multi-process deployments, replace #store with a shared Redis backend.
 *
 * Protects against:
 *   - Bandwidth exhaustion from download bots
 *   - yt-dlp CPU/memory exhaustion from rapid /api/info floods
 *   - General HTTP DoS from a single IP
 *   - Bandwidth leeching via /api/file
 */

/** Extract the real client IP, respecting Nginx's X-Real-IP / X-Forwarded-For headers. */
export function getClientIp(req) {
  const xri = req.headers['x-real-ip']
  if (xri) return xri.trim()
  const xff = req.headers['x-forwarded-for']
  if (xff) return xff.split(',')[0].trim()
  return req.socket?.remoteAddress ?? '0.0.0.0'
}

/**
 * SlidingWindowLimiter — token-counting with a rolling time window.
 *
 * @param {number} windowMs   Window duration in milliseconds
 * @param {number} max        Max hits allowed within the window
 */
class SlidingWindowLimiter {
  #store = new Map()  // ip -> number[] (timestamps)
  #windowMs
  #max

  constructor(windowMs, max) {
    this.#windowMs = windowMs
    this.#max      = max
    // Periodically prune stale entries to prevent memory growth
    setInterval(() => this.#prune(), Math.min(windowMs, 5 * 60_000)).unref()
  }

  #prune() {
    const cutoff = Date.now() - this.#windowMs
    for (const [ip, ts] of this.#store) {
      const pruned = ts.filter(t => t > cutoff)
      if (pruned.length === 0) this.#store.delete(ip)
      else this.#store.set(ip, pruned)
    }
  }

  /**
   * Record a hit and return whether the request is allowed.
   * @returns {{ ok: boolean, remaining: number, retryAfter: number }}
   */
  hit(ip) {
    const now    = Date.now()
    const cutoff = now - this.#windowMs
    const ts     = (this.#store.get(ip) ?? []).filter(t => t > cutoff)

    if (ts.length >= this.#max) {
      const retryAfter = Math.ceil((ts[0] + this.#windowMs - now) / 1000)
      return { ok: false, remaining: 0, retryAfter }
    }

    ts.push(now)
    this.#store.set(ip, ts)
    return { ok: true, remaining: this.#max - ts.length, retryAfter: 0 }
  }

  /** Drain all hits for an IP (e.g., after an admin action). */
  reset(ip) { this.#store.delete(ip) }

  get size() { return this.#store.size }
}

// ── Rate limit tiers ──────────────────────────────────────────────────────────
//
//  ┌───────────────────┬─────────────────┬──────────────────────────────────────┐
//  │ Endpoint          │ Limit           │ Reason                               │
//  ├───────────────────┼─────────────────┼──────────────────────────────────────┤
//  │ All routes        │ 300 / 1 min     │ General DoS protection               │
//  │ POST /api/info    │ 15 / 5 min      │ Spawns yt-dlp — CPU intensive        │
//  │ POST /api/download│ 8 / 1 hour      │ Bandwidth + download queue slots     │
//  │ GET  /api/thumbnail│ 90 / 1 min     │ Outbound HTTP proxy — network cost   │
//  │ GET  /api/file    │ 12 / 10 min     │ Prevent bandwidth leeching           │
//  └───────────────────┴─────────────────┴──────────────────────────────────────┘

export const limiters = {
  global:    new SlidingWindowLimiter(60_000,       300),
  info:      new SlidingWindowLimiter(5 * 60_000,    15),
  download:  new SlidingWindowLimiter(60 * 60_000,    8),
  thumbnail: new SlidingWindowLimiter(60_000,         90),
  file:      new SlidingWindowLimiter(10 * 60_000,   12),
}

/**
 * Create an Express middleware for a specific limiter.
 *
 * @param {SlidingWindowLimiter} limiter
 * @param {string} [scope]   Human-readable scope name for the error message
 */
export function rateLimitMiddleware(limiter, scope = 'requests') {
  return (req, res, next) => {
    const ip     = getClientIp(req)
    const result = limiter.hit(ip)

    res.setHeader('X-RateLimit-Remaining', result.remaining)
    if (!result.ok) {
      res.setHeader('Retry-After', result.retryAfter)
      return res.status(429).json({
        error: `Too many ${scope} from your IP. Please wait ${result.retryAfter}s before retrying.`,
        retryAfter: result.retryAfter,
      })
    }
    next()
  }
}

/** Global rate-limit middleware — applied to every route. */
export const globalRateLimit = rateLimitMiddleware(limiters.global, 'requests')

'use strict';
/**
 * PM2 Ecosystem — VideoDownloader backend
 *
 * Quick reference:
 *   First deploy:         pm2 start ecosystem.config.cjs --env production
 *   Zero-downtime reload: pm2 reload videodownloader-backend
 *   Hard restart:         pm2 restart videodownloader-backend
 *   Status:               pm2 status
 *   Live logs:            pm2 logs videodownloader-backend
 *   Persist across reboot: pm2 save && pm2 startup   (then run the printed command)
 *
 * Runtime config (PORT, FRONTEND_ORIGIN, etc.) is loaded from the .env file
 * written by deploy.sh — never hardcode secrets here.
 *
 * Requires Node 20.6+ for the --env-file flag.
 */

const path = require('path');

// __dirname = directory of this config file = app root on the server
const APP_DIR = __dirname;

module.exports = {
  apps: [
    {
      name: 'videodownloader-backend',
      script: path.join(APP_DIR, 'server/index.js'),
      cwd: APP_DIR,

      // ── Node.js options ────────────────────────────────────────────────────
      // --max-old-space-size caps V8 heap at 700 MB.
      // --env-file reads key=value pairs from .env into process.env (Node 20.6+).
      interpreter: 'node',
      interpreter_args: '--max-old-space-size=700 --env-file .env',

      // Single process — SessionManager holds in-memory WS state so cluster
      // mode would break session affinity. PM2 guards availability instead.
      instances: 1,
      exec_mode: 'fork',

      // ── Auto-restart on crash ──────────────────────────────────────────────
      autorestart: true,
      watch: false,                     // never watch files in production
      max_memory_restart: '850M',       // restart if RSS > 850 MB (leak guard)
      restart_delay: 2000,              // wait 2 s before first restart
      exp_backoff_restart_delay: 100,   // exponential backoff: 100→200→400 ms…
      max_restarts: 20,                 // stop retrying after 20 rapid crashes
      min_uptime: '15s',                // process is "stable" after 15 s uptime

      // ── Logging ────────────────────────────────────────────────────────────
      out_file: path.join(APP_DIR, 'logs/backend.out.log'),
      error_file: path.join(APP_DIR, 'logs/backend.err.log'),
      merge_logs: false,
      log_date_format: 'YYYY-MM-DD HH:mm:ss Z',

      // ── Environment ────────────────────────────────────────────────────────
      // Only NODE_ENV is hardcoded here. Everything else comes from .env.
      env_production: {
        NODE_ENV: 'production',
      },
    },
  ],
};

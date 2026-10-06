# ── VideoDownloader — Backend image ──────────────────────────────────────────
# Node.js + yt-dlp (Unix launcher with bundled EJS scripts) + Python + ffmpeg
# Serves the Express API on port 3001; static frontend is served by nginx.

FROM node:22-alpine

# System dependencies:
#   ffmpeg  — media muxing / transcoding during yt-dlp post-processing
#   curl    — used to check yt-dlp update endpoint in health scripts
#   ca-certificates — TLS root certs for HTTPS downloads
RUN apk add --no-cache python3 ffmpeg curl ca-certificates

# The platform-independent yt-dlp asset is a Python zipapp, not a standalone ELF.
RUN curl -fsSL \
      https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp \
      -o /usr/local/bin/yt-dlp \
    && chmod +x /usr/local/bin/yt-dlp

# Verify the tools are reachable before building the rest of the image
RUN yt-dlp --version && ffmpeg -version | head -1

# ── App setup ─────────────────────────────────────────────────────────────────

WORKDIR /app

# Install production dependencies only
RUN npm install -g pnpm@latest
COPY server/package.json server/pnpm-lock.yaml ./
RUN pnpm install --prod --frozen-lockfile

# Copy server source (routes, middleware, utils, index.js, etc.)
COPY server/ .

# Temp directory for active downloads (override via SESSION_BASE env var)
RUN mkdir -p /tmp/anydownloader-sessions

EXPOSE 3001

HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
  CMD wget -qO- http://localhost:3001/api/health || exit 1

CMD ["node", "index.js"]

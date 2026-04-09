# ── VideoDownloader — nginx image ─────────────────────────────────────────────
# Stage 1: Build the React/Vite frontend
# Stage 2: nginx serving the built static files + proxying API to backend

# ── Stage 1: frontend build ───────────────────────────────────────────────────
FROM node:20-alpine AS builder

RUN npm install -g pnpm@latest
WORKDIR /app

# Dependencies first (cached layer — only invalidated when lockfile changes)
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

# Source files
COPY index.html tsconfig.json vite.config.ts ./
COPY public/ ./public/
COPY src/ ./src/

# Build args map to Vite env vars at build time
ARG VITE_SHOW_ADS=true
ARG VITE_AD_MODE=live
ARG VITE_API_BASE_URL=

RUN VITE_SHOW_ADS=${VITE_SHOW_ADS} \
    VITE_AD_MODE=${VITE_AD_MODE} \
    VITE_API_BASE_URL=${VITE_API_BASE_URL} \
    pnpm build

# ── Stage 2: nginx ────────────────────────────────────────────────────────────
FROM nginx:1.27-alpine

# Remove the default nginx config
RUN rm /etc/nginx/conf.d/default.conf

# Our config (template: only ${DOMAIN}, ${BACKEND_HOST}, ${BACKEND_PORT} are
# substituted at startup — all other nginx $vars are left intact)
COPY docker/nginx/nginx.conf.template /etc/nginx/templates/default.conf.template

# Static frontend files
COPY --from=builder /app/dist /usr/share/nginx/html

# Limit which variables envsubst replaces — prevents it from destroying nginx's
# own $host, $remote_addr, $scheme, etc. variables in the config.
ENV NGINX_ENVSUBST_FILTER="\$DOMAIN \$BACKEND_HOST \$BACKEND_PORT"

# Default backend coordinates (overridden in docker-compose.yml environment:)
ENV BACKEND_HOST=app
ENV BACKEND_PORT=3001

EXPOSE 80 443

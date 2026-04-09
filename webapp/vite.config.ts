import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  // Load env vars so they're accessible inside this config if needed
  loadEnv(mode, process.cwd(), '')

  return {
    plugins: [react()],

    // ── Dev server ─────────────────────────────────────────────────────────
    server: {
      port: 3000,
      proxy: {
        // Forward /api HTTP calls to the Node backend in dev
        '/api': {
          target: 'http://localhost:3001',
          changeOrigin: true,
        },
        // Forward /ws WebSocket upgrades to the Node backend in dev.
        // In production, Nginx handles this; no proxy needed.
        '/ws': {
          target: 'ws://localhost:3001',
          changeOrigin: true,
          ws: true,
        },
      },
    },

    // ── Production build ───────────────────────────────────────────────────
    build: {
      // Use Terser instead of the default esbuild.
      // Terser supports mangle.toplevel which renames all top-level identifiers
      // (component names, helpers, utility functions) — much stronger obfuscation.
      minify: 'terser',

      // NEVER emit source maps in production.
      // Source maps completely undo obfuscation by mapping mangled names back
      // to the original source identifiers.
      sourcemap: false,

      terserOptions: {
        compress: {
          // Strip all console.* and debugger statements from the bundle
          drop_console: true,
          drop_debugger: true,
          // Two compression passes: first removes dead code, second re-optimises
          passes: 2,
          // Inline single-use functions to further reduce symbol surface
          inline: 2,
        },

        mangle: {
          // Rename all top-level identifiers — exported names, component names,
          // and helpers. This is the primary obfuscation step.
          toplevel: true,
          // Avoid clashing with Safari 10 identifier-mangling bugs
          safari10: true,
        },

        format: {
          // Remove all comments (including licence comments) from the output
          comments: false,
        },
      },

      // Split vendor libs into a separate chunk for better browser caching.
      // React + ReactDOM change rarely; users keep them cached across deploys.
      rollupOptions: {
        output: {
          manualChunks: {
            vendor: ['react', 'react-dom'],
          },
          // Content-hash filenames ensure CDN/browser cache invalidates on change
          chunkFileNames: 'assets/[name]-[hash].js',
          entryFileNames: 'assets/[name]-[hash].js',
          assetFileNames: 'assets/[name]-[hash][extname]',
        },
      },
    },
  }
})

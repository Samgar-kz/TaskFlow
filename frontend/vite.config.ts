import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // Same-origin proxy: the HttpOnly refresh cookie works without CORS in development.
    proxy: {
      '/api': { target: process.env.API_URL ?? 'http://localhost:5100', changeOrigin: false },
      '/hubs': { target: process.env.API_URL ?? 'http://localhost:5100', ws: true },
    },
  },
})

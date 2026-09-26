import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    // O backend serve este dist (mesma imagem Docker, padrao do Pickia).
    outDir: 'dist',
    rollupOptions: {
      output: {
        // Dependencias em chunk proprio: um deploy normal so' invalida o
        // chunk do app, e o React fica em cache no navegador do visitante.
        manualChunks(id) {
          const p = id.replace(/\\/g, '/')
          if (!p.includes('/node_modules/')) return
          if (/\/node_modules\/(react|react-dom|react-router|react-router-dom|scheduler)\//.test(p)) return 'vendor-react'
          if (/\/node_modules\/axios\//.test(p)) return 'vendor-net'
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: { '/api': 'http://127.0.0.1:8000' },
  },
})

import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import basicSsl from '@vitejs/plugin-basic-ssl'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const backendTarget = env.VITE_DEV_BACKEND_TARGET || 'https://localhost:7222'

  return {
    plugins: [
      react(),
      basicSsl()
    ],
    server: {
      https: {},
      port: 5173,
      proxy: {
        '/api': {
          target: backendTarget,
          changeOrigin: true,
          secure: false
        },
        '/hub': {
          target: backendTarget,
          changeOrigin: true,
          secure: false,
          ws: true
        }
      }
    },
    build: {
      rolldownOptions: {
        output: {
          manualChunks(id) {
            if (id.includes('node_modules/livekit-client') || id.includes('node_modules/@livekit')) {
              return 'vendor-livekit'
            }
          }
        }
      }
    }
  }
})

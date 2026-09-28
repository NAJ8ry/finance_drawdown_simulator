import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Dev server proxies /api to the .NET backend; production build is served by the backend from wwwroot
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5047',
    },
  },
  build: {
    outDir: '../backend/Finance.Api/wwwroot',
    emptyOutDir: true,
  },
})

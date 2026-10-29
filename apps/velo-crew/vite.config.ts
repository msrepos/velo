import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'VELO_');
  return {
    plugins: [react()],
    server: { port: 5176, proxy: { '/api/': { target: env.VELO_API_PROXY_TARGET || 'https://api.velo.internal', changeOrigin: true } } },
  };
});

import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'VELO_');
  return {
    server: { port: 5177, proxy: { '/api/': { target: env.VELO_API_PROXY_TARGET || 'https://api.velo.internal', changeOrigin: true } } },
    plugins: [react(), VitePWA({
      registerType: 'prompt',
      includeAssets: ['logo.png', 'icon.svg', 'icon-192.png', 'icon-512.png', 'maskable-512.png'],
      manifest: {
        id: '/', name: 'Velo Driver — السواق', short_name: 'Velo Driver', lang: 'ar', dir: 'rtl',
        description: 'موقع العميل، تجهيز الطلب، ورحلة الفان.',
        start_url: '/', scope: '/', display: 'standalone', background_color: '#f6f7f3', theme_color: '#17241e',
        icons: [
          { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
        ]
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png}'], navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api(?:\/|$)/, /^\/admin(?:\/|$)/],
        // Only the public app shell is cached. Addresses, tokens and API calls never are.
        runtimeCaching: []
      }
    })]
  };
});

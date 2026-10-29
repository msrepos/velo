import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'VELO_');
  return {
  server: {
    proxy: { '/api/': { target: env.VELO_API_PROXY_TARGET || 'https://api.velo.internal', changeOrigin: true } }
  },
  plugins: [react(), VitePWA({
    registerType: 'prompt',
    includeAssets: ['logo.png', 'icon.svg', 'icon-192.png', 'icon-512.png', 'maskable-512.png'],
    manifest: {
      id: '/', name: 'Velo — your day, delivered.', short_name: 'Velo',
      description: 'Everyday essentials in your neighborhood. Interactive shopping demo.',
      start_url: '/', scope: '/', display: 'standalone',
      background_color: '#faf9f6', theme_color: '#ed3c34',
      icons: [
        { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
        { src: '/icon-512.png', sizes: '512x512', type: 'image/png' },
        { src: '/maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
      ]
    },
    workbox: {
      globPatterns: ['**/*.{js,css,html,svg,png,jpg,webp,woff2}'],
      navigateFallback: '/index.html',
      // Admin navigations should always receive the current server shell.
      navigateFallbackDenylist: [/^\/admin(?:\/|$)/, /^\/staff(?:\/|$)/, /^\/api(?:\/|$)/],
      // Authenticated API responses and mutations are deliberately never cached.
      runtimeCaching: []
    }
  })]
  };
});

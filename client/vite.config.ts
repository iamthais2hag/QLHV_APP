import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '');
  // Mục tiêu proxy cho API khi chạy dev. Có thể chỉnh qua biến môi trường VITE_API_PROXY_TARGET.
  const apiTarget = env.VITE_API_PROXY_TARGET || 'http://api.qlhv.local:5000';

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true,
          secure: false,
        },
      },
    },
  };
});

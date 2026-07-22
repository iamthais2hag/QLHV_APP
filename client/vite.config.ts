import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '');
  const legacyApiBase = env.VITE_API_BASE_URL?.replace(/\/api\/?$/, '');
  // Mục tiêu proxy cho API khi chạy dev. Có thể chỉnh qua biến môi trường VITE_API_PROXY_TARGET.
  // VITE_API_BASE_URL cũ chỉ được dùng làm proxy target tương thích ngược;
  // browser code luôn gọi /api tương đối và production không phụ thuộc biến này.
  const apiTarget = env.VITE_API_PROXY_TARGET || legacyApiBase || 'http://api.qlhv.local:5000';

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

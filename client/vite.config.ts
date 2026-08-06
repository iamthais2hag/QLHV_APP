import { defineConfig, loadEnv, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '');
  const legacyApiBase = env.VITE_API_BASE_URL?.replace(/\/api\/?$/, '');
  // Mục tiêu proxy cho API khi chạy dev. Có thể chỉnh qua biến môi trường VITE_API_PROXY_TARGET.
  // VITE_API_BASE_URL cũ chỉ được dùng làm proxy target tương thích ngược;
  // browser code luôn gọi /api tương đối và production không phụ thuộc biến này.
  const apiTarget = env.VITE_API_PROXY_TARGET || legacyApiBase || 'http://api.qlhv.local:5000';
  const frontendBuiltAtUtc = env.QLHV_FRONTEND_BUILT_AT_UTC || new Date().toISOString();
  const frontendBuildId = env.QLHV_FRONTEND_BUILD_ID
    || `qlhv-ui-${frontendBuiltAtUtc.replace(/\D/g, '').slice(0, 14)}`;
  const buildInfoPlugin: Plugin = {
    name: 'qlhv-build-info',
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: 'build-info.json',
        source: `${JSON.stringify({
          frontendBuildId,
          frontendBuiltAtUtc,
        }, null, 2)}\n`,
      });
    },
  };

  return {
    plugins: [react(), buildInfoPlugin],
    define: {
      __QLHV_FRONTEND_BUILD_ID__: JSON.stringify(frontendBuildId),
      __QLHV_FRONTEND_BUILT_AT_UTC__: JSON.stringify(frontendBuiltAtUtc),
    },
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

import { fileURLToPath, URL } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vite';

/**
 * 前端開發與打包設定；正式部署的 API 轉送由網站主機設定，不依賴開發伺服器。
 */
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  // 固定開發位址與連接埠，讓 VS Code 啟動設定及同源 /api 請求使用一致入口。
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: 'http://127.0.0.1:5141', changeOrigin: true },
    },
  },
  // 打包產物預覽使用另一個連接埠，避免與開發伺服器互相占用。
  preview: { host: '127.0.0.1', port: 4173, strictPort: true },
});

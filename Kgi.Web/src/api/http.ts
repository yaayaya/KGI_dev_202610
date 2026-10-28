import axios from 'axios';

/**
 * 前端共用的 Axios 設定；生成路徑已含 /api，baseURL 應設服務根網址而非再附加 /api。
 */
export const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/',
  timeout: 10_000,
});

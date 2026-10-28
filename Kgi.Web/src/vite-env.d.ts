/// <reference types="vite/client" />

/**
 * 可公開到瀏覽器的環境變數型別；VITE_ 值會進入前端產物，不能存放機密。
 */
interface ImportMetaEnv {
  /** API 服務根位置；生成的端點已包含 /api，此處預設為 /。 */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

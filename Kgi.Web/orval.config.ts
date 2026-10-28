import { defineConfig } from 'orval';

/**
 * 由已版控的後端契約生成 Axios 函式與型別；自訂流程放在 mutator／Feature，不修改生成檔。
 */
export default defineConfig({
  kgi: {
    // 讀取建置輸出的本機契約，前端產碼不依賴執行中的 API 或資料庫連線。
    input: '../Kgi.Api/openapi/Kgi.Api.json',
    output: {
      // split 將呼叫函式與 schema 型別分成兩檔，clean 僅整理這個生成目錄。
      target: './src/api/generated/kgiApi.ts',
      mode: 'split',
      client: 'axios-functions',
      // Orval v8 預設使用 Fetch，明確沿用既有 Axios 與自訂請求入口。
      httpClient: 'axios',
      clean: true,
      formatter: 'prettier',
      override: {
        header: () => ['由 Orval 自動產生，請勿手動修改。', '來源：Kgi.Api/openapi/Kgi.Api.json'],
        // HTTP 設定由共用入口維護，API 路徑與請求／回應定義則由契約生成。
        mutator: { path: './src/api/mutator.ts', name: 'request' },
        // 日期維持 API 的 ISO 字串，由顯示元件自行決定台北時間等呈現方式。
        useDates: false,
      },
    },
  },
});

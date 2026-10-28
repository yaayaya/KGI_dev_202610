---
name: kgi-frontend
description: 開發 KGI 的 Vue 畫面、元件、狀態或 API 串接時使用，維持功能分層、繁中註解、可讀性與既有 ERP 樣式。跨前後端契約變更時同時讀取 kgi-backend。
---

# KGI 前端開發

沿用 Vue 3、TypeScript、Vite、Vue Router、Tailwind、shadcn-vue／Reka UI、VueUse、Axios 與 Orval。版本依 `Kgi.Web/package.json` 與 lockfile。共通規則見 [AGENTS](../../../AGENTS.md)，功能行為見 [Q1](../../../_Doc/Q1.md)，執行與產碼見 [README](../../../README.md)。

## 分層與檔案責任

以下路徑以 `Kgi.Web/src/` 為起點：

| 位置 | 責任 |
| --- | --- |
| `layouts/` | 全站選單、Header 與共用外框 |
| `features/<功能>/` | 功能頁、專用元件及功能 composable；Q1 狀態入口為 `useWarrantWorkspace.ts` |
| `components/`、`components/ui/` | 跨功能共用元件與 shadcn 基礎元件 |
| `styles/main.css`、`lib/utils.ts` | 主題與共用樣式、`cn` class 合併工具；業務版面留在 Vue 元件 |
| `features/<功能>/*Api.ts` | 功能 API 轉接、路徑編碼與取消訊號 |
| `api/generated/`、`api/mutator.ts`、`api/http.ts` | Orval 生成函式與型別 → 手寫請求入口 → 共用 Axios 設定 |

- 頁面負責組合與呈現，功能流程及複雜非同步狀態放 composable；畫面專用的簡單狀態留在元件，用 `ref`／`computed`，目前不導入 Pinia。
- VueUse 按需匯入，圖示用 `@lucide/vue`、通知用 `vue-sonner`；修改共用元件前先查呼叫端，依實際共用需求拆分。
- API 型別以後端 OpenAPI 為準；金融數值維持十進位字串，計算由後端處理。保留取消請求、過期回應防護與獨立錯誤狀態。
- 契約變更依 README 重產並版控 OpenAPI／Orval 產物，不手改生成檔；沿用 HTTP 狀態及 ProblemDetails。`VITE_API_BASE_URL` 為服務根路徑／網址，端點已含 `/api`，`VITE_` 變數不放機密。

## 註解與畫面可讀性

- 手寫模組開頭說明責任，重要方法用繁中 JSDoc；script 按狀態、衍生值與操作分群，命名表達用途，註解解釋原因與邊界。
- template 以 `<!-- ... -->` 標示主要畫面區塊，CSS 註明主題、共用樣式或動畫範圍；避免逐行重述語法，生成檔保留生成註記。
- 保持緊湊 ERP 密度與既有 KGI 藍、橘、綠語意色；補充說明用 `HelpHint`，保留手機版、鍵盤焦點及減少動態效果設定。
- 前端由使用者人工驗證；不自行新增或執行前端自動測試，也不預設跑 build／lint／type-check。使用者要求或契約產碼必要流程才執行，並回報實際範圍；純文件修改只檢查差異與連結。

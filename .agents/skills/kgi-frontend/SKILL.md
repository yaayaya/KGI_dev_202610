---
name: kgi-frontend
description: 開發或調整 KGI 專案 Kgi.Web 的 Vue 畫面、功能狀態、ERP 樣式、Axios 串接與 Orval 產碼時使用。跨前後端契約變更時同時讀取 kgi-backend。
---

# KGI 前端開發

沿用本專案已確認的前端選擇，以清楚的功能分工完成當次需求。先讀取相關檔案與呼叫端；Q1 行為以 [Q1 說明](../../../_Doc/Q1.md) 為依據，執行指令見 [README](../../../README.md)。

## 技術與規模

- 使用 Vue 3、TypeScript、Vite、Vue Router、Tailwind CSS、shadcn-vue／Reka UI、VueUse、Axios、Orval。圖示沿用 `@lucide/vue`，通知沿用 `vue-sonner`。
- 目前不導入 Pinia；單頁狀態優先留在頁面或功能 composable，不為了命名一致而加 store／service／repository。
- VueUse 以 `@vueuse/core` 直接依賴、按需具名匯入，優先用於螢幕判斷及事件生命週期等工具；簡單狀態仍使用 Vue 的 `ref`／`computed`，不為套用工具全面改寫。
- Node.js 相容需求與 Orval 版本以 `Kgi.Web/package.json` 為準，安裝結果依 lockfile 固定；除非使用者要求，不自行升級套件或改成 Docker 產碼，不把特定本機版本寫成專案需求。
- 依功能需要增加檔案，不要求每個功能都複製 Q1 全套拆分；保留現有非同步、錯誤與無障礙處理。

## 檔案職責

以下路徑以 `Kgi.Web/src/` 為起點：

| 位置 | 負責內容 |
| --- | --- |
| `layouts/AppLayout.vue` | 全站選單、Header、手機抽屜及共用外框 |
| `features/<功能>/` | 功能頁、專用元件、狀態與 API 語意轉接；畫面專用狀態留在畫面 |
| `features/warrants/useWarrantWorkspace.ts` | Q1 驗證、選券、預覽、存檔及歷史請求狀態 |
| `components/`、`components/ui/` | 跨功能共用元件與 shadcn 基礎元件；修改基礎元件時先查所有呼叫端 |
| `styles/main.css` | 主題語意變數與 ERP 共用樣式；業務版面放在對應 Vue 元件 |
| `lib/utils.ts` | 提供 `cn`，合併 class 與處理 Tailwind 衝突，不另加一套樣式合併工具 |
| `api/http.ts`、`api/mutator.ts` | 共用 Axios 設定，以及 Orval 使用的手寫請求入口 |
| `api/generated/` | 由 OpenAPI 產生的 API 函式與型別，不手動修改 |

## 契約與 API 串接

- API DTO 以後端 OpenAPI 為準，不在前端再手寫同名 DTO；畫面額外需要的狀態型別才由前端定義。`warrantTypes.ts` 目前只是生成型別的轉匯出入口。
- 沿用 Axios；共用設定放 `api/http.ts`，生成請求經 `api/mutator.ts`。Q1 的代碼路徑編碼與取消訊號由 `warrantApi.ts` 轉接，不在元件重新拼一套 URL，也不因加入 VueUse 改用 `useFetch`。
- `VITE_API_BASE_URL` 是服務根路徑 `/` 或根網址；生成端點已含 `/api`，避免重複前綴。`VITE_` 變數會進入瀏覽器，不放機密。
- 契約變更時，於專案根目錄執行 `npm --prefix Kgi.Web run api:update`；此命令含必要的後端建置及前端產碼。只按現有契約重產時用 `npm --prefix Kgi.Web run api:generate`。
- 檢查並一併版控 `Kgi.Api/openapi/Kgi.Api.json` 與 `api/generated/` 的變更。修改契約來源或產碼設定後重產，不能靠手改生成檔修正型別。
- 保留 HTTP 狀態與 ProblemDetails 錯誤內容，不包成所有結果都成功的回應。

## Q1 必須保留的資料與互動

- 股價、價格、比例與避險張數維持 API 的十進位字串，不用 `Number`／`parseFloat` 重算金融數值。庫存與流水號依契約保留整數。
- 財務公式由後端計算；前端只驗證輸入格式、呈現結果。公式提示文字須與後端一致，UTC 時間顯示為台北時間。
- 選券與股價改變時失效舊預覽；保留防抖、取消請求及過期回應防護，不能只為縮短程式就刪掉。
- 預覽不存檔；存檔成功清空股價與結果、保留選券並刷新歷史；失敗保留輸入與結果。歷史載入失敗不能被顯示為存檔失敗。
- 存檔中防止重複送出及混用選券／股價，不自動重試可能已成功的寫入。清單篩選不偷偷更換目前選券。

## 畫面與註解

- 維持緊湊 ERP 密度：共用左側功能選單與 Header，清楚呈現代碼、CALL／PUT、履約價、行使比例及庫存；避免大型橫幅、過大按鈕及空白。
- 使用既有 KGI 藍、橘、綠語意色彩；約 60／30／10 為品牌強調方向，不是每個畫面的硬性面積配額。目前色碼是專案主題選值，不宣稱為官方 CI 色碼。
- 補充解釋放在 `HelpHint` 的 hover／focus／點擊提示，避免長說明常駐。保留鍵盤焦點、手機版、收合動畫及減少動態效果設定。
- 手寫模組開頭說明責任與邊界；script 按狀態、衍生值及操作分群，重要方法用繁中 JSDoc 說明目的。
- template 用 `<!-- ... -->` 標示清單、基本資料、試算、錯誤、歷史等主要區塊；CSS 註解標示主題、共用樣式或動畫範圍。不逐行重述語法，不為一行轉匯出寫長篇解釋。
- Orval 生成檔保留生成註記；業務註解寫在手寫入口。基礎元件只有在職責或行為需說明時補註解。

## 交付與驗證

- 前端以使用者人工測試為準，不新增前端測試檔、測試框架或測試 script，也不自行執行前端自動測試。
- 不把 build、lint、type-check 當作預設收尾；使用者要求或任務必要流程（例如契約產碼）才執行，並說明實際執行範圍。
- 交付時列出這次影響的人工檢查項目與預期結果。Q1 涉及時涵蓋篩選、快速切券／改價、無效股價、存檔成功清空、失敗保留、歷史更新；只列本次相關項目。
- 純註解或文件修改只檢查差異與連結，不宣稱操作已驗證。

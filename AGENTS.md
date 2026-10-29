# KGI 專案開發指引

## 共用慣例

- 回覆、文件與手寫註解使用繁體中文。
- Commit 格式為 `<type>(<scope>): <subject>`，subject 使用繁體中文。
- 專案測試使用一般執行方式，不使用 sandbox；是否執行及範圍依使用者要求與對應 skill 處理。
- 維持根目錄的 `Kgi.Api`、`Kgi.Web`、`Kgi.Tests`，不再增加 `src/<專案>`、`tests/<專案>` 外層。
- 採單一 API、單一 Vue 前端與單一資料庫 `KGI`；API 與資料庫測試共用 `ConnectionStrings:Kgi`。測試不自動重建資料庫，只建立及清理自己擁有的測試資料。目前實作 Q1、Q2、Q3，不順帶製作 Q4；Q3 的操作及維運監控皆使用 Vue。
- API 與測試的資料存取統一使用 Dapper、`Microsoft.Data.SqlClient` 與明確參數化 SQL；資料庫結構以 SQL 腳本為準，POCO、Service／DTO 與 OpenAPI 契約分別維護。
- 單一 `Kgi.Api` 內採 Controller → Service → Dao → DB：Service 保留驗證、計算、流程與 DTO，具體功能 Dao 負責 Dapper、SQL 參數、POCO 映射及連線管理；不先建立通用 BaseDao、Repository 或額外介面。
- 依需求修改，不因新增功能就建立空資料夾、通用介面或額外架構層；不順帶升級套件、重建資料庫或格式化整個專案。

## 專案 Skills

處理對應範圍前，讀取以下 skill；跨前後端契約的需求同時讀取兩份。使用者當次明確要求優先於這些慣例。

| 範圍 | Skill |
| --- | --- |
| `Kgi.Web/` 的畫面、狀態、樣式、API 串接與前端產碼 | [kgi-frontend](.agents/skills/kgi-frontend/SKILL.md) |
| `Kgi.Api/`、`Kgi.Tests/`、DAO／Dapper／SQL、DB First 與 OpenAPI 契約 | [kgi-backend](.agents/skills/kgi-backend/SKILL.md) |

啟動與產碼指令見 [README](README.md)，功能行為與維護入口見 [Q1 說明](_Doc/Q1.md)、[Q2 說明](_Doc/Q2.md)、[Q3 說明](_Doc/Q3.md)。

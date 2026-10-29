---
name: kgi-backend
description: 開發 KGI 的 .NET API、DAO、Dapper／SQL 或後端測試時使用，維持分層責任、繁中註解與程式可讀性。跨前後端契約變更時同時讀取 kgi-frontend。
---

# KGI 後端開發

維持單一 .NET 10 Web API 與主要資料庫 `KGI`。共通規則見 [AGENTS](../../../AGENTS.md)，功能規格見 [Q1](../../../_Doc/Q1.md)，啟動與產碼見 [README](../../../README.md)。此 Skill 僅保存專案的架構與撰寫慣例。

## 分層與檔案責任

`Controller → Service → Dao → SQL Server`，功能檔案集中在 `Kgi.Api/Features/<功能>/`：

| 檔案 | 責任 |
| --- | --- |
| `*Controller.cs` | HTTP 路由、輸入綁定、狀態碼與 OpenAPI 宣告 |
| `*Service.cs` | 輸入正規化、驗證、商業計算、流程及 DTO 轉換；注入具體 Dao |
| `*Dao.cs` | Dapper、SQL 參數、POCO 映射、連線及交易；注入 `SqlConnectionFactory` |
| `*Sql.cs` | 集中該功能的明確參數化 SQL |
| `*Dtos.cs` | API Request／Response，與資料庫 Entity 分開 |

- `Data/SqlConnectionFactory.cs` 集中讀取連線設定，只建立未開啟的連線；`Data/Entities/` 放純 POCO。
- Service 不直接操作連線或 SQL；Dao 不處理商業規則、HTTP 錯誤或 API 格式。金融計算使用 `decimal`，輸出精確數值字串，時間以 UTC 傳輸。
- 共用例外處理放 `Exceptions/`，契約調整放 `Extensions/ApiSchemaTransformer.cs`，DI 與管線放 `Program.cs`。
- 依實際責任拆檔；有具體共用或替換需求時才加介面、Base 類別或新層。

## 資料與契約

- 資料存取統一使用 Dapper、`Microsoft.Data.SqlClient` 與明確 SQL，不使用 EF、資料庫 LINQ 或 `IQueryable`。
- SQL 明列欄位，必要時用別名對應 POCO；外部輸入一律參數化，decimal 精度對齊欄位，搜尋內容保持字面比對。
- Dao 每次操作以 `await using` 管理連線，`OpenAsync` 與 Dapper `CommandDefinition` 傳遞 `CancellationToken`；需原子性的多步操作，在同一 Dao 方法內使用同一連線與明確交易。
- DB First 的結構來源為 `Kgi.Tests/Database/Scripts/InitTestDb/`；結構變更同步 SQL、POCO、Dao 與 DTO。表及欄位透過 `MS_Description` 記錄繁中用途。
- Controller 使用 `ActionResult<T>`，錯誤沿用 ProblemDetails，保留 Swagger UI；契約變更同步檢查 `ApiSchemaTransformer`、operationId、nullable 與金融字串，依 README 重產並版控 OpenAPI／Orval 產物。
- 連線放 User Secrets／環境變數；API 與 DB 測試共用 `ConnectionStrings:Kgi`，本版本僅使用 `KGI`。API 啟動及測試均不自動重建或清表；資料重建只限使用者明確授權的目標。設定 `KGI_RUN_DB_TESTS=1` 才執行 DB 測試，只建立及清理本次自有資料，保留既有部位、行情與結算；跳過測試不代表通過。

## 註解與可讀性

- 手寫類別與方法使用繁中 XML `summary`，首尾標籤與內容各占一行；DTO 欄位註明意義、單位、格式及必要的 null 情境。
- 命名表達用途，方法維持單一責任；按流程分段，複雜判斷及邊界處理說明原因，避免逐行翻譯程式。
- 註解寫在規則所在處；生成檔改來源及生成設定，保持生成註記。
- 只執行本次必要驗證並如實回報；純文件修改檢查差異與連結即可。

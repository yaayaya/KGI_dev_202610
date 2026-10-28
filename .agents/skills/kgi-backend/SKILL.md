---
name: kgi-backend
description: 開發或調整 KGI 專案 Kgi.Api、Kgi.Tests、Service／DAO 分工、Dapper／Microsoft.Data.SqlClient 資料存取、參數化 SQL、POCO、DB First 流程及 OpenAPI 契約時使用。契約影響前端時同時讀取 kgi-frontend。
---

# KGI 後端開發

維持單一 .NET 10 Web API 與主要資料庫 `KGI`，以功能分組完成需求。先讀取相關 Controller、Service、Dao、DTO、SQL 與 POCO；Q1 行為見 [Q1 說明](../../../_Doc/Q1.md)，啟動與產碼流程見 [README](../../../README.md)。本 Skill 的權威來源與載入入口均為 repository 的 `.agents/skills/kgi-backend/`，不複製或掛載至 Global Skills。

## 架構與命名

以下路徑以 `Kgi.Api/` 為起點：

| 位置 | 負責內容 |
| --- | --- |
| `Features/<功能>/<功能>Controller.cs` | HTTP 路由、輸入綁定、成功狀態碼與 OpenAPI 宣告，繼承 `ControllerBase` |
| `Features/<功能>/<功能>Service.cs` | 輸入正規化、驗證、金融計算、流程、DTO 轉換及 UTC 格式化，注入該功能 Dao |
| `Features/<功能>/<功能>Dao.cs` | Dapper 參數、SQL 執行、POCO 映射與連線生命週期，注入 `SqlConnectionFactory` |
| `Features/<功能>/<功能>Sql.cs` | 明確列出欄位的參數化 SQL；Q1 為 `Features/Warrants/WarrantSql.cs` |
| `Features/<功能>/<功能>Dtos.cs` | API Request／Response；與資料庫 Entity 分開 |
| `Data/SqlConnectionFactory.cs` | 集中連線設定，只建立尚未開啟的 `Microsoft.Data.SqlClient.SqlConnection` |
| `Data/Entities/` | 對應資料表的純 C# POCO，不放商業邏輯或 API DTO |
| `Exceptions/GlobalExceptionHandler.cs` | 集中將例外轉成適當狀態碼與 ProblemDetails |
| `Extensions/ApiSchemaTransformer.cs` | 讓產碼契約符合實際欄位、必填／可空及精確數值規則 |
| `Program.cs` | DI、資料庫與 HTTP 管線註冊 |

- 使用 Controllers，不為了新功能改成 Minimal API、MVC View 或 Razor。
- 單一 `Kgi.Api` 內採 Controller → Service → Dao → DB；Service 注入具體 Dao，不直接使用 Dapper、SQL 參數或連線。Dao 不放商業規則或 API DTO，SQL 按功能集中在 `*Sql.cs`。
- Q1 使用 `WarrantDao`；暫不導入 interface、通用 BaseDao、Repository、通用 BaseController、CQRS 或額外專案分層，有實際替換、外部依賴或多處共用需求時再評估抽象。
- `WarrantDao` 提供 `GetWarrantsAsync`、`GetWarrantAsync`（查無時回傳 null）、`InsertTrialAsync`、`GetRecentTrialsAsync`。由 Service 決定查無資料的處理、正規化輸入及轉成 API 回應；不在 Dao 丟出 HTTP 層例外或格式化金融字串。
- Q1 存檔流程由 Service 先透過 Dao 查主檔、重新計算，再呼叫 Dao 寫入；單一 INSERT 不額外建立交易。真正有獨立責任時才拆 Query／計算服務，不按行數或預想未來機械式拆檔。
- 不使用 EF Core、DbContext、`Data/Configurations/`、資料庫 LINQ 或 `IQueryable`；不引入變更追蹤或 Scaffold 流程。
- Middleware、Filters、Authorization 等依實際需求新增，不先建空殼。

## HTTP、DTO 與 OpenAPI

- 使用 `ActionResult<T>`，清單使用 `List<T>`；不導入自訂 Response 外殼。保留真實的 200／201／400／404／500 等狀態，錯誤使用 ProblemDetails。
- Entity 不直接對外回傳，避免資料庫變更直接改動 API 契約。非同步連線、交易與資料操作傳遞 `CancellationToken`，Dapper 命令透過 `CommandDefinition` 傳入取消權杖。
- 保留 Swagger UI，現有 Development 位址為 `http://127.0.0.1:5141/swagger`；OpenAPI 文件為 `/openapi/v1.json`。不換成 Scalar。
- 維持穩定 operationId；新增或修改路由時同步宣告成功／失敗型別與狀態，核對必填、nullable、enum 及金融字串。
- 改變 DTO 時檢查 `ApiSchemaTransformer` 是否也需調整；例如 C# 的 `decimal` 輸入目前以 OpenAPI 字串描述，不能讓產碼退回不精確的 number。
- 契約變更後，於根目錄執行 `npm --prefix Kgi.Web run api:update`，同步更新 OpenAPI JSON 與前端 Orval 生成檔並版控；跨契約變更另讀前端 skill，不手改產物。

## Q1 數值與存檔規則

- 商業計算放在後端 Service，使用 `decimal`，不使用 `float`／`double`。前端傳股價，後端依主檔重新計算，不信任前端提供的計算結果。
- 股價須大於零、最多四位小數並符合 `DECIMAL(18,4)`；同時檢查計算結果的資料庫範圍。價格及比例對外固定四位小數，避險張數固定兩位，以不受地區影響的字串輸出。
- Q1 目前使用題目指定的簡化 CALL／PUT 模型，包含正值 Delta 與 `MidpointRounding.AwayFromZero`；勿自行改成市場定價模型。修改公式時同步維護前端提示與文件。
- 預覽不寫紀錄；存檔重新驗證及計算後寫入。最近紀錄取十筆，時間倒序並以流水號作同時間的排序依據。
- 時間以 UTC 儲存及傳輸，台北時間屬顯示需求。

## SQL 與 DB First

DB First 流程為：**SQL 腳本 → 資料庫 → Dapper SQL／POCO／DAO → Service／DTO → OpenAPI／Orval**。SQL 腳本是結構的權威來源；修改資料表後，同步檢查功能 SQL、POCO、Dao 與 API 契約，不以 ORM 映射或產生模型取代結構維護。

- 結構、索引與預設資料腳本集中在 `Kgi.Tests/Database/Scripts/InitTestDb/`，不另建平行的 `DBScripts/` 或 Migrations 流程。
- 腳本依序為 `DbScript/BuildInitDbScript.sql`、`DbScript/BuildIndexScript.sql`、`Preload.sql`。沿用直接可讀的 `DROP TABLE IF EXISTS`、`CREATE TABLE`、`INSERT` 與獨立一行 `GO`；有外鍵時先處理子表。
- 表、欄位需有繁中用途說明，透過 `MS_Description` 保留可在資料庫檢視的備註；型別、精度、預設值、限制、SQL 參數及 POCO 須一致。SQL 投影明確列出欄位，必要時以別名對應 POCO 屬性，不使用 `SELECT *`。
- 所有外部輸入以 `@參數` 傳入 Dapper，不以字串串接或插值組成 SQL；關鍵字篩選維持代碼包含關鍵字的行為，不讓輸入改變 SQL 結構或比對規則。金融值以 `decimal` 參數傳送，精度與 scale 對齊資料庫欄位。
- 每個 Dao 操作透過 `SqlConnectionFactory` 建立連線，使用 `await using` 釋放並以 `OpenAsync(cancellationToken)` 開啟；Dapper `CommandDefinition` 同樣傳入取消權杖，不跨操作或請求共用已開啟連線。未來多步讀寫需保證原子性時，在同一 Dao 操作內以同一連線明確建立 `SqlTransaction`，將交易傳入每個相關命令，成功後提交、失敗時回復；不預先建立通用交易框架。
- API 啟動不自動重建、套表或灌資料。修改文件、API 或產碼不代表授權清除資料庫；重建僅限當次已明確要求的目標與範圍，不建立本機 `.bak`。
- 測試重建入口為 `Kgi.Tests/Database/Scripts/InitTestDbTool.cs`，只接受 `KGI_Test`，並要求 `KGI_ALLOW_DB_REBUILD=1`。保留目標驗證、先讀完三份 SQL、`GO` 分批與 60 秒 timeout；各批次共用同一連線以保留腳本內跨批次交易，不把 `CREATE DATABASE` 包進交易。
- 測試查詢、資料寫入、fixture 與初始化批次執行同樣使用 Dapper／`Microsoft.Data.SqlClient`，不保留另一套 ORM 或 `SqlCommand` 存取；固定結構腳本分批交由 Dapper 執行，外部輸入仍必須參數化。
- 連線使用 User Secrets／環境變數；不把密碼放入程式、文件、生成碼或命令輸出。

## 備註格式

手寫類別與方法沿用繁中 XML summary，開頭、內容及結尾分行：

```csharp
/// <summary>
/// 查詢權證基本資料，可依代碼關鍵字篩選。
/// </summary>
```

DTO 每個欄位都需說明意義、單位、格式與必要的 null 情境。商業規則說明放在規則所在處；產生碼改由生成設定或可保留的擴充方式維護，避免每次重產後手動補回。

## 驗證與交付

- 依當次需求執行必要的後端檢查；一般指令為 `dotnet test Kgi.Tests/Kgi.Tests.csproj`，使用一般執行方式，不使用 sandbox。使用者要求不執行時，改為說明未驗證項目。
- 現有 DB 測試會重建 `KGI_Test`，只有同時提供 `KGI_TEST_CONNECTION_STRING` 與 `KGI_ALLOW_DB_REBUILD=1` 才啟用；執行前確認已授權測試重建。不能因測試被跳過而宣稱 DB 已驗證。
- 純註解或文件通常不需要建置、測試或資料庫操作；會影響 OpenAPI 描述的 DTO XML 註解仍按契約流程重產。必要的契約產碼建置與資料庫測試分開說明，前者不需要重建 DB。
- 交付說明實際改動、契約／SQL 是否變動、已執行與未執行的檢查；前端操作仍由使用者人工測試，不順帶新增前端測試工具。

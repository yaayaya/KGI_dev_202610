# KGI 證券作業平台

目前提供[權證試算（Q1）](_Doc/Q1.md)與[借券維持率監控（Q2）](_Doc/Q2.md)。功能可由左側選單切換。

## 系統架構

```mermaid
flowchart LR
    Web[前端操作畫面] --> Controller
    subgraph API[Kgi.Api]
        Controller --> Service --> Dao
    end
    Dao --> DB[(SQL Server)]
```

`WarrantService` 負責輸入正規化、驗證、金融計算、流程與 DTO／UTC 格式化；`WarrantDao` 負責 Dapper 參數、SQL 執行、POCO 映射及連線生命週期。SQL 集中在 `WarrantSql`，`SqlConnectionFactory` 只建立連線，維持單一 API 專案。

Q2 沿用相同分工：`LendingService` 負責 CSV 匯入驗證與流程，`LendingDao` 負責交易式批次寫入；維持率依題目要求，由 `LendingSql` 使用 SQL JOIN 一次計算全部合約。前端 CSV 預覽使用 Papa Parse，正式匯入由後端 CsvHelper 完整解析。

## 使用方式

需要 .NET 10、Node.js 24 LTS 與 SQL Server。Node.js 主要版本由 [.nvmrc](.nvmrc) 指定，使用 nvm 時可執行 `nvm install` 與 `nvm use` 切換；Node.js 與 Orval 的版本需求以 [Kgi.Web/package.json](Kgi.Web/package.json) 為準，實際套件版本由 lockfile 固定。以下指令皆在專案根目錄執行。

1. 準備可連線的 SQL Server 執行個體。
2. 首次使用先建立 `KGI` 資料庫，在該資料庫依序執行[建表](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/BuildInitDbScript.sql)、[索引](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/BuildIndexScript.sql)、[預設資料](Kgi.Tests/Database/Scripts/InitTestDb/Preload.sql)。建表會清除既有資料。
3. 依環境設定連線，替換以下範例的資料庫主機、連接埠、帳號與密碼，並依資料庫憑證設定調整連線選項：

```powershell
dotnet user-secrets set 'ConnectionStrings:Kgi' 'Server=<資料庫主機>,<連接埠>;Database=KGI;User Id=<資料庫帳號>;Password="<資料庫密碼>";Encrypt=True;TrustServerCertificate=<true或false>;' --project Kgi.Api
```

4. 在兩個終端機分別啟動後端與前端：

```powershell
# 後端
dotnet run --project Kgi.Api
```

```powershell
# 前端
npm --prefix Kgi.Web ci
npm --prefix Kgi.Web run dev
```

開啟[操作畫面](http://127.0.0.1:5173)，或使用 [Swagger](http://127.0.0.1:5141/swagger) 試呼叫 API。

### 透過測試重建 KGI_Test

測試總管可找到並單獨執行 `Kgi.Tests.Database.RebuildDbTests.RebuildDb`。此測試會清除 `KGI_Test` 的 Q1、Q2 資料，依序執行建表、索引與預設資料腳本，再確認有 800 筆權證、1,000 筆借券合約，且試算紀錄與行情為空。

執行前須設定環境變數或於 `appsettings.Development.json` 設定；若使用 IDE 測試總管，亦會自動載入 `appsettings.Development.json`。未設定時，資料庫測試會顯示跳過。

```powershell
$env:KGI_TEST_CONNECTION_STRING = 'Server=<資料庫主機>,<連接埠>;Database=KGI_Test;User Id=<資料庫帳號>;Password="<資料庫密碼>";Encrypt=True;TrustServerCertificate=<true或false>;'
$env:KGI_ALLOW_DB_REBUILD = '1'
dotnet test Kgi.Tests/Kgi.Tests.csproj --filter 'FullyQualifiedName=Kgi.Tests.Database.RebuildDbTests.RebuildDb'
```

重建工具只接受資料庫名稱 `KGI_Test`，不會重建應用程式使用的 `KGI`。一般 DB/API 測試也會透過共用 fixture 在測試開始前自動重建 `KGI_Test`；單獨執行 `RebuildDb` 時只重建一次。

### 已有 Q1 資料時加入 Q2

在既有 `KGI` 資料庫依序執行 [Q2 增量建表](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/AddQ2Lending.sql)及[預設資料](Kgi.Tests/Database/Scripts/InitTestDb/Preload.sql)。增量腳本只補上 Q2 物件，預設資料只補缺少的主檔，不清除 Q1 權證或試算紀錄；不要對既有資料執行全量建表腳本。

開啟[借券維持率監控](http://127.0.0.1:5173/lending)，選取[行情 CSV 範例](_Doc/examples/Market_Price.csv)，可先按「查看預覽」確認前十筆，再按「確認匯入」。完整規則與人工檢查項目見 [Q2](_Doc/Q2.md)。API 啟動與契約產碼不會自動套用資料庫腳本。

## API 產碼

更新後端契約與前端型別／Axios 函式：

```powershell
npm --prefix Kgi.Web run api:update
```

只依現有契約重產前端時，執行 `npm --prefix Kgi.Web run api:generate`。契約位於 `Kgi.Api/openapi/Kgi.Api.json`，生成檔位於 `Kgi.Web/src/api/generated/`，兩者一併版控，生成檔不可手改。

產碼使用獨立的 `output/openapi-api` 建置目錄，不必停止正在執行的 API，也不讀寫資料庫；修改 API 本身後仍需重新啟動服務。Axios 的 `VITE_API_BASE_URL` 改為服務根路徑 `/` 或服務根網址，生成路徑已包含 `/api`。

資料庫維護採 DB First：SQL 腳本 → 資料庫 → Dapper SQL／POCO／DAO → Service／DTO → OpenAPI／Orval。後端 DAO 與測試統一使用 Dapper、`Microsoft.Data.SqlClient` 與明確參數化 SQL；`Data/Entities/` 保留純 C# 資料模型，SQL 集中在各功能的 `*Sql.cs`，不使用 Scaffold 產生模型。

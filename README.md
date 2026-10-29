# KGI 證券作業平台

提供[權證試算（Q1）](_Doc/Q1.md)、[借券維持率監控（Q2）](_Doc/Q2.md)、[借券利息結算（Q3）](_Doc/Q3.md)與[股務代理作業（Q4）](_Doc/Q4.md)。全部使用 Vue，並共用單一資料庫 `KGI`。

## 環境

.NET 10、Node.js 24 LTS、SQL Server。以下指令皆在專案根目錄執行。

## 啟動

1. 首次使用在 SSMS 執行 [DBSCRIPT/Rebuild.sql](DBSCRIPT/Rebuild.sql)，建立並初始化 `KGI`。**重跑會清除既有資料。**
2. 設定資料庫連線，替換主機、連接埠、帳號及密碼：

```powershell
dotnet user-secrets set 'ConnectionStrings:Kgi' 'Server=<主機>,<連接埠>;Database=KGI;User Id=<帳號>;Password="<密碼>";Encrypt=True;TrustServerCertificate=True;' --project Kgi.Api
```

3. 在兩個終端機分別啟動後端與前端：

```powershell
# 後端
dotnet run --project Kgi.Api
```

```powershell
# 前端
npm --prefix Kgi.Web ci
npm --prefix Kgi.Web run dev
```

- [操作畫面](http://127.0.0.1:5173)
- [Swagger](http://127.0.0.1:5141/swagger)
- [行情 CSV 範例](_Doc/examples/Market_Price.csv)

## 既有資料庫升級

需保留舊資料時，在 SSMS 選擇 `KGI`，依 Q2 → Q3 → Q4 順序執行尚未套用的 [Q2](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/AddQ2Lending.sql)、[Q3](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/AddQ3Interest.sql)、[Q4](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/AddQ4StockAffairs.sql) 增量建表，最後執行 [Preload.sql](Kgi.Tests/Database/Scripts/InitTestDb/Preload.sql)。已執行完整重建可略過；API 啟動不會自動建表或灌資料。

## 啟用 Q3

Q3 預設停用。資料庫就緒後，啟用背景結算並重啟 API：

```powershell
dotnet user-secrets set 'InterestSettlement:Enabled' 'true' --project Kgi.Api
dotnet run --project Kgi.Api
```

開啟[借券利息結算](http://127.0.0.1:5173/interest)，操作與示範資料見 [Q3](_Doc/Q3.md)。Q1、Q2、Q4 不需額外啟用。

## 測試

測試沿用 API 的 `ConnectionStrings:Kgi`。一般 DB/API 測試只建立及清理自己的資料，不自動重建或清空 `KGI`；未啟用時跳過。

```powershell
$env:KGI_RUN_DB_TESTS = '1'
dotnet test Kgi.Tests/Kgi.Tests.csproj -c Release --filter 'Category!=DatabaseReset'
```

**手動重建及全域行情重置會刪除既有資料**，獨立分類為 `DatabaseReset`，另須明確啟用。只有需要重建時，才執行指定入口：

```powershell
$env:KGI_RUN_DB_TESTS = '1'
$env:KGI_ALLOW_DB_REBUILD = '1'
dotnet test Kgi.Tests/Kgi.Tests.csproj -c Release --filter 'FullyQualifiedName=Kgi.Tests.Database.RebuildDbTests.RebuildDb'
```

兩種重建入口使用相同的建表、索引及預載來源，皆只操作 `KGI`。

## API 產碼

修改 API 契約後，更新 OpenAPI 與前端呼叫程式：

```powershell
npm --prefix Kgi.Web run api:update
```

## 設計架構

- Vue 依功能拆分畫面與狀態，API 採 `Controller → Service → Dao → KGI`，讓驗證、金融計算與資料存取各自集中。
- 金額由後端使用 `decimal` 計算；Dao 使用 Dapper、參數化 SQL 與交易，確保精度及整批寫入一致。各功能架構見上方 Q1～Q4 文件。

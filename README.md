# KGI 證券作業平台

提供[權證試算（Q1）](_Doc/Q1.md)、[借券維持率監控（Q2）](_Doc/Q2.md)、[借券利息結算（Q3）](_Doc/Q3.md)與[股務代理作業（Q4）](_Doc/Q4.md)。四個功能共用單一 `KGI`；Q3 操作與監控全部使用 Vue。

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

## 啟用 Q3

既有 `KGI` 依序執行 [Q3 增量建表](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/AddQ3Interest.sql)、[Q4 增量建表](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/AddQ4StockAffairs.sql) 與 [Preload.sql](Kgi.Tests/Database/Scripts/InitTestDb/Preload.sql)，保留原有資料。確認腳本完成後啟用背景結算並重啟 API：

```powershell
dotnet user-secrets set 'InterestSettlement:Enabled' 'true' --project Kgi.Api
dotnet run --project Kgi.Api
```

開啟[借券利息結算](http://127.0.0.1:5173/interest)。預載提供正常千筆批次及故意總額不符批次；公式、交易與監控規則見 [Q3](_Doc/Q3.md)。API 啟動不會自動建表或灌資料。

## 啟用 Q4

既有 Q1／Q2／Q3 的 `KGI` 依序執行 [Q4 增量建表](Kgi.Tests/Database/Scripts/InitTestDb/DbScript/AddQ4StockAffairs.sql) 與 [Preload.sql](Kgi.Tests/Database/Scripts/InitTestDb/Preload.sql)，保留既有資料。預載涵蓋全部功能，若尚未有 Q3，先執行其增量建表。

開啟[股務代理作業](http://127.0.0.1:5173/stock-affairs)。預載提供 3 間公司、300 位股東、506 筆名簿、1,000 筆基準日快照、6 場會議與 8 個股利批次。過戶／質權使用確認 Dialog；通知與發放為模擬外部作業，明細及狀態實際寫入同一個 `KGI`。操作及精確控制總額見 [Q4](_Doc/Q4.md)。

## 測試

測試沿用 API 的 `ConnectionStrings:Kgi`。一般 DB/API 測試只建立及清理自己的唯一權證、行情日期與結算批次，不自動重建或清空 `KGI`；未啟用時跳過。

```powershell
$env:KGI_RUN_DB_TESTS = '1'
dotnet test Kgi.Tests/Kgi.Tests.csproj --filter 'Category!=DatabaseReset'
```

**手動重建及全域行情重置會刪除既有資料**，獨立分類為 `DatabaseReset`，另須明確啟用。只有需要重建時，才執行指定入口：

```powershell
$env:KGI_RUN_DB_TESTS = '1'
$env:KGI_ALLOW_DB_REBUILD = '1'
dotnet test Kgi.Tests/Kgi.Tests.csproj --filter 'FullyQualifiedName=Kgi.Tests.Database.RebuildDbTests.RebuildDb'
```

重建腳本的手寫來源為 `Kgi.Tests/Database/Scripts/InitTestDb/`。修改來源後，執行 `pwsh DBSCRIPT/Generate-Rebuild.ps1` 同步產製可在 SSMS 執行的完整重建入口。

## API 產碼

修改 API 契約後，更新 OpenAPI 與前端呼叫程式：

```powershell
npm --prefix Kgi.Web run api:update
```

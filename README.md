# KGI 證券作業平台

[Q1 權證試算](_Doc/Q1.md)｜[Q2 借券維持率](_Doc/Q2.md)｜[Q3 利息結算](_Doc/Q3.md)｜[Q4 股務代理](_Doc/Q4.md)

四項功能預設啟用，使用 Vue、.NET API 與單一 SQL Server 資料庫 `KGI`。

## 啟動

環境：.NET 10、Node.js 24 LTS、SQL Server。以下指令在專案根目錄執行。

1. 在 SSMS 執行 [DBSCRIPT/Rebuild.sql](DBSCRIPT/Rebuild.sql)，建立資料表及示範資料。**重跑會清除既有資料。**
2. 開啟 [Kgi.Api/appsettings.json](Kgi.Api/appsettings.json)，直接填寫 `ConnectionStrings` 下的 `Kgi`。此欄位預設為空字串；將範例中的主機、連接埠、帳號、密碼換成實際值，並保留設定檔其他欄位。API 與測試共用此設定。

```json
{
  "ConnectionStrings": {
    "Kgi": "Server=<主機>,<連接埠>;Database=KGI;User Id=<帳號>;Password=<密碼>;Encrypt=True;TrustServerCertificate=True;"
  }
}
```

3. 在兩個終端機分別啟動：

```powershell
# 後端
dotnet run --project Kgi.Api
```

```powershell
# 前端
npm --prefix Kgi.Web ci
npm --prefix Kgi.Web run dev
```

[操作畫面](http://127.0.0.1:5173)｜[Swagger](http://127.0.0.1:5141/swagger)｜[行情 CSV 範例](_Doc/examples/Market_Price.csv)

## 測試

使用 VS Code／Visual Studio 的測試總管執行 `Kgi.Tests`。

## API 產碼

修改 API 契約後執行：

```powershell
npm --prefix Kgi.Web run api:update
```

## 設計架構

- 前端依功能拆分 Vue 畫面與狀態；後端採 `Controller → Service → Dao → KGI`，分開處理 HTTP、驗證計算與資料存取。
- 金額使用 `decimal`；Dapper 搭配參數化 SQL 與交易，確保精度及整批寫入一致。各功能規則與架構見 Q1～Q4 文件。

using Dapper;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database;

/// <summary>
/// 序列化共用 KGI 的功能測試，避免不同案例同時修改資料。
/// </summary>
[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;

/// <summary>
/// 確認既有 KGI 已部署完成，不重建、清表或重設示範資料。
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// 沿用 API 的單一 KGI 連線。
    /// </summary>
    public string ConnectionString => DatabaseTestConfiguration.ConnectionString
        ?? throw new InvalidOperationException("請沿用 API 設定 ConnectionStrings:Kgi，資料庫名稱須為 KGI。");

    /// <summary>
    /// 確認 Q1 至 Q4 已部署；不清表、不改寫既有作業。
    /// </summary>
    public async Task InitializeAsync()
    {
        if (DatabaseFactAttribute.IsEnabled)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            var ready = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.Warrant_Master', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Warrant_Trial_Log', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Lending_Contract', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Market_Price', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Interest_Batch', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Interest_Source', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Interest_Detail', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Company_Master', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Shareholder_Master', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Shareholding_Register', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Shareholding_Snapshot', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Shareholding_Snapshot_Detail', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Share_Operation_Request', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Share_Transfer_Log', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Shareholders_Meeting', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Meeting_Notice_Detail', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Dividend_Batch', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Dividend_Detail', N'U') IS NOT NULL
                    THEN 1 ELSE 0 END AS BIT);
                """));
            if (!ready)
            {
                throw new InvalidOperationException("KGI 尚未部署完成，請先依 README 完成資料庫初始化。");
            }
        }
    }

    /// <summary>
    /// 保留既有 KGI，由各測試自行清理擁有的資料。
    /// </summary>
    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// 已設定 API 共用連線即可執行一般資料庫測試，不包含重建授權。
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    /// <summary>
    /// 判斷是否已設定 API 共用連線。
    /// </summary>
    public static bool IsEnabled => DatabaseTestConfiguration.IsEnabled;

    /// <summary>
    /// 未滿足一般測試條件時顯示明確的跳過原因。
    /// </summary>
    public DatabaseFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = "資料庫測試需設定 Kgi.Api/appsettings.json 的 ConnectionStrings:Kgi；測試不會重建 KGI。";
        }
    }
}

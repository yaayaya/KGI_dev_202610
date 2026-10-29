using Dapper;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database;

[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;

/// <summary>
/// 確認既有 KGI 已部署完成，不重建、清表或重設示範資料。
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public string ConnectionString => DatabaseTestConfiguration.ConnectionString
        ?? throw new InvalidOperationException("請沿用 API 設定 ConnectionStrings:Kgi，資料庫名稱須為 KGI。");

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
                    THEN 1 ELSE 0 END AS BIT);
                """));
            if (!ready)
            {
                throw new InvalidOperationException("KGI 尚未部署完成，請依 README 套用建表或增量腳本後再執行資料庫測試。");
            }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class DatabaseFactAttribute : FactAttribute
{
    public static bool IsEnabled => DatabaseTestConfiguration.IsEnabled;

    public DatabaseFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = "資料庫測試需設定 KGI_RUN_DB_TESTS=1，並沿用 API 的 ConnectionStrings:Kgi；測試不會重建 KGI。";
        }
    }
}

using Dapper;
using Kgi.Tests.Database.Scripts;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database;

/// <summary>
/// 獨立執行資料庫重建，避免共用 fixture 預先重建，也避免與其他資料庫測試並行。
/// </summary>
[CollectionDefinition("DatabaseRebuild", DisableParallelization = true)]
public sealed class DatabaseRebuildCollection;

/// <summary>
/// 提供可從測試總管單獨執行的 KGI_Test 重建入口。
/// </summary>
[Collection("DatabaseRebuild")]
[Trait("Category", "Database")]
public sealed class RebuildDbTests
{
    /// <summary>
    /// 重建 Q1、Q2 資料表與索引，灌入預設資料並確認作業紀錄已清空。
    /// </summary>
    [DatabaseFact]
    public async Task RebuildDb()
    {
        var connectionString = DatabaseTestConfiguration.ConnectionString
            ?? throw new InvalidOperationException("請設定 KGI_TEST_CONNECTION_STRING，或於 appsettings.Development.json 設定 ConnectionStrings:KgiTest。");

        await InitTestDbTool.RebuildAsync(connectionString);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT COUNT(*) FROM dbo.Warrant_Master;
            SELECT COUNT(*) FROM dbo.Warrant_Trial_Log;
            SELECT COUNT(*) FROM dbo.Lending_Contract;
            SELECT COUNT(*) FROM dbo.Market_Price;
            """));

        Assert.Equal(800, await results.ReadSingleAsync<int>());
        Assert.Equal(0, await results.ReadSingleAsync<int>());
        Assert.Equal(1000, await results.ReadSingleAsync<int>());
        Assert.Equal(0, await results.ReadSingleAsync<int>());
    }
}

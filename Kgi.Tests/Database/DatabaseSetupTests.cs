using Dapper;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Database;

/// <summary>
/// 唯讀確認單一 KGI 的結構與示範來源已部署，不重建資料庫或清除既有作業紀錄。
/// </summary>
[Collection("Database")]
[Trait("Category", "Database")]
public sealed class DatabaseSetupTests(DatabaseFixture database)
{
    /// <summary>
    /// 驗證功能資料表、必要索引及受信任外鍵，不假定既有資料筆數或批次狀態。
    /// </summary>
    [DatabaseFact]
    public async Task ExistingKgiHasRequiredTablesIndexesAndForeignKeys()
    {
        Assert.True(string.Equals("KGI", new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog,
            StringComparison.OrdinalIgnoreCase), "資料庫測試必須使用應用程式的同一 KGI 資料庫。");
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo');
            SELECT I.name FROM sys.indexes AS I
            INNER JOIN sys.tables AS T ON T.object_id = I.object_id
            WHERE T.schema_id = SCHEMA_ID(N'dbo') AND I.is_disabled = 0 AND I.name IS NOT NULL;
            SELECT name FROM sys.foreign_keys
            WHERE schema_id = SCHEMA_ID(N'dbo') AND is_disabled = 0 AND is_not_trusted = 0;
            """));
        var tables = (await results.ReadAsync<string>()).ToHashSet(StringComparer.Ordinal);
        var indexes = (await results.ReadAsync<string>()).ToHashSet(StringComparer.Ordinal);
        var foreignKeys = (await results.ReadAsync<string>()).ToHashSet(StringComparer.Ordinal);

        foreach (var table in new[]
                 {
                     "Warrant_Master", "Warrant_Trial_Log", "Lending_Contract", "Market_Price",
                     "Interest_Batch", "Interest_Source", "Interest_Detail"
                 })
        {
            Assert.Contains(table, tables);
        }

        foreach (var index in new[]
                 {
                     "IX_Warrant_Trial_Log_Warrant_ID_Created_Time_Log_ID", "IX_Lending_Contract_Stock_ID",
                     "IX_Interest_Batch_Process_Status_Batch_ID", "IX_Interest_Detail_Batch_ID_Detail_ID",
                     "PK_Interest_Batch", "PK_Interest_Source", "PK_Interest_Detail",
                     "UQ_Interest_Detail_Batch_ID_Contract_No"
                 })
        {
            Assert.Contains(index, indexes);
        }

        foreach (var foreignKey in new[]
                 {
                     "FK_Warrant_Trial_Log_Warrant_Master", "FK_Interest_Source_Interest_Batch",
                     "FK_Interest_Detail_Interest_Source"
                 })
        {
            Assert.Contains(foreignKey, foreignKeys);
        }
    }

    /// <summary>
    /// 示範來源應完整保留，不要求批次仍待處理，也不要求歷史或結算明細為空。
    /// </summary>
    [DatabaseFact]
    public async Task ExampleBatchesKeepCompleteSourcesRegardlessOfSettlementStatus()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var rows = (await connection.QueryAsync<ExampleSourceSummary>(new CommandDefinition(
            """
            SELECT Batch.Batch_ID AS BatchId, COUNT(Source.Contract_No) AS SourceCount,
                   SUM(CASE WHEN Source.Contract_No IS NOT NULL
                                 AND (Source.Principal_Amt <= 0 OR Source.Annual_Rate <= 0)
                            THEN 1 ELSE 0 END) AS InvalidSourceCount
            FROM dbo.Interest_Batch AS Batch
            LEFT JOIN dbo.Interest_Source AS Source ON Source.Batch_ID = Batch.Batch_ID
            WHERE Batch.Batch_ID IN ('20261027_INT', '20261028_INT', '20261029_INT')
            GROUP BY Batch.Batch_ID;
            """))).ToDictionary(row => row.BatchId, StringComparer.Ordinal);

        foreach (var (batchId, sourceCount) in new[]
                 {
                     ("20261027_INT", 5), ("20261028_INT", 10), ("20261029_INT", 1000)
                 })
        {
            Assert.True(rows.TryGetValue(batchId, out var row), $"缺少示範批次 {batchId}，請先執行增量及預載腳本。");
            Assert.Equal(sourceCount, row!.SourceCount);
            Assert.Equal(0, row.InvalidSourceCount);
        }
    }

    /// <summary>
    /// 單一示範批次的來源完整性摘要，不包含或改寫任何結算狀態。
    /// </summary>
    private sealed record ExampleSourceSummary(string BatchId, int SourceCount, int InvalidSourceCount);
}

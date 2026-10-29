using System.Text.RegularExpressions;
using Dapper;
using Kgi.Tests.Database;
using Microsoft.Data.SqlClient;

namespace Kgi.Tests.Features.Interest;

/// <summary>
/// 在同一 KGI 驗證增量與預載可重跑，只建立及清理本次唯一批次。
/// </summary>
[Collection("Database")]
[Trait("Category", "Database")]
public sealed class InterestMigrationTests(DatabaseFixture database)
{
    /// <summary>
    /// 自建終態與所有既有批次、來源、明細及 Q1、Q2 資料都必須在重跑後保留。
    /// </summary>
    [DatabaseFact]
    public async Task RepeatedMigrationAndPreloadPreserveSettledBatchesAndExistingFeatures()
    {
        var successId = $"M{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var failureId = $"M{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var parameters = new
        {
            SuccessId = new DbString { Value = successId, IsAnsi = true, Length = 20 },
            FailureId = new DbString { Value = failureId, IsAnsi = true, Length = 20 }
        };
        var scripts = Path.Combine(AppContext.BaseDirectory, "Database", "Scripts", "InitTestDb");
        var migration = await File.ReadAllTextAsync(Path.Combine(scripts, "DbScript", "AddQ3Interest.sql"));
        var preload = await File.ReadAllTextAsync(Path.Combine(scripts, "Preload.sql"));
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var before = await ReadExistingFeaturesAsync(connection, parameters);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.Interest_Batch
                    (Batch_ID, Expected_Amt, Process_Status, Progress_Rate, Started_Time, Finished_Time, Error_Message)
                VALUES (@SuccessId, 1.0000, 2, 100, SYSUTCDATETIME(), SYSUTCDATETIME(), NULL),
                       (@FailureId, 2.0002, 9, 37, SYSUTCDATETIME(), SYSUTCDATETIME(), N'保留本次測試的失敗原因');
                INSERT INTO dbo.Interest_Source (Batch_ID, Contract_No, Principal_Amt, Annual_Rate)
                VALUES (@SuccessId, 'MIGRATE_ONE', 365.0000, 1.00000000),
                       (@FailureId, 'MIGRATE_ONE', 730.0000, 1.00000000);
                INSERT INTO dbo.Interest_Detail (Batch_ID, Contract_No, Daily_Interest)
                VALUES (@SuccessId, 'MIGRATE_ONE', 1.0000);
                """, parameters));

            var ownBatches = await ReadOwnedBatchesAsync(connection, parameters);
            var ownData = await ReadOwnedDataAsync(connection, parameters);
            for (var run = 0; run < 2; run++)
            {
                await ExecuteScriptAsync(connection, migration);
                await ExecuteScriptAsync(connection, preload);
            }

            Assert.Equal(ownBatches, await ReadOwnedBatchesAsync(connection, parameters));
            Assert.Equal(ownData, await ReadOwnedDataAsync(connection, parameters));
            Assert.Equal(before, await ReadExistingFeaturesAsync(connection, parameters));
        }
        finally
        {
            // 不還原或覆寫任何既有示範資料，只依完整唯一編號清除此測試建立的資料。
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM dbo.Interest_Detail WHERE Batch_ID IN (@SuccessId, @FailureId);
                DELETE FROM dbo.Interest_Source WHERE Batch_ID IN (@SuccessId, @FailureId);
                DELETE FROM dbo.Interest_Batch WHERE Batch_ID IN (@SuccessId, @FailureId);
                """, parameters));
        }
    }

    /// <summary>
    /// 排除本次唯一批次，比對既有 Q1、Q2、Q3 所有資料的筆數及全欄位校驗摘要。
    /// </summary>
    private static async Task<List<TableSnapshot>> ReadExistingFeaturesAsync(SqlConnection connection, object parameters)
    {
        var rows = await connection.QueryAsync<TableSnapshot>(new CommandDefinition(
            """
            SELECT 'Warrant_Master' AS TableName, COUNT_BIG(*) AS [RowCount],
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Warrant_ID, Strike_Price, Conversion_Ratio,
                       Warrant_Type, Position_Qty)), 0) AS ChecksumValue FROM dbo.Warrant_Master
            UNION ALL
            SELECT 'Warrant_Trial_Log', COUNT_BIG(*),
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Log_ID, Warrant_ID, Market_Price,
                       Theory_Price, Hedge_Qty, Created_Time)), 0) FROM dbo.Warrant_Trial_Log
            UNION ALL
            SELECT 'Lending_Contract', COUNT_BIG(*),
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Contract_No, Stock_ID,
                       Borrowed_Qty, Collateral_Val)), 0) FROM dbo.Lending_Contract
            UNION ALL
            SELECT 'Market_Price', COUNT_BIG(*),
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Stock_ID, Trade_Date, Closing_Price)), 0)
            FROM dbo.Market_Price
            UNION ALL
            SELECT 'Interest_Batch', COUNT_BIG(*),
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Batch_ID, Expected_Amt, Process_Status,
                       Progress_Rate, Started_Time, Finished_Time, Error_Message)), 0)
            FROM dbo.Interest_Batch WHERE Batch_ID NOT IN (@SuccessId, @FailureId)
            UNION ALL
            SELECT 'Interest_Source', COUNT_BIG(*),
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Batch_ID, Contract_No, Principal_Amt, Annual_Rate)), 0)
            FROM dbo.Interest_Source WHERE Batch_ID NOT IN (@SuccessId, @FailureId)
            UNION ALL
            SELECT 'Interest_Detail', COUNT_BIG(*),
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Detail_ID, Batch_ID, Contract_No, Daily_Interest)), 0)
            FROM dbo.Interest_Detail WHERE Batch_ID NOT IN (@SuccessId, @FailureId)
            ORDER BY TableName;
            """, parameters));
        return rows.AsList();
    }

    /// <summary>
    /// 保存本次終態批次的完整狀態，驗證重跑沒有重設時間、預計總額或錯誤訊息。
    /// </summary>
    private static async Task<List<BatchSnapshot>> ReadOwnedBatchesAsync(SqlConnection connection, object parameters)
    {
        var rows = await connection.QueryAsync<BatchSnapshot>(new CommandDefinition(
            """
            SELECT Batch_ID AS BatchId, Expected_Amt AS ExpectedAmt, Process_Status AS ProcessStatus,
                   Progress_Rate AS ProgressRate, Started_Time AS StartedTime,
                   Finished_Time AS FinishedTime, Error_Message AS ErrorMessage
            FROM dbo.Interest_Batch WHERE Batch_ID IN (@SuccessId, @FailureId) ORDER BY Batch_ID;
            """, parameters));
        return rows.AsList();
    }

    /// <summary>
    /// 保存本次來源及結果資料，驗證原有金額與流水號均未被重建或覆寫。
    /// </summary>
    private static async Task<List<TableSnapshot>> ReadOwnedDataAsync(SqlConnection connection, object parameters)
    {
        var rows = await connection.QueryAsync<TableSnapshot>(new CommandDefinition(
            """
            SELECT 'Interest_Source' AS TableName, COUNT_BIG(*) AS [RowCount],
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Batch_ID, Contract_No, Principal_Amt, Annual_Rate)), 0)
                       AS ChecksumValue
            FROM dbo.Interest_Source WHERE Batch_ID IN (@SuccessId, @FailureId)
            UNION ALL
            SELECT 'Interest_Detail', COUNT_BIG(*),
                   COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(Detail_ID, Batch_ID, Contract_No, Daily_Interest)), 0)
            FROM dbo.Interest_Detail WHERE Batch_ID IN (@SuccessId, @FailureId)
            ORDER BY TableName;
            """, parameters));
        return rows.AsList();
    }

    /// <summary>
    /// 僅處理版控腳本中獨立一行的 GO 分隔符，不使用 SQLCMD 或另外啟動資料庫程式。
    /// </summary>
    private static async Task ExecuteScriptAsync(SqlConnection connection, string sql)
    {
        foreach (var batch in Regex.Split(sql, @"^[\t ]*GO[\t ]*(?:--[^\r\n]*)?\r?$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await connection.ExecuteAsync(new CommandDefinition(batch, commandTimeout: 60));
            }
        }
    }

    /// <summary>
    /// 資料表筆數及資料校驗摘要。
    /// </summary>
    private sealed record TableSnapshot(string TableName, long RowCount, int ChecksumValue);

    /// <summary>
    /// 完整批次狀態與可供監控的時間及錯誤資訊。
    /// </summary>
    private sealed record BatchSnapshot(string BatchId, decimal ExpectedAmt, int ProcessStatus, int ProgressRate,
        DateTime? StartedTime, DateTime? FinishedTime, string? ErrorMessage);
}

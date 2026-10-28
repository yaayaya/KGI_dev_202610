using System.Data;
using System.Text.Json;
using Dapper;
using Kgi.Api.Data;
using Microsoft.Data.SqlClient;

namespace Kgi.Api.Features.Lending;

/// <summary>
/// 管理借券監控的 JOIN 查詢、行情批次參數及交易，不在此處格式化 API 或解析 CSV。
/// </summary>
public sealed class LendingDao(SqlConnectionFactory connections)
{
    /// <summary>
    /// 以單次資料庫往返取得行情日期與全部合約的 SQL 計算結果。
    /// </summary>
    public async Task<LendingMonitorData> GetMonitorAsync(DateOnly? tradeDate, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        // 保留行情讀取鎖直到兩個結果集全部讀完，避免同日行情替換時混讀不同批次。
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var parameters = new DynamicParameters();
        parameters.Add("TradeDate", tradeDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
        LendingMonitorData monitor;
        using (var result = await connection.QueryMultipleAsync(new CommandDefinition(
            LendingSql.Monitor, parameters, transaction, cancellationToken: cancellationToken)))
        {
            var effectiveDate = await result.ReadSingleAsync<DateTime?>();
            var contracts = (await result.ReadAsync<LendingMonitorRow>()).AsList();
            monitor = new LendingMonitorData(effectiveDate, contracts);
        }

        await transaction.CommitAsync(cancellationToken);
        return monitor;
    }

    /// <summary>
    /// 在同一交易內確認所有合約都有行情，再批次替換指定交易日，失敗時保留原有資料。
    /// </summary>
    public async Task<List<string>> ReplacePricesAsync(ParsedMarketPrices market, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        // 序列化隔離保護完整性檢查期間的合約集合；同日期應用程式鎖讓並行匯入依序完成。
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var parameters = new DynamicParameters();
        parameters.Add("TradeDate", market.TradeDate.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("LockResource", $"Kgi.Lending.MarketPrice:{market.TradeDate:yyyyMMdd}", DbType.String, size: 255);
        parameters.Add("Prices", JsonSerializer.Serialize(market.Prices.Select(price => new
        {
            stockId = price.StockId,
            closingPrice = price.ClosingPrice
        })), DbType.String, size: -1);

        var lockResult = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            LendingSql.AcquireImportLock, parameters, transaction, cancellationToken: cancellationToken));
        if (lockResult < 0)
        {
            throw new InvalidOperationException("無法取得行情匯入鎖，請稍後重新整理監控資料再確認匯入結果。");
        }

        var missingStocks = (await connection.QueryAsync<string>(new CommandDefinition(
            LendingSql.MissingStocks, parameters, transaction, cancellationToken: cancellationToken))).AsList();
        if (missingStocks.Count > 0)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return missingStocks;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            LendingSql.ReplacePrices, parameters, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return [];
    }
}

using System.Data;
using Dapper;
using Kgi.Api.Data;
using Kgi.Api.Data.Entities;

namespace Kgi.Api.Features.Warrants;

/// <summary>
/// 負責權證與試算紀錄的資料存取，管理連線、SQL 參數及 POCO 映射，不執行業務計算。
/// </summary>
public sealed class WarrantDao(SqlConnectionFactory connections)
{
    /// <summary>
    /// 依已正規化的關鍵字查詢權證；null 表示不篩選，排序及字面比對交由明確 SQL 處理。
    /// </summary>
    public async Task<List<WarrantMaster>> GetWarrantsAsync(
        string? keyword, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var parameters = new DynamicParameters();
        // 保留 Unicode 與完整長度，不將搜尋內容拼接為 SQL。
        parameters.Add("Keyword", keyword, DbType.String, size: -1);
        var warrants = await connection.QueryAsync<WarrantMaster>(new CommandDefinition(
            WarrantSql.ListWarrants, parameters, cancellationToken: cancellationToken));
        return warrants.AsList();
    }

    /// <summary>
    /// 依代碼取得權證主檔；不存在時回傳 null，由 Service 決定業務錯誤與回應。
    /// </summary>
    public async Task<WarrantMaster?> GetWarrantAsync(
        string warrantId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<WarrantMaster>(new CommandDefinition(
            WarrantSql.GetWarrant, WarrantParameters(warrantId), cancellationToken: cancellationToken));
    }

    /// <summary>
    /// 寫入 Service 已計算的結果，取回資料庫產生的流水號、UTC 時間與實際儲存值。
    /// </summary>
    public async Task<WarrantTrialLog> InsertTrialAsync(
        WarrantTrialLog trial, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var parameters = WarrantParameters(trial.WarrantId);
        parameters.Add("MarketPrice", trial.MarketPrice, DbType.Decimal, precision: 18, scale: 4);
        parameters.Add("TheoryPrice", trial.TheoryPrice, DbType.Decimal, precision: 18, scale: 4);
        parameters.Add("HedgeQty", trial.HedgeQty, DbType.Decimal, precision: 18, scale: 2);
        return await connection.QuerySingleAsync<WarrantTrialLog>(new CommandDefinition(
            WarrantSql.InsertTrial, parameters, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// 取得指定權證最近十筆試算紀錄；存在性檢查由 Service 負責。
    /// </summary>
    public async Task<List<WarrantTrialLog>> GetRecentTrialsAsync(
        string warrantId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var trials = await connection.QueryAsync<WarrantTrialLog>(new CommandDefinition(
            WarrantSql.RecentTrials, WarrantParameters(warrantId), cancellationToken: cancellationToken));
        return trials.AsList();
    }

    /// <summary>
    /// 權證代碼以 varchar 傳入；保留過長查詢值，避免截斷後誤查到另一個代碼。
    /// </summary>
    private static DynamicParameters WarrantParameters(string warrantId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("WarrantId", warrantId, DbType.AnsiString, size: Math.Max(10, warrantId.Length));
        return parameters;
    }
}

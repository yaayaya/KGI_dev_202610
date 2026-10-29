using System.Data;
using System.Text.Json;
using Dapper;
using Kgi.Api.Data;
using Microsoft.Data.SqlClient;

namespace Kgi.Api.Features.StockAffairs;

/// <summary>
/// 管理股務資料的參數化查詢、交易鎖與原子寫入；商業驗證及計算由 Service 提供。
/// </summary>
public sealed class StockAffairsDao(SqlConnectionFactory connections)
{
    private const string OperatorId = "DEMO_OPERATOR";

    /// <summary>
    /// 一次聚合全部公司的正持股人數與股數，避免逐公司查詢。
    /// </summary>
    public async Task<List<StockCompanyRow>> GetCompaniesAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return (await connection.QueryAsync<StockCompanyRow>(new CommandDefinition(
            StockAffairsSql.Companies, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// 在同一讀取交易取得公司存在狀態、名簿與合法股東候選清單。
    /// </summary>
    public async Task<StockRegisterData> GetRegisterAsync(string companyCode, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            StockAffairsSql.CompanyExists + StockAffairsSql.Register + StockAffairsSql.Candidates,
            Parameters(companyCode), transaction, cancellationToken: cancellationToken));
        var exists = await results.ReadSingleAsync<bool>();
        var holders = (await results.ReadAsync<StockHolderRow>()).AsList();
        var candidates = (await results.ReadAsync<StockCandidateResponse>()).AsList();
        await transaction.CommitAsync(cancellationToken);
        return new StockRegisterData(exists, holders, candidates);
    }

    /// <summary>
    /// 查詢指定公司的全部股權稽核紀錄；公司不存在時回傳 null。
    /// </summary>
    public async Task<List<StockTransferRow>?> GetTransfersAsync(string companyCode, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            StockAffairsSql.CompanyExists + StockAffairsSql.Transfers, Parameters(companyCode), cancellationToken: cancellationToken));
        var exists = await results.ReadSingleAsync<bool>();
        var rows = (await results.ReadAsync<StockTransferRow>()).AsList();
        return exists ? rows : null;
    }

    /// <summary>
    /// 依請求及公司取得交易鎖，交由 Service 規劃異動，再一次寫入部位、請求與全部稽核。
    /// </summary>
    public async Task<StockRequestRow> ApplyTransfersAsync(string companyCode, string requestId, string payloadHash,
        Func<StockTransferState, StockTransferPlan> planFactory, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // 先鎖全域請求，再鎖公司；相同識別碼跨公司重送也遵循一致鎖定順序。
        await AcquireLockAsync(connection, transaction, $"Kgi.Stock.Request:{requestId}", cancellationToken);
        await AcquireLockAsync(connection, transaction, $"Kgi.Stock.Company:{companyCode}", cancellationToken);
        var parameters = Parameters(companyCode, requestId: requestId);
        var existing = await connection.QuerySingleOrDefaultAsync<StockRequestRow>(new CommandDefinition(
            StockAffairsSql.ExistingRequest, parameters, transaction, cancellationToken: cancellationToken));
        if (existing is not null)
        {
            existing.AlreadyApplied = true;
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        StockTransferState state;
        using (var results = await connection.QueryMultipleAsync(new CommandDefinition(
            StockAffairsSql.CompanyExists + StockAffairsSql.Positions + StockAffairsSql.Candidates,
            parameters, transaction, cancellationToken: cancellationToken)))
        {
            var exists = await results.ReadSingleAsync<bool>();
            var positions = (await results.ReadAsync<StockPositionRow>()).AsList();
            var candidates = await results.ReadAsync<StockCandidateResponse>();
            state = new StockTransferState(exists, positions,
                candidates.Select(row => row.ShareholderId).ToHashSet(StringComparer.Ordinal));
        }

        // 委派在同一交易鎖內檢查商業規則；驗證拋錯時交易會在釋放時回滾。
        var plan = planFactory(state);
        parameters.Add("PayloadHash", payloadHash, DbType.AnsiStringFixedLength, size: 64);
        parameters.Add("OperatorId", OperatorId, DbType.AnsiString, size: 50);
        parameters.Add("Now", DateTime.UtcNow, DbType.DateTime);
        parameters.Add("Positions", JsonSerializer.Serialize(plan.Positions), DbType.String, size: -1);
        parameters.Add("Logs", JsonSerializer.Serialize(plan.Logs), DbType.String, size: -1);
        await connection.ExecuteAsync(new CommandDefinition(StockAffairsSql.WriteTransfers, parameters,
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new StockRequestRow { RequestId = requestId, CompanyCode = companyCode,
            PayloadHash = payloadHash, TransferCount = plan.Logs.Count };
    }

    /// <summary>
    /// 取得公司會議清單與固定快照或已寄發的收件人數。
    /// </summary>
    public async Task<List<StockMeetingRow>?> GetMeetingsAsync(string companyCode, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            StockAffairsSql.CompanyExists + StockAffairsSql.Meetings + " ORDER BY m.Meeting_Date DESC,m.Meeting_ID;",
            Parameters(companyCode), cancellationToken: cancellationToken));
        var exists = await results.ReadSingleAsync<bool>();
        var rows = (await results.ReadAsync<StockMeetingRow>()).AsList();
        return exists ? rows : null;
    }

    /// <summary>
    /// 以同一讀取交易取得會議、快照與已寄發明細。
    /// </summary>
    public async Task<StockNoticesData?> GetNoticesAsync(string companyCode, string meetingId, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var data = await ReadNoticesAsync(connection, transaction, companyCode, meetingId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return data;
    }

    /// <summary>
    /// 以會議鎖保證模擬通知明細及旗標同時完成；重送保留原有名單及時間。
    /// </summary>
    public async Task<StockNoticesData?> SendNoticesAsync(string companyCode, string meetingId,
        Action<StockNoticesData> validate, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AcquireLockAsync(connection, transaction, $"Kgi.Stock.Meeting:{meetingId}", cancellationToken);
        var data = await ReadNoticesAsync(connection, transaction, companyCode, meetingId, cancellationToken);
        if (data is null || data.Meeting.NoticeSent)
        {
            await transaction.CommitAsync(cancellationToken);
            return data;
        }

        validate(data);
        var parameters = Parameters(companyCode, meetingId: meetingId, snapshotId: data.Meeting.SnapshotId);
        parameters.Add("OperatorId", OperatorId, DbType.AnsiString, size: 50);
        parameters.Add("Now", DateTime.UtcNow, DbType.DateTime);
        await connection.ExecuteAsync(new CommandDefinition(StockAffairsSql.WriteNotices, parameters,
            transaction, cancellationToken: cancellationToken));
        var completed = await ReadNoticesAsync(connection, transaction, companyCode, meetingId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return completed;
    }

    /// <summary>
    /// 查詢公司股利批次；公司不存在時回傳 null。
    /// </summary>
    public async Task<List<StockDividendRow>?> GetDividendsAsync(string companyCode, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            StockAffairsSql.CompanyExists + StockAffairsSql.Dividends + " ORDER BY b.Batch_ID;",
            Parameters(companyCode), cancellationToken: cancellationToken));
        var exists = await results.ReadSingleAsync<bool>();
        var rows = (await results.ReadAsync<StockDividendRow>()).AsList();
        return exists ? rows : null;
    }

    /// <summary>
    /// 在一致讀取交易中取得股利試算來源及既有凍結明細。
    /// </summary>
    public async Task<StockDividendData?> GetDividendAsync(string companyCode, string batchId, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var data = await ReadDividendAsync(connection, transaction, companyCode, batchId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return data;
    }

    /// <summary>
    /// 鎖住股利批次後交由 Service 重新計算，凍結明細與完成狀態在同一交易提交。
    /// </summary>
    public async Task<StockDividendWriteData?> ConfirmDividendAsync(string companyCode, string batchId,
        Func<StockDividendData, StockDividendCalculation> calculate, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await AcquireLockAsync(connection, transaction, $"Kgi.Stock.Dividend:{batchId}", cancellationToken);
        var data = await ReadDividendAsync(connection, transaction, companyCode, batchId, cancellationToken);
        if (data is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        if (data.Batch.Status == 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return new StockDividendWriteData(data.Batch, data.Details.Count, true);
        }

        var result = calculate(data);
        var parameters = Parameters(companyCode, batchId: batchId);
        parameters.Add("Details", JsonSerializer.Serialize(result.Details), DbType.String, size: -1);
        parameters.Add("Now", DateTime.UtcNow, DbType.DateTime);
        parameters.Add("OperatorId", OperatorId, DbType.AnsiString, size: 50);
        await connection.ExecuteAsync(new CommandDefinition(StockAffairsSql.WriteDividend, parameters,
            transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        data.Batch.Status = 1;
        return new StockDividendWriteData(data.Batch, result.Details.Count, false);
    }

    /// <summary>
    /// 取得會議與兩種通知來源，所有資料使用呼叫端同一連線及交易。
    /// </summary>
    private static async Task<StockNoticesData?> ReadNoticesAsync(SqlConnection connection, SqlTransaction transaction,
        string companyCode, string meetingId, CancellationToken cancellationToken)
    {
        var parameters = Parameters(companyCode, meetingId: meetingId);
        var meeting = await connection.QuerySingleOrDefaultAsync<StockMeetingRow>(new CommandDefinition(
            StockAffairsSql.Meetings + " AND m.Meeting_ID=@MeetingId;", parameters,
            transaction, cancellationToken: cancellationToken));
        if (meeting is null) return null;
        parameters.Add("SnapshotId", meeting.SnapshotId, DbType.AnsiString, size: 30);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            StockAffairsSql.Snapshot + StockAffairsSql.Notices, parameters, transaction, cancellationToken: cancellationToken));
        var snapshot = (await results.ReadAsync<StockSnapshotRow>()).AsList();
        var notices = (await results.ReadAsync<StockNoticeRow>()).AsList();
        return new StockNoticesData(meeting, snapshot, notices);
    }

    /// <summary>
    /// 取得股利批次、快照與已完成明細，保留缺快照資訊供 Service 判斷。
    /// </summary>
    private static async Task<StockDividendData?> ReadDividendAsync(SqlConnection connection, SqlTransaction transaction,
        string companyCode, string batchId, CancellationToken cancellationToken)
    {
        var parameters = Parameters(companyCode, batchId: batchId);
        var batch = await connection.QuerySingleOrDefaultAsync<StockDividendRow>(new CommandDefinition(
            StockAffairsSql.Dividends + " AND b.Batch_ID=@BatchId;", parameters,
            transaction, cancellationToken: cancellationToken));
        if (batch is null) return null;
        parameters.Add("SnapshotId", batch.SnapshotId, DbType.AnsiString, size: 30);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            StockAffairsSql.Snapshot + StockAffairsSql.DividendDetails, parameters,
            transaction, cancellationToken: cancellationToken));
        var snapshot = (await results.ReadAsync<StockSnapshotRow>()).AsList();
        var details = (await results.ReadAsync<StockDividendDetailRow>()).AsList();
        return new StockDividendData(batch, snapshot, details);
    }

    /// <summary>
    /// 取得限定交易生命週期的應用程式鎖，失敗時不繼續執行寫入。
    /// </summary>
    private static async Task AcquireLockAsync(SqlConnection connection, SqlTransaction transaction,
        string resource, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("LockResource", resource, DbType.String, size: 255);
        var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            StockAffairsSql.Lock, parameters, transaction, cancellationToken: cancellationToken));
        if (result < 0) throw new InvalidOperationException("無法取得股務交易鎖，請稍後查詢結果再重送相同請求。");
    }

    /// <summary>
    /// 集中指定識別碼的 SQL 字串型態及長度，避免隱含轉型及超長輸入。
    /// </summary>
    private static DynamicParameters Parameters(string companyCode, string? requestId = null,
        string? meetingId = null, string? batchId = null, string? snapshotId = null)
    {
        var parameters = new DynamicParameters();
        parameters.Add("CompanyCode", companyCode, DbType.AnsiString, size: 10);
        if (requestId is not null) parameters.Add("RequestId", requestId, DbType.AnsiString, size: 64);
        if (meetingId is not null) parameters.Add("MeetingId", meetingId, DbType.AnsiString, size: 20);
        if (batchId is not null) parameters.Add("BatchId", batchId, DbType.AnsiString, size: 20);
        if (snapshotId is not null) parameters.Add("SnapshotId", snapshotId, DbType.AnsiString, size: 30);
        return parameters;
    }
}

using System.Data;
using System.Text.Json;
using Dapper;
using Kgi.Api.Data;
using Kgi.Api.Data.Entities;
using Microsoft.Data.SqlClient;

namespace Kgi.Api.Features.Interest;

/// <summary>
/// 管理利息批次查詢、原子受理、持久進度，以及持有批次鎖的帳務交易。
/// </summary>
public sealed class InterestDao(SqlConnectionFactory connections)
{
    /// <summary>
    /// 查詢全部批次快照，不讀取尚未提交的明細。
    /// </summary>
    public async Task<List<InterestBatch>> GetBatchesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return (await connection.QueryAsync<InterestBatch>(new CommandDefinition(
            InterestSql.Batches, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// 讀取指定批次，不存在時回傳 null。
    /// </summary>
    public async Task<InterestBatch?> GetBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InterestBatch>(new CommandDefinition(
            InterestSql.Batch, BatchParameters(batchId), cancellationToken: cancellationToken));
    }

    /// <summary>
    /// 取得已提交的明細；呼叫端只對成功批次使用此方法。
    /// </summary>
    public async Task<List<InterestDetail>> GetDetailsAsync(string batchId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return (await connection.QueryAsync<InterestDetail>(new CommandDefinition(
            InterestSql.Details, BatchParameters(batchId), cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// 以單一 UPDATE 受理待處理批次；同批並行請求只有一筆能取得回應。
    /// </summary>
    public async Task<InterestBatch?> TryAcceptAsync(string batchId, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InterestBatch>(new CommandDefinition(
            InterestSql.Accept, BatchParameters(batchId), cancellationToken: cancellationToken));
    }

    /// <summary>
    /// 查詢資料庫持久佇列，記憶體佇列遺失不會漏掉已受理的批次。
    /// </summary>
    public async Task<List<string>> GetPendingBatchIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return (await connection.QueryAsync<string>(new CommandDefinition(
            InterestSql.Pending, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>
    /// 取得跨執行個體的批次鎖，將持有鎖的連線交由具體帳務工作階段管理。
    /// </summary>
    public async Task<SettlementSession?> TryOpenSettlementAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var connection = connections.CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            var parameters = BatchParameters(batchId);
            parameters.Add("LockResource", LockResource(batchId), DbType.String, size: 255);
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                InterestSql.AcquireLock, parameters, cancellationToken: cancellationToken));
            if (result < 0)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new SettlementSession(connection, batchId);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// 獨立短交易提交進度，不受明細帳務交易的回滾影響。
    /// </summary>
    public async Task UpdateProgressAsync(string batchId, int progressRate, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var parameters = BatchParameters(batchId);
        parameters.Add("ProgressRate", progressRate, DbType.Int32);
        await connection.ExecuteAsync(new CommandDefinition(InterestSql.Progress, parameters, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// 在明細已回滾後獨立保存失敗原因，不覆寫已成功提交的批次。
    /// </summary>
    public async Task MarkFailedAsync(string batchId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var parameters = BatchParameters(batchId);
        parameters.Add("ErrorMessage", errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage, DbType.String, size: 2000);
        await connection.ExecuteAsync(new CommandDefinition(InterestSql.Fail, parameters, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// 使用 ANSI 參數對齊批次主鍵型別，避免查詢時隱含轉型。
    /// </summary>
    private static DynamicParameters BatchParameters(string batchId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("BatchId", batchId, DbType.AnsiString, size: 20);
        return parameters;
    }

    /// <summary>
    /// 使用固定前綴及正規化批次編號，讓大小寫不同的同批請求共用鎖。
    /// </summary>
    private static string LockResource(string batchId) => $"Kgi.Interest.Batch:{batchId.ToUpperInvariant()}";

    /// <summary>
    /// 持有單批 session 鎖與明細交易的具體資源；Service 透過方法操作，不接觸連線或 SQL。
    /// </summary>
    public sealed class SettlementSession : IAsyncDisposable
    {
        private readonly SqlConnection connection;
        private readonly string batchId;
        private SqlTransaction? transaction;
        private bool disposed;

        /// <summary>
        /// 接手已取得 session 鎖的連線，直到整批完成或回滾後才釋放。
        /// </summary>
        internal SettlementSession(SqlConnection connection, string batchId)
        {
            this.connection = connection;
            this.batchId = batchId;
        }

        /// <summary>
        /// 取得鎖後重讀狀態，避免執行其他工作者剛完成的批次。
        /// </summary>
        public Task<InterestBatch?> GetBatchAsync(CancellationToken cancellationToken = default) =>
            connection.QuerySingleOrDefaultAsync<InterestBatch>(new CommandDefinition(
                InterestSql.Batch, BatchParameters(batchId), cancellationToken: cancellationToken));

        /// <summary>
        /// 獨立提交開始時間，作為辨識工作者中斷的持久標記。
        /// </summary>
        public async Task<bool> TryMarkStartedAsync(CancellationToken cancellationToken = default) =>
            await connection.ExecuteAsync(new CommandDefinition(InterestSql.Start,
                BatchParameters(batchId), cancellationToken: cancellationToken)) == 1;

        /// <summary>
        /// 一次讀取此批不可變的計息來源快照。
        /// </summary>
        public async Task<List<InterestSource>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
            (await connection.QueryAsync<InterestSource>(new CommandDefinition(
                InterestSql.Sources, BatchParameters(batchId), cancellationToken: cancellationToken))).AsList();

        /// <summary>
        /// 開始原子帳務交易，直到最終校驗前都不更新或鎖住批次主列。
        /// </summary>
        public async Task BeginDetailsAsync(CancellationToken cancellationToken = default)
        {
            transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        }

        /// <summary>
        /// 使用 OPENJSON 一次寫入一組計算完成的明細，避免逐筆資料庫往返。
        /// </summary>
        public async Task InsertDetailsAsync(IReadOnlyList<InterestDetail> details, CancellationToken cancellationToken = default)
        {
            var parameters = BatchParameters(batchId);
            parameters.Add("Details", JsonSerializer.Serialize(details.Select(detail => new
            {
                contractNo = detail.ContractNo,
                dailyInterest = detail.DailyInterest
            })), DbType.String, size: -1);
            await connection.ExecuteAsync(new CommandDefinition(InterestSql.InsertDetails, parameters,
                RequireTransaction(), cancellationToken: cancellationToken));
        }

        /// <summary>
        /// 在同一明細交易內用 SQL 加總並鎖定最終批次總額，保留四位小數的校驗精度。
        /// </summary>
        public Task<AmountCheck> CheckAmountsAsync(CancellationToken cancellationToken = default) =>
            connection.QuerySingleAsync<AmountCheck>(new CommandDefinition(InterestSql.CheckAmounts,
                BatchParameters(batchId), RequireTransaction(), cancellationToken: cancellationToken));

        /// <summary>
        /// 將全部明細及狀態 2／進度 100 一併提交，提交結果不明時由狀態條件防止誤覆寫。
        /// </summary>
        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            var currentTransaction = RequireTransaction();
            var affected = await connection.ExecuteAsync(new CommandDefinition(InterestSql.Complete,
                BatchParameters(batchId), currentTransaction, cancellationToken: cancellationToken));
            if (affected != 1)
            {
                throw new InvalidOperationException("結算批次狀態已變更，無法提交帳務。");
            }

            await currentTransaction.CommitAsync(cancellationToken);
            transaction = null;
            await currentTransaction.DisposeAsync();
        }

        /// <summary>
        /// 明確回滾尚未提交的明細，完成後才能在獨立連線記錄失敗。
        /// </summary>
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            var currentTransaction = transaction;
            if (currentTransaction is null)
            {
                return;
            }

            transaction = null;
            try
            {
                // 提交已完成但回應遺失時，SqlClient 可能已將交易與連線分離。
                if (currentTransaction.Connection is not null)
                {
                    await currentTransaction.RollbackAsync(cancellationToken);
                }
            }
            finally
            {
                await currentTransaction.DisposeAsync();
            }
        }

        /// <summary>
        /// 回滾未提交內容並明確釋放 session 鎖，避免鎖隨連線池留存。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                await RollbackAsync(CancellationToken.None);
            }
            finally
            {
                try
                {
                    if (connection.State == ConnectionState.Open)
                    {
                        var parameters = new DynamicParameters();
                        parameters.Add("LockResource", LockResource(batchId), DbType.String, size: 255);
                        await connection.ExecuteAsync(new CommandDefinition(InterestSql.ReleaseLock,
                            parameters, cancellationToken: CancellationToken.None));
                    }
                }
                finally
                {
                    await connection.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// 防止在尚未建立明細交易時執行帳務操作。
        /// </summary>
        private SqlTransaction RequireTransaction() => transaction
            ?? throw new InvalidOperationException("尚未建立結算帳務交易。");
    }

    /// <summary>
    /// SQL 四位小數總額校驗結果，不包含 API 格式。
    /// </summary>
    public sealed class AmountCheck
    {
        /// <summary>
        /// 此批交易內的明細實際總額（元）。
        /// </summary>
        public decimal ActualAmt { get; set; }

        /// <summary>
        /// 校驗時鎖定的主表預計總額（元）。
        /// </summary>
        public decimal ExpectedAmt { get; set; }

        /// <summary>
        /// 差異是否不超過 0.0001 元。
        /// </summary>
        public bool IsConsistent { get; set; }
    }
}

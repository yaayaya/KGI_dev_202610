using System.Globalization;
using Kgi.Api.Data.Entities;

namespace Kgi.Api.Features.Interest;

/// <summary>
/// 處理利息批次驗證、逐筆 decimal 計息、結算流程及對外金融字串。
/// </summary>
public sealed class InterestService(InterestDao dao, IConfiguration configuration, ILogger<InterestService> logger)
{
    private const decimal MaxAmount = 99999999999999.9999m;
    private const decimal MaxAnnualRate = 9.99999999m;
    private const int ChunkSize = 200;

    /// <summary>
    /// 結算功能依應用程式設定預設啟用；明確停用時 HTTP 入口不讀取資料庫。
    /// </summary>
    public bool IsEnabled { get; } = configuration.GetValue<bool>("InterestSettlement:Enabled");

    /// <summary>
    /// 取得全部批次的即時監控快照。
    /// </summary>
    public async Task<List<InterestBatchResponse>> GetBatchesAsync(CancellationToken cancellationToken = default) =>
        (await dao.GetBatchesAsync(cancellationToken)).Select(ToResponse).ToList();

    /// <summary>
    /// 取得單批狀態；只有成功後才查明細，避免監控被未提交明細的鎖阻塞。
    /// </summary>
    public async Task<InterestBatchDetailResponse> GetBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        batchId = NormalizeBatchId(batchId);
        var batch = await dao.GetBatchAsync(batchId, cancellationToken)
            ?? throw new KeyNotFoundException("找不到指定的利息結算批次。");
        var details = batch.ProcessStatus == 2
            ? await dao.GetDetailsAsync(batchId, cancellationToken)
            : [];
        return new InterestBatchDetailResponse
        {
            Batch = ToResponse(batch),
            CalculatedAmt = batch.ProcessStatus == 2 ? Format(details.Sum(detail => detail.DailyInterest)) : null,
            Details = details.Select(detail => new InterestDetailResponse
            {
                DetailId = detail.DetailId,
                ContractNo = detail.ContractNo,
                DailyInterest = Format(detail.DailyInterest)
            }).ToList()
        };
    }

    /// <summary>
    /// 原子受理待處理批次；已受理、成功或失敗的批次回傳 null，查無資料則回報不存在。
    /// </summary>
    public async Task<InterestBatchResponse?> TryStartAsync(string batchId, CancellationToken cancellationToken = default)
    {
        batchId = NormalizeBatchId(batchId);
        var accepted = await dao.TryAcceptAsync(batchId, cancellationToken);
        if (accepted is not null)
        {
            return ToResponse(accepted);
        }

        if (await dao.GetBatchAsync(batchId, cancellationToken) is null)
        {
            throw new KeyNotFoundException("找不到指定的利息結算批次。");
        }

        return null;
    }

    /// <summary>
    /// 單日利息為本金乘年利率除以 365，逐筆取四位小數，中點遠離零。
    /// </summary>
    public static decimal CalculateDailyInterest(decimal principalAmt, decimal annualRate)
    {
        if (principalAmt <= 0 || principalAmt > MaxAmount || decimal.Round(principalAmt, 4) != principalAmt)
        {
            throw new ArgumentException("計息本金須大於零、最多四位小數，且符合 DECIMAL(18,4)。", nameof(principalAmt));
        }

        if (annualRate <= 0 || annualRate > MaxAnnualRate || decimal.Round(annualRate, 8) != annualRate)
        {
            throw new ArgumentException("年利率須大於零、最多八位小數，且符合 DECIMAL(9,8)。", nameof(annualRate));
        }

        var interest = decimal.Round(principalAmt * annualRate / 365m, 4, MidpointRounding.AwayFromZero);
        if (interest > MaxAmount)
        {
            throw new ArgumentException("計算出的單筆利息超出 DECIMAL(18,4) 範圍。");
        }

        return interest;
    }

    /// <summary>
    /// 執行已受理批次；跨程序鎖、短交易進度與整批帳務交易分工，失敗後不自動重跑。
    /// </summary>
    public async Task ProcessBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        batchId = NormalizeBatchId(batchId);
        await using var session = await dao.TryOpenSettlementAsync(batchId, cancellationToken);
        if (session is null)
        {
            return;
        }

        var batch = await session.GetBatchAsync(cancellationToken);
        if (batch is null || batch.ProcessStatus != 1)
        {
            return;
        }

        // 取得鎖後仍有開始時間，代表原工作者已離線；不可把中斷帳務當成新工作重做。
        if (batch.StartedTime.HasValue)
        {
            await dao.MarkFailedAsync(batchId, "上次結算工作中斷，未完成的明細已回滾；請由維運確認後處理。", cancellationToken);
            return;
        }

        if (!await session.TryMarkStartedAsync(cancellationToken))
        {
            return;
        }

        try
        {
            var sources = await session.GetSourcesAsync(cancellationToken);
            if (sources.Count == 0)
            {
                throw new ArgumentException("此批次沒有計息來源，禁止將空批次標記為成功。");
            }

            await session.BeginDetailsAsync(cancellationToken);
            var processed = 0;
            foreach (var sourceChunk in sources.Chunk(ChunkSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var details = sourceChunk.Select(source => new InterestDetail
                {
                    BatchId = batchId,
                    ContractNo = source.ContractNo,
                    DailyInterest = CalculateDailyInterest(source.PrincipalAmt, source.AnnualRate)
                }).ToList();
                await session.InsertDetailsAsync(details, cancellationToken);
                processed += details.Count;
                var progress = (int)((long)processed * 95 / sources.Count);
                await dao.UpdateProgressAsync(batchId, progress, cancellationToken);
            }

            var check = await session.CheckAmountsAsync(cancellationToken);
            if (!check.IsConsistent)
            {
                await session.RollbackAsync(CancellationToken.None);
                await dao.MarkFailedAsync(batchId,
                    $"總額校驗失敗：預計 {Format(check.ExpectedAmt)} 元，明細 {Format(check.ActualAmt)} 元，差異 {Format(decimal.Abs(check.ActualAmt - check.ExpectedAmt))} 元，超過 0.0001 元；本批明細已全部回滾。",
                    CancellationToken.None);
                return;
            }

            await session.CompleteAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "利息批次 {BatchId} 結算未完成，將回滾明細並保存失敗狀態。", batchId);
            try
            {
                await session.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(rollbackException, "利息批次 {BatchId} 回滾失敗，關閉持鎖連線以終止未提交交易。", batchId);
                await session.DisposeAsync();
            }

            var message = exception switch
            {
                OperationCanceledException => "結算服務已停止，本批未提交的明細已回滾；請由維運確認後處理。",
                ArgumentException => $"計息來源無效：{exception.Message} 本批明細已全部回滾。",
                _ => "結算發生系統錯誤，未提交的明細已回滾；請提供批次編號由維運查詢日誌。"
            };
            await dao.MarkFailedAsync(batchId, message, CancellationToken.None);
        }
    }

    /// <summary>
    /// 批次識別碼採明確 ASCII 格式並統一大小寫，與資料庫主鍵及批次鎖一致。
    /// </summary>
    private static string NormalizeBatchId(string batchId)
    {
        batchId = batchId.Trim();
        if (batchId.Length is < 1 or > 20 || !char.IsAsciiLetterOrDigit(batchId[0]) ||
            batchId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.'))
        {
            throw new ArgumentException("批次編號須為一至二十碼，以英數字開頭，僅可包含英數字、底線、連字號及點。");
        }

        return batchId.ToUpperInvariant();
    }

    /// <summary>
    /// 金額以不受地區設定影響的四位小數字串傳輸。
    /// </summary>
    private static string Format(decimal amount) => amount.ToString("F4", CultureInfo.InvariantCulture);

    /// <summary>
    /// 將批次 POCO 轉成金融字串與明確 UTC 時間契約。
    /// </summary>
    private static InterestBatchResponse ToResponse(InterestBatch batch) => new()
    {
        BatchId = batch.BatchId,
        ExpectedAmt = Format(batch.ExpectedAmt),
        ProcessStatus = batch.ProcessStatus,
        ProgressRate = batch.ProgressRate,
        StartedTime = FormatTime(batch.StartedTime),
        FinishedTime = FormatTime(batch.FinishedTime),
        ErrorMessage = batch.ErrorMessage
    };

    /// <summary>
    /// SQL datetime2 沒有 Kind，依儲存約定標記 UTC 後輸出 ISO 8601。
    /// </summary>
    private static string? FormatTime(DateTime? time) => time.HasValue
        ? DateTime.SpecifyKind(time.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)
        : null;
}

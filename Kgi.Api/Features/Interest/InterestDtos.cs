namespace Kgi.Api.Features.Interest;

/// <summary>
/// 利息批次的監控快照，金額以四位小數字串傳輸。
/// </summary>
public sealed record InterestBatchResponse
{
    /// <summary>
    /// 批次編號，最多二十個字元。
    /// </summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>
    /// 預計總額（元），固定四位小數，獨立於本次計算結果。
    /// </summary>
    public string ExpectedAmt { get; init; } = string.Empty;

    /// <summary>
    /// 批次狀態：0 待處理、1 處理中、2 成功、9 失敗。
    /// </summary>
    public int ProcessStatus { get; init; }

    /// <summary>
    /// 處理進度（0–100）；只有成功提交後為 100。
    /// </summary>
    public int ProgressRate { get; init; }

    /// <summary>
    /// 工作者開始處理的 UTC ISO 8601 時間；尚未開始為 null。
    /// </summary>
    public string? StartedTime { get; init; }

    /// <summary>
    /// 成功或失敗的 UTC ISO 8601 時間；尚未結束為 null。
    /// </summary>
    public string? FinishedTime { get; init; }

    /// <summary>
    /// 可呈現的失敗日誌；沒有失敗時為 null，不含連線或系統機密。
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 選定批次的監控快照及已提交明細；處理中不讀取未提交帳務。
/// </summary>
public sealed record InterestBatchDetailResponse
{
    /// <summary>
    /// 本次查詢的批次狀態。
    /// </summary>
    public InterestBatchResponse Batch { get; init; } = new();

    /// <summary>
    /// 已提交明細總額（元），固定四位小數；未成功時為 null。
    /// </summary>
    public string? CalculatedAmt { get; init; }

    /// <summary>
    /// 已成功提交的合約利息明細；未成功時為空集合。
    /// </summary>
    public List<InterestDetailResponse> Details { get; init; } = [];
}

/// <summary>
/// 已提交的單筆合約利息。
/// </summary>
public sealed record InterestDetailResponse
{
    /// <summary>
    /// 明細流水號，為資料庫自動產生的正整數。
    /// </summary>
    public int DetailId { get; init; }

    /// <summary>
    /// 此批來源快照的合約編號，最多二十個字元。
    /// </summary>
    public string ContractNo { get; init; } = string.Empty;

    /// <summary>
    /// 該合約的當日利息（元），固定四位小數。
    /// </summary>
    public string DailyInterest { get; init; } = string.Empty;
}

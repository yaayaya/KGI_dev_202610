namespace Kgi.Api.Data.Entities;

/// <summary>
/// 利息批次主檔，對應 Interest_Batch，保留帳務狀態與持久化進度。
/// </summary>
public sealed class InterestBatch
{
    /// <summary>
    /// 最多二十個字元的批次編號。
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// 獨立提供的預計總額（元），保留四位小數。
    /// </summary>
    public decimal ExpectedAmt { get; set; }

    /// <summary>
    /// 狀態：0 待處理、1 處理中、2 成功、9 失敗。
    /// </summary>
    public int ProcessStatus { get; set; }

    /// <summary>
    /// 已持久化的處理進度（0–100）。
    /// </summary>
    public int ProgressRate { get; set; }

    /// <summary>
    /// 工作者實際開始時間（UTC）；尚未開始為 null。
    /// </summary>
    public DateTime? StartedTime { get; set; }

    /// <summary>
    /// 作業結束時間（UTC）；尚未結束為 null。
    /// </summary>
    public DateTime? FinishedTime { get; set; }

    /// <summary>
    /// 供監控呈現的失敗原因，最多兩千字；沒有失敗為 null。
    /// </summary>
    public string? ErrorMessage { get; set; }
}

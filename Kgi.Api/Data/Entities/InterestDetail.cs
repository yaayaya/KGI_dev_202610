namespace Kgi.Api.Data.Entities;

/// <summary>
/// 已計算的單筆合約利息，對應 Interest_Detail。
/// </summary>
public sealed class InterestDetail
{
    /// <summary>
    /// 資料庫自動編號；尚未寫入時為零。
    /// </summary>
    public int DetailId { get; set; }

    /// <summary>
    /// 所屬批次編號，最多二十個字元。
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// 對應此批來源快照的合約編號。
    /// </summary>
    public string ContractNo { get; set; } = string.Empty;

    /// <summary>
    /// 當日利息（元），保留四位小數。
    /// </summary>
    public decimal DailyInterest { get; set; }
}

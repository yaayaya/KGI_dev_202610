namespace Kgi.Api.Data.Entities;

/// <summary>
/// 每批不可變的合約計息來源快照，不從 Q2 擔保品推算本金。
/// </summary>
public sealed class InterestSource
{
    /// <summary>
    /// 所屬批次編號，最多二十個字元。
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// 來源合約編號，最多二十個字元。
    /// </summary>
    public string ContractNo { get; set; } = string.Empty;

    /// <summary>
    /// 計息本金（元），保留四位小數。
    /// </summary>
    public decimal PrincipalAmt { get; set; }

    /// <summary>
    /// 年利率，保留八位小數；0.05 表示百分之五。
    /// </summary>
    public decimal AnnualRate { get; set; }
}

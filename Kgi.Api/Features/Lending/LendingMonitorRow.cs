namespace Kgi.Api.Features.Lending;

/// <summary>
/// 監控 JOIN 查詢的純資料投影，尚未轉為 API 字串格式。
/// </summary>
public sealed class LendingMonitorRow
{
    public string ContractNo { get; set; } = string.Empty;
    public string StockId { get; set; } = string.Empty;
    public int BorrowedQty { get; set; }
    public decimal CollateralValue { get; set; }
    public decimal? ClosingPrice { get; set; }
    public decimal? MaintenanceRatio { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// 同一次監控查詢所使用的行情交易日與完整合約清單。
/// </summary>
public sealed record LendingMonitorData(DateTime? TradeDate, List<LendingMonitorRow> Contracts);

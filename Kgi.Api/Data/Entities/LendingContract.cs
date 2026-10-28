namespace Kgi.Api.Data.Entities;

/// <summary>
/// 借券合約資料，對應 Lending_Contract，不包含 API 格式或計算規則。
/// </summary>
public sealed class LendingContract
{
    public string ContractNo { get; set; } = string.Empty;
    public string StockId { get; set; } = string.Empty;
    public int BorrowedQty { get; set; }
    public decimal CollateralValue { get; set; }
}

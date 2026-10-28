namespace Kgi.Api.Data.Entities;

/// <summary>
/// 股票每日收盤行情，對應 Market_Price；交易日為沒有時區的營業日期。
/// </summary>
public sealed class MarketPrice
{
    public string StockId { get; set; } = string.Empty;
    public decimal ClosingPrice { get; set; }
    public DateTime TradeDate { get; set; }
}

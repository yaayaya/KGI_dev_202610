using System.Globalization;

namespace Kgi.Api.Features.Lending;

/// <summary>
/// 協調行情格式驗證、整批匯入與監控回應，維持率依題目要求由 SQL 一次 JOIN 計算。
/// </summary>
public sealed class LendingService(LendingDao dao, MarketPriceCsvParser parser)
{
    /// <summary>
    /// 驗證選填的交易日期並將同一份 SQL 查詢結果轉成統計與精確數值字串。
    /// </summary>
    public async Task<LendingMonitorResponse> GetMonitorAsync(string? tradeDate, CancellationToken cancellationToken = default)
    {
        DateOnly? date = null;
        if (tradeDate is not null)
        {
            if (!DateOnly.TryParseExact(tradeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                throw new ArgumentException("tradeDate 須為有效的 yyyy-MM-dd 日期。");
            }

            date = parsedDate;
        }

        var monitor = await dao.GetMonitorAsync(date, cancellationToken);
        var contracts = monitor.Contracts.Select(row => new LendingContractResponse
        {
            ContractNo = row.ContractNo,
            StockId = row.StockId,
            BorrowedQty = row.BorrowedQty,
            CollateralValue = Format(row.CollateralValue, 2),
            ClosingPrice = row.ClosingPrice.HasValue ? Format(row.ClosingPrice.Value, 4) : null,
            MaintenanceRatio = row.MaintenanceRatio.HasValue ? Format(row.MaintenanceRatio.Value, 2) : null,
            Status = row.Status
        }).ToList();

        return new LendingMonitorResponse
        {
            TradeDate = monitor.TradeDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TotalCount = contracts.Count,
            NormalCount = contracts.Count(contract => contract.Status == "Normal"),
            WarningCount = contracts.Count(contract => contract.Status == "Warning"),
            CallCount = contracts.Count(contract => contract.Status == "Call"),
            UnpricedCount = contracts.Count(contract => contract.Status == "Unpriced"),
            Contracts = contracts
        };
    }

    /// <summary>
    /// 驗證檔案限制與全部內容，確認完整覆蓋合約標的後才交易式取代同日行情。
    /// </summary>
    public async Task<LendingImportResponse> ImportAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("請選擇包含行情資料的 CSV 檔案。");
        }

        if (file.Length > MarketPriceCsvParser.MaxFileBytes)
        {
            throw new ArgumentException("CSV 檔案不得超過 2 MiB。");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("只接受副檔名為 .csv 的行情檔案。");
        }

        await using var stream = file.OpenReadStream();
        var market = await parser.ParseAsync(stream, cancellationToken);
        var missingStocks = await dao.ReplacePricesAsync(market, cancellationToken);
        if (missingStocks.Count > 0)
        {
            var examples = string.Join("、", missingStocks.Take(20));
            var remaining = missingStocks.Count > 20 ? $"等 {missingStocks.Count} 檔" : string.Empty;
            throw new ArgumentException($"CSV 未涵蓋全部合約標的：{examples}{remaining}。本次未寫入，請補齊後重新匯入。");
        }

        return new LendingImportResponse
        {
            TradeDate = market.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ImportedCount = market.Prices.Count
        };
    }

    /// <summary>
    /// 顯示值採固定小數位及遠離零的中點捨入，狀態仍使用 SQL 原始門檻結果。
    /// </summary>
    private static string Format(decimal value, int digits) =>
        decimal.Round(value, digits, MidpointRounding.AwayFromZero).ToString($"F{digits}", CultureInfo.InvariantCulture);
}

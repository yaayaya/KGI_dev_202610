using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Kgi.Api.Data.Entities;

namespace Kgi.Api.Features.Lending;

/// <summary>
/// 以 CsvHelper 解析單日 UTF-8 行情，完整驗證後才交給匯入流程，不操作資料庫。
/// </summary>
public sealed class MarketPriceCsvParser
{
    public const int MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxRows = 10_000;
    private const decimal MaxPrice = 99999999999999.9999m;
    private static readonly string[] Headers = ["Stock_ID", "Closing_Price", "Trade_Date"];
    private static readonly Regex StockPattern = new(@"^[A-Z0-9][A-Z0-9._-]{0,9}$", RegexOptions.CultureInvariant);
    private static readonly Regex PricePattern = new(@"^[0-9]+(?:\.[0-9]{1,4})?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 解析標準 CSV 引號與換行，拒絕混合日期、重複股票及不符合資料庫範圍的價格。
    /// </summary>
    public async Task<ParsedMarketPrices> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        // 不依賴上傳端宣告的 Length；即使直接呼叫解析器，也限制實際讀取量並嚴格檢查 UTF-8。
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(bytes.AsMemory(0, Math.Min(bytes.Length, MaxFileBytes + 1 - (int)buffer.Length)), cancellationToken)) > 0)
        {
            await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
            if (buffer.Length > MaxFileBytes)
            {
                throw new ArgumentException("CSV 檔案不得超過 2 MiB。");
            }
        }

        string content;
        try
        {
            content = new UTF8Encoding(false, true).GetString(buffer.ToArray()).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            throw new ArgumentException("CSV 必須使用 UTF-8 編碼，可包含 UTF-8 BOM。");
        }

        using var text = new StringReader(content);
        using var csv = new CsvReader(text, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            DetectColumnCountChanges = true,
            ExceptionMessagesContainRawData = false,
            MaxFieldSize = 256
        });

        try
        {
            if (!await csv.ReadAsync())
            {
                throw new ArgumentException("CSV 檔案沒有資料，請選擇包含表頭及行情的檔案。");
            }

            csv.ReadHeader();
            if (csv.HeaderRecord is null || !csv.HeaderRecord.Select(header => header.Trim()).SequenceEqual(Headers))
            {
                throw new ArgumentException("第 1 列：表頭須依序為 Stock_ID,Closing_Price,Trade_Date，且不得缺少、重複或增加欄位。");
            }

            var prices = new List<MarketPrice>();
            var stocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateOnly? tradeDate = null;
            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = csv.Context.Parser!.RawRow;
                if (prices.Count >= MaxRows)
                {
                    throw new ArgumentException($"第 {row} 列：CSV 最多接受 {MaxRows:N0} 筆行情。");
                }

                var stockId = (csv.GetField(0) ?? string.Empty).Trim().ToUpperInvariant();
                var priceText = (csv.GetField(1) ?? string.Empty).Trim();
                var dateText = (csv.GetField(2) ?? string.Empty).Trim();
                if (!StockPattern.IsMatch(stockId))
                {
                    throw new ArgumentException($"第 {row} 列：Stock_ID 須為一至十個英數字、點、底線或連字號，並以英數字開頭。");
                }

                if (!stocks.Add(stockId))
                {
                    throw new ArgumentException($"第 {row} 列：Stock_ID 重複，同一交易日每檔股票只能有一筆行情。");
                }

                if (!PricePattern.IsMatch(priceText) ||
                    !decimal.TryParse(priceText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var closingPrice) ||
                    closingPrice <= 0 || closingPrice > MaxPrice)
                {
                    throw new ArgumentException($"第 {row} 列：Closing_Price 必須大於零、最多四位小數，且不超過 99999999999999.9999。");
                }

                if (!DateOnly.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    throw new ArgumentException($"第 {row} 列：Trade_Date 須為有效的 yyyyMMdd 日期，例如 20260424。");
                }

                if (tradeDate.HasValue && tradeDate.Value != date)
                {
                    throw new ArgumentException($"第 {row} 列：每份 CSV 只能包含一個交易日。");
                }

                tradeDate = date;
                prices.Add(new MarketPrice { StockId = stockId, ClosingPrice = closingPrice, TradeDate = date.ToDateTime(TimeOnly.MinValue) });
            }

            if (prices.Count == 0)
            {
                throw new ArgumentException("CSV 只有表頭，至少需要一筆行情資料。");
            }

            return new ParsedMarketPrices(tradeDate!.Value, prices);
        }
        catch (CsvHelperException)
        {
            throw new ArgumentException($"第 {csv.Context.Parser?.RawRow ?? 1} 列：CSV 格式錯誤，請檢查欄位數量、引號與換行。");
        }
    }
}

/// <summary>
/// 已完成格式與資料驗證的單日行情，尚未寫入資料庫。
/// </summary>
public sealed record ParsedMarketPrices(DateOnly TradeDate, List<MarketPrice> Prices);

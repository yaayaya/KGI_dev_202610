using System.Text;
using Kgi.Api.Features.Lending;

namespace Kgi.Tests.Features.Lending;

/// <summary>
/// 驗證行情 CSV 的資料邊界與錯誤定位，不需資料庫或網頁伺服器。
/// </summary>
public sealed class MarketPriceCsvParserTests
{
    private const string Header = "Stock_ID,Closing_Price,Trade_Date\n";
    private readonly MarketPriceCsvParser parser = new();

    /// <summary>
    /// 標準引號、UTF-8 BOM、欄位周圍空白與不同換行皆保留股票代碼及十進位精度。
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task ReadsBomQuotedFieldsAndPreservesLeadingZeros(string newline)
    {
        var csv = "\uFEFF\"Stock_ID\",\"Closing_Price\",\"Trade_Date\"" + newline
            + "\" 0050 \",\"100.1234\",\"20261028\"" + newline
            + "abc,0.0001,20261028" + newline;

        var result = await ParseAsync(csv);

        Assert.Equal(new DateOnly(2026, 10, 28), result.TradeDate);
        Assert.Equal(2, result.Prices.Count);
        Assert.Equal("0050", result.Prices[0].StockId);
        Assert.Equal(100.1234m, result.Prices[0].ClosingPrice);
        Assert.Equal("ABC", result.Prices[1].StockId);
        Assert.Equal(0.0001m, result.Prices[1].ClosingPrice);
        Assert.All(result.Prices, price => Assert.Equal(new DateTime(2026, 10, 28), price.TradeDate));
    }

    /// <summary>
    /// 引號內換行屬於同一欄位，空白正規化後仍可取得正確價格。
    /// </summary>
    [Fact]
    public async Task ReadsQuotedMultilineFieldsWithoutSplittingRecords()
    {
        var result = await ParseAsync(Header + "0050,\"\r\n100.0000\r\n\",20261028\n");

        Assert.Equal(100m, Assert.Single(result.Prices).ClosingPrice);
    }

    /// <summary>
    /// 允許資料庫價格上限，避免在解析時經過浮點數而損失最後一位小數。
    /// </summary>
    [Fact]
    public async Task AcceptsExactDecimalDatabaseMaximum()
    {
        var result = await ParseAsync(Header + "0050,99999999999999.9999,20261028\n");

        Assert.Equal(99999999999999.9999m, Assert.Single(result.Prices).ClosingPrice);
    }

    /// <summary>
    /// 價格必須符合正值、最多四位小數及資料庫範圍，錯誤需指向資料列。
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.12345")]
    [InlineData("100000000000000")]
    [InlineData("1e2")]
    [InlineData("NaN")]
    [InlineData("")]
    public async Task RejectsInvalidPricesWithRowNumber(string price)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            ParseAsync(Header + $"0050,{price},20261028\n"));

        Assert.Contains("第 2 列", error.Message);
        Assert.Contains("Closing_Price", error.Message);
    }

    /// <summary>
    /// 驗證日期格式及曆法日期，避免含糊格式或不存在的日期進入資料庫。
    /// </summary>
    [Theory]
    [InlineData("2026-10-28")]
    [InlineData("20260229")]
    [InlineData("20261301")]
    [InlineData("")]
    public async Task RejectsInvalidDatesWithRowNumber(string date)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            ParseAsync(Header + $"0050,100,{date}\n"));

        Assert.Contains("第 2 列", error.Message);
        Assert.Contains("Trade_Date", error.Message);
    }

    /// <summary>
    /// 每份檔案只允許單一交易日及每檔股票一筆資料，股票正規化後同樣不得重複。
    /// </summary>
    [Theory]
    [InlineData("0050,100,20261028\n0056,100,20261029\n")]
    [InlineData("0050,100,20261028\n0050,101,20261028\n")]
    [InlineData("abc,100,20261028\nABC,101,20261028\n")]
    public async Task RejectsMixedDatesOrDuplicateStocks(string rows)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => ParseAsync(Header + rows));

        Assert.Contains("第 3 列", error.Message);
    }

    /// <summary>
    /// 欄位缺少、重複、多出或順序不同時，須拒絕整份檔案而非猜測欄位用途。
    /// </summary>
    [Theory]
    [InlineData("Stock_ID,Closing_Price\n0050,100\n")]
    [InlineData("Stock_ID,Stock_ID,Trade_Date\n0050,100,20261028\n")]
    [InlineData("Closing_Price,Stock_ID,Trade_Date\n100,0050,20261028\n")]
    [InlineData("Stock_ID,Closing_Price,Trade_Date,Extra\n0050,100,20261028,x\n")]
    public async Task RejectsUnexpectedHeaders(string csv)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => ParseAsync(csv));

        Assert.Contains("第 1 列", error.Message);
    }

    /// <summary>
    /// 空檔、僅有表頭及不完整資料列都不得當成成功匯入。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Stock_ID,Closing_Price,Trade_Date\n")]
    [InlineData("Stock_ID,Closing_Price,Trade_Date\n0050,100\n")]
    [InlineData("Stock_ID,Closing_Price,Trade_Date\n0050,100,20261028,extra\n")]
    [InlineData("Stock_ID,Closing_Price,Trade_Date\n\"0050,100,20261028\n")]
    public async Task RejectsEmptyOrMalformedFiles(string csv)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ParseAsync(csv));
    }

    /// <summary>
    /// 非 UTF-8 輸入須明確拒絕，不能以替代字元悄悄改寫股票代碼。
    /// </summary>
    [Fact]
    public async Task RejectsInvalidUtf8Bytes()
    {
        using var stream = new MemoryStream([0xFF, 0xFE, 0x00]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseAsync(stream));

        Assert.Contains("UTF-8", error.Message);
    }

    /// <summary>
    /// 解析器本身也限制實際檔案大小，不只信任 HTTP 上傳宣告的長度。
    /// </summary>
    [Fact]
    public async Task RejectsActualBytesBeyondFileLimit()
    {
        using var stream = new MemoryStream(new byte[MarketPriceCsvParser.MaxFileBytes + 1]);

        await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseAsync(stream));
    }

    /// <summary>
    /// 行情上限限制資料列數，不能靠重複股票或檔案容量驗證取代。
    /// </summary>
    [Fact]
    public async Task RejectsMoreThanTenThousandDistinctRows()
    {
        var csv = new StringBuilder(Header);
        for (var number = 0; number <= MarketPriceCsvParser.MaxRows; number++)
        {
            csv.Append($"S{number:D5},100,20261028\n");
        }

        var error = await Assert.ThrowsAsync<ArgumentException>(() => ParseAsync(csv.ToString()));

        Assert.Contains("筆行情", error.Message);
    }

    /// <summary>
    /// 請求取消時停止讀取與解析，避免離線請求繼續消耗匯入資源。
    /// </summary>
    [Fact]
    public async Task HonorsCancellationBeforeReading()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Header + "0050,100,20261028\n"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parser.ParseAsync(stream, cancellation.Token));
    }

    /// <summary>
    /// 以 UTF-8 記憶體串流提供輸入，維持測試與上傳解析入口一致。
    /// </summary>
    private async Task<ParsedMarketPrices> ParseAsync(string csv)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await parser.ParseAsync(stream);
    }
}

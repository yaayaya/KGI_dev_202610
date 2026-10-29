using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Kgi.Api.Features.Lending;
using Kgi.Api.Features.Warrants;
using Kgi.Tests.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Features.Lending;

/// <summary>
/// 在明確允許重建的共用 KGI 中驗證批次匯入、SQL 維持率與既有 Q1 資料不受影響。
/// </summary>
[Collection("Database")]
[Trait("Category", "Database")]
public sealed class LendingApiTests(DatabaseFixture database)
{
    private static readonly string[] StockIds =
    [
        "0050", "0056", "1101", "1216", "1301", "1303", "2002", "2303", "2308", "2317",
        "2327", "2330", "2345", "2357", "2379", "2382", "2412", "2454", "2881", "2882"
    ];

    /// <summary>
    /// 指定交易日尚無行情時，合約必須標示未計算，不得混入正常件數。
    /// </summary>
    [DatabaseFact]
    public async Task MissingPricesRemainUnpricedInsteadOfNormal()
    {
        var date = new DateTime(2026, 4, 24);
        await DeletePricesAsync(date);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var monitor = await GetMonitorAsync(client, date);

        Assert.Equal(1000, monitor.TotalCount);
        Assert.Equal(1000, monitor.UnpricedCount);
        Assert.Equal(0, monitor.NormalCount + monitor.WarningCount + monitor.CallCount);
        Assert.All(monitor.Contracts, contract =>
        {
            Assert.Equal("Unpriced", contract.Status);
            Assert.Null(contract.ClosingPrice);
            Assert.Null(contract.MaintenanceRatio);
        });
    }

    /// <summary>
    /// 約千筆合約一次取得計算結果；120 與 140 的邊界，以及顯示取位不得改變狀態。
    /// </summary>
    [DatabaseFact]
    public async Task ImportCalculatesAllContractsAndPreservesUnroundedStatusBoundaries()
    {
        var date = new DateTime(2026, 4, 25);
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var imported = await ImportAsync(client, CreateCsv(date));
            Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
            var result = Assert.IsType<LendingImportResponse>(
                await imported.Content.ReadFromJsonAsync<LendingImportResponse>());
            Assert.Equal("2026-04-25", result.TradeDate);
            Assert.Equal(20, result.ImportedCount);

            var monitor = await GetMonitorAsync(client, date);
            Assert.Equal(1000, monitor.TotalCount);
            Assert.Equal(1000, monitor.Contracts.Count);
            Assert.True(monitor.NormalCount > 0, "合成合約應包含正常情境。");
            Assert.True(monitor.WarningCount > 0, "合成合約應包含預警情境。");
            Assert.True(monitor.CallCount > 0, "合成合約應包含處置情境。");
            Assert.Equal(0, monitor.UnpricedCount);
            Assert.Equal(monitor.TotalCount,
                monitor.NormalCount + monitor.WarningCount + monitor.CallCount + monitor.UnpricedCount);

            var expected = new[]
            {
                (ContractNo: "CT2026000001", Ratio: "110.00", Status: "Call"),
                (ContractNo: "CT2026000002", Ratio: "120.00", Status: "Call"),
                (ContractNo: "CT2026000003", Ratio: "120.00", Status: "Warning"),
                (ContractNo: "CT2026000004", Ratio: "140.00", Status: "Warning"),
                (ContractNo: "CT2026000005", Ratio: "140.00", Status: "Normal"),
                (ContractNo: "CT2026000006", Ratio: "160.00", Status: "Normal")
            };
            foreach (var item in expected)
            {
                var contract = Assert.Single(monitor.Contracts, row => row.ContractNo == item.ContractNo);
                Assert.Equal("100.0000", contract.ClosingPrice);
                Assert.Equal(item.Ratio, contract.MaintenanceRatio);
                Assert.Equal(item.Status, contract.Status);
            }

            // Q2 行情匯入不得更動既有 Q1 權證資料。
            var warrants = Assert.IsType<List<WarrantResponse>>(
                await client.GetFromJsonAsync<List<WarrantResponse>>("/api/warrants"));
            Assert.Equal(800, warrants.Count);
            Assert.Equal("100.0000", Assert.Single(warrants, row => row.WarrantId == "03001C").StrikePrice);
        }
        finally
        {
            await DeletePricesAsync(date);
        }
    }

    /// <summary>
    /// 同日重傳不累加資料，完整替換當日行情，同時保留其他交易日。
    /// </summary>
    [DatabaseFact]
    public async Task ReimportReplacesWholeDayWithoutDuplicatingOrTouchingOtherDates()
    {
        var date = new DateTime(2026, 4, 26);
        var otherDate = new DateTime(2026, 4, 27);
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var first = await ImportAsync(client, CreateCsv(date) + "9999,100.0000,20260426\n");
            first.EnsureSuccessStatusCode();
            var before = await GetMonitorAsync(client, date);
            Assert.Equal("Normal", Assert.Single(before.Contracts,
                row => row.ContractNo == "CT2026000005").Status);
            using var other = await ImportAsync(client, CreateCsv(otherDate, 90m));
            other.EnsureSuccessStatusCode();
            using var replaced = await ImportAsync(client, CreateCsv(date, 200m));
            replaced.EnsureSuccessStatusCode();
            using var repeated = await ImportAsync(client, CreateCsv(date, 200m));
            repeated.EnsureSuccessStatusCode();

            var currentPrices = await GetPricesAsync(date);
            Assert.Equal(20, currentPrices.Count);
            Assert.All(currentPrices, price => Assert.Equal(200m, price));
            var unchangedPrices = await GetPricesAsync(otherDate);
            Assert.Equal(20, unchangedPrices.Count);
            Assert.All(unchangedPrices, price => Assert.Equal(90m, price));

            var monitor = await GetMonitorAsync(client, date);
            Assert.True(monitor.CallCount > before.CallCount, "行情上漲後，處置件數應增加。");
            Assert.Equal("55.00", Assert.Single(monitor.Contracts,
                row => row.ContractNo == "CT2026000001").MaintenanceRatio);
            var changed = Assert.Single(monitor.Contracts, row => row.ContractNo == "CT2026000005");
            Assert.Equal("70.00", changed.MaintenanceRatio);
            Assert.Equal("Call", changed.Status);
        }
        finally
        {
            await DeletePricesAsync(date, otherDate);
        }
    }

    /// <summary>
    /// 缺少任何合約標的或資料格式錯誤，都須保留同日原有完整行情。
    /// </summary>
    [DatabaseFact]
    public async Task RejectedFileDoesNotLeavePartialWritesOrDeleteExistingPrices()
    {
        var date = new DateTime(2026, 4, 28);
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var initial = await ImportAsync(client, CreateCsv(date));
            initial.EnsureSuccessStatusCode();

            var invalidFiles = new[]
            {
                CreateCsv(date, 200m, StockIds.Skip(1)),
                CreateCsv(date, 200m) + "9999,0,20260428\n"
            };
            foreach (var csv in invalidFiles)
            {
                using var rejected = await ImportAsync(client, csv);
                await AssertBadRequestAsync(rejected);
                var prices = await GetPricesAsync(date);
                Assert.Equal(20, prices.Count);
                Assert.All(prices, price => Assert.Equal(100m, price));
            }
        }
        finally
        {
            await DeletePricesAsync(date);
        }
    }

    /// <summary>
    /// 同日並行匯入須序列化為完整批次，不能混合兩批價格或產生重複鍵。
    /// </summary>
    [DatabaseFact]
    public async Task ConcurrentImportsKeepOneCompleteSetForTheTradeDate()
    {
        var date = new DateTime(2026, 4, 29);
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            var responses = await Task.WhenAll(
                ImportAsync(client, CreateCsv(date, 100m)),
                ImportAsync(client, CreateCsv(date, 200m)));
            foreach (var response in responses)
            {
                using (response)
                {
                    response.EnsureSuccessStatusCode();
                }
            }

            var prices = await GetPricesAsync(date);
            Assert.Equal(20, prices.Count);
            Assert.Single(prices.Distinct());
            Assert.Contains(prices[0], new[] { 100m, 200m });
        }
        finally
        {
            await DeletePricesAsync(date);
        }
    }

    /// <summary>
    /// INT 最大張數不可中間溢位；接近半分的維持率不可先取六位再取兩位而二次進位。
    /// </summary>
    [DatabaseFact]
    public async Task ExtremeQuantityAndNearHalfCentRatioKeepCorrectDecimalResults()
    {
        var date = new DateTime(2026, 4, 30);
        const string contractNo = "Q2_INT_MAX";
        const string roundingContractNo = "Q2_ROUNDING";
        try
        {
            await using (var connection = CreateConnection())
            {
                var parameters = new DynamicParameters();
                parameters.Add("ContractNo", contractNo, DbType.AnsiString, size: 20);
                parameters.Add("StockId", "0050", DbType.AnsiString, size: 10);
                parameters.Add("Quantity", int.MaxValue, DbType.Int32);
                parameters.Add("Collateral", 257698037640000m, DbType.Decimal, precision: 18, scale: 2);
                parameters.Add("RoundingContractNo", roundingContractNo, DbType.AnsiString, size: 20);
                parameters.Add("RoundingCollateral", 2999874.99m, DbType.Decimal, precision: 18, scale: 2);
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.Lending_Contract (Contract_No, Stock_ID, Borrowed_Qty, Collateral_Val)
                    VALUES (@ContractNo, @StockId, @Quantity, @Collateral),
                        (@RoundingContractNo, @StockId, 25, @RoundingCollateral);
                    """, parameters));
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var imported = await ImportAsync(client, CreateCsv(date));
            imported.EnsureSuccessStatusCode();
            var monitor = await GetMonitorAsync(client, date);
            var contract = Assert.Single(monitor.Contracts, row => row.ContractNo == contractNo);
            Assert.Equal(int.MaxValue, contract.BorrowedQty);
            Assert.Equal("120.00", contract.MaintenanceRatio);
            Assert.Equal("Warning", contract.Status);

            var roundingContract = Assert.Single(monitor.Contracts, row => row.ContractNo == roundingContractNo);
            Assert.Equal("119.99", roundingContract.MaintenanceRatio);
            Assert.Equal("Call", roundingContract.Status);
        }
        finally
        {
            await using var connection = CreateConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.Lending_Contract WHERE Contract_No IN @ContractNos;",
                new
                {
                    ContractNos = new[] { contractNo, roundingContractNo }
                        .Select(value => new DbString { Value = value, IsAnsi = true, Length = 20 }).ToArray()
                }));
            await DeletePricesAsync(date);
        }
    }

    /// <summary>
    /// 交易日查詢須使用明確 ISO 格式，不得接受不存在或含糊的日期。
    /// </summary>
    [DatabaseFact]
    public async Task InvalidMonitorDateReturnsProblemDetails()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        foreach (var date in new[] { "20261028", "2026-02-29", "not-a-date" })
        {
            using var response = await client.GetAsync($"/api/lending/monitor?tradeDate={date}");
            await AssertBadRequestAsync(response);
        }
    }

    /// <summary>
    /// 建立使用 fixture 正式 ConnectionStrings:Kgi 的 API 測試主機。
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Kgi", database.ConnectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Kgi"] = database.ConnectionString }));
        });

    /// <summary>
    /// 建立特定交易日的完整行情，日期及價格以固定格式輸出。
    /// </summary>
    private static string CreateCsv(DateTime date, decimal price = 100m, IEnumerable<string>? stocks = null)
    {
        var csv = new StringBuilder("Stock_ID,Closing_Price,Trade_Date\n");
        foreach (var stock in stocks ?? StockIds)
        {
            csv.Append(stock).Append(',').Append(price.ToString("F4", CultureInfo.InvariantCulture))
                .Append(',').Append(date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append('\n');
        }

        return csv.ToString();
    }

    /// <summary>
    /// 以真實 multipart 檔案欄位送入同一匯入 API，避免繞過 HTTP 綁定與驗證。
    /// </summary>
    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, string csv)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(csv, Encoding.UTF8, "text/csv"), "file", "Market_Price.csv");
        return await client.PostAsync("/api/lending/market-prices/import", form);
    }

    /// <summary>
    /// 取得指定日期的完整監控資料，保留非成功狀態供測試立即辨識。
    /// </summary>
    private static async Task<LendingMonitorResponse> GetMonitorAsync(HttpClient client, DateTime date)
    {
        using var response = await client.GetAsync(
            $"/api/lending/monitor?tradeDate={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        response.EnsureSuccessStatusCode();
        return Assert.IsType<LendingMonitorResponse>(
            await response.Content.ReadFromJsonAsync<LendingMonitorResponse>());
    }

    /// <summary>
    /// 單次查詢當日價格，驗證交易結果沒有部分覆寫或重複列。
    /// </summary>
    private async Task<List<decimal>> GetPricesAsync(DateTime date)
    {
        await using var connection = CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("TradeDate", date, DbType.Date);
        var prices = await connection.QueryAsync<decimal>(new CommandDefinition(
            "SELECT Closing_Price FROM dbo.Market_Price WHERE Trade_Date = @TradeDate ORDER BY Stock_ID;",
            parameters));
        return prices.AsList();
    }

    /// <summary>
    /// 只清理各測試明確擁有的日期，保留其他測試及 Q1 資料。
    /// </summary>
    private async Task DeletePricesAsync(params DateTime[] dates)
    {
        await using var connection = CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Market_Price WHERE Trade_Date IN @TradeDates;", new { TradeDates = dates }));
    }

    /// <summary>
    /// 建立共用 KGI 的個別連線，不跨測試共用已開啟連線。
    /// </summary>
    private SqlConnection CreateConnection() => new(database.ConnectionString);

    /// <summary>
    /// 驗證資料錯誤保留 HTTP 400 與可供畫面呈現的 ProblemDetails。
    /// </summary>
    private static async Task AssertBadRequestAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = Assert.IsType<ProblemDetails>(await response.Content.ReadFromJsonAsync<ProblemDetails>());
        Assert.Equal(400, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }
}

using System.Data;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Kgi.Api.Data.Entities;
using Kgi.Api.Features.Warrants;
using Kgi.Tests.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Features.Warrants;

[Collection("Database")]
[Trait("Category", "Database")]
public sealed class WarrantApiTests(DatabaseFixture database)
{
    [DatabaseFact]
    public async Task ListReturnsAllEightHundredWarrantsAndSupportsKeywordSearch()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var listResponse = await client.GetAsync("/api/warrants");
        listResponse.EnsureSuccessStatusCode();
        var warrants = Assert.IsType<List<WarrantResponse>>(
            await listResponse.Content.ReadFromJsonAsync<List<WarrantResponse>>());
        Assert.Equal(800, warrants.Count);
        Assert.True(warrants.Select(warrant => warrant.StrikePrice).Distinct().Count() >= 700,
            "合成資料應至少包含 700 種履約價。");
        Assert.True(warrants.Select(warrant => warrant.ConversionRatio).Distinct().Count() >= 12,
            "合成資料應至少包含 12 種行使比例。");
        Assert.True(warrants.Select(warrant => warrant.PositionQty).Distinct().Count() >= 500,
            "合成資料應至少包含 500 種庫存張數。");
        Assert.Contains(warrants, warrant => warrant.PositionQty == 0);
        Assert.Contains(warrants, warrant => warrant.WarrantType == "CALL");
        Assert.Contains(warrants, warrant => warrant.WarrantType == "PUT");

        using var searchResponse = await client.GetAsync("/api/warrants?keyword=03001");
        searchResponse.EnsureSuccessStatusCode();
        var matches = Assert.IsType<List<WarrantResponse>>(
            await searchResponse.Content.ReadFromJsonAsync<List<WarrantResponse>>());
        var match = Assert.Single(matches);
        Assert.Equal("03001C", match.WarrantId);
        Assert.Equal("100.0000", match.StrikePrice);
        Assert.Equal("0.1000", match.ConversionRatio);
    }

    /// <summary>
    /// 特殊符號須照代碼字面搜尋，不能擴大成 SQL LIKE 的萬用字元比對。
    /// </summary>
    [DatabaseFact]
    public async Task KeywordSearchTreatsSqlPatternCharactersAsLiteralText()
    {
        var warrantIds = new[] { "LIT%01", "LIT_01", "LIT[01]", "LITX01" };
        try
        {
            await using (var connection = CreateConnection())
            {
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                foreach (var warrantId in warrantIds)
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("WarrantId", warrantId, DbType.AnsiString, size: 10);
                    parameters.Add("StrikePrice", 100m, DbType.Decimal, precision: 18, scale: 4);
                    parameters.Add("ConversionRatio", 0.1m, DbType.Decimal, precision: 18, scale: 4);
                    parameters.Add("WarrantType", "CALL", DbType.AnsiString, size: 4);
                    parameters.Add("PositionQty", 1000, DbType.Int32);
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO dbo.Warrant_Master
                            (Warrant_ID, Strike_Price, Conversion_Ratio, Warrant_Type, Position_Qty)
                        VALUES (@WarrantId, @StrikePrice, @ConversionRatio, @WarrantType, @PositionQty);
                        """, parameters, transaction));
                }

                await transaction.CommitAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            var cases = new[]
            {
                (Keyword: "%", WarrantId: "LIT%01"),
                (Keyword: "_", WarrantId: "LIT_01"),
                (Keyword: "[", WarrantId: "LIT[01]"),
                (Keyword: "[01]", WarrantId: "LIT[01]")
            };
            foreach (var item in cases)
            {
                using var response = await client.GetAsync(
                    $"/api/warrants?keyword={Uri.EscapeDataString(item.Keyword)}");
                response.EnsureSuccessStatusCode();
                var matches = Assert.IsType<List<WarrantResponse>>(
                    await response.Content.ReadFromJsonAsync<List<WarrantResponse>>());
                Assert.Equal(item.WarrantId, Assert.Single(matches).WarrantId);
            }
        }
        finally
        {
            await using var connection = CreateConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.Warrant_Master WHERE Warrant_ID IN @WarrantIds;",
                new { WarrantIds = warrantIds.Select(ToVarcharWarrantId).ToArray() }));
        }
    }

    [DatabaseFact]
    public async Task PreviewCalculatesCallAndPutWithoutWritingLogs()
    {
        var before = await CountTrialsAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var cases = new (string WarrantId, decimal MarketPrice, string TheoryPrice, string HedgeQty)[]
        {
            ("03001C", 120m, "2.0000", "80.00"),
            ("03001C", 100m, "0.0000", "50.00"),
            ("03001C", 80m, "0.0000", "20.00"),
            ("03002P", 120m, "0.0000", "20.00"),
            ("03002P", 100m, "0.0000", "50.00"),
            ("03002P", 80m, "2.0000", "80.00")
        };

        foreach (var item in cases)
        {
            using var response = await client.PostAsJsonAsync(
                $"/api/warrants/{item.WarrantId}/preview", new { marketPrice = item.MarketPrice });
            response.EnsureSuccessStatusCode();
            var preview = Assert.IsType<TrialResponse>(await response.Content.ReadFromJsonAsync<TrialResponse>());
            Assert.Equal(item.TheoryPrice, preview.TheoryPrice);
            Assert.Equal(item.HedgeQty, preview.HedgeQty);
            Assert.Null(preview.LogId);
            Assert.Null(preview.CreatedTime);
        }

        Assert.Equal(before, await CountTrialsAsync());
    }

    [DatabaseFact]
    public async Task SaveRecalculatesInsteadOfTrustingClientResultsAndPersistsUtcTime()
    {
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync("/api/warrants/03001C/trials", new
            {
                marketPrice = "120.1234",
                theoryPrice = "99999.9999",
                hedgeQty = "99999.99"
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var saved = Assert.IsType<TrialResponse>(await response.Content.ReadFromJsonAsync<TrialResponse>());
            Assert.Equal("120.1234", saved.MarketPrice);
            Assert.Equal("2.0123", saved.TheoryPrice);
            Assert.Equal("80.00", saved.HedgeQty);
            Assert.NotNull(saved.LogId);
            Assert.True(saved.LogId.Value > 0);
            Assert.NotNull(saved.CreatedTime);
            Assert.Equal(DateTimeKind.Utc, saved.CreatedTime.Value.Kind);

            await using var connection = CreateConnection();
            var persisted = await connection.QuerySingleAsync<WarrantTrialLog>(new CommandDefinition(
                """
                SELECT Log_ID AS LogId, Warrant_ID AS WarrantId, Market_Price AS MarketPrice,
                    Theory_Price AS TheoryPrice, Hedge_Qty AS HedgeQty, Created_Time AS CreatedTime
                FROM dbo.Warrant_Trial_Log
                WHERE Log_ID = @LogId;
                """, new { saved.LogId }));
            Assert.Equal(saved.LogId.Value, persisted.LogId);
            Assert.Equal("03001C", persisted.WarrantId);
            Assert.Equal(120.1234m, persisted.MarketPrice);
            Assert.Equal(2.0123m, persisted.TheoryPrice);
            Assert.Equal(80m, persisted.HedgeQty);
            Assert.Equal(saved.CreatedTime.Value, persisted.CreatedTime);
        }
        finally
        {
            await DeleteTrialsAsync("03001C");
        }
    }

    [DatabaseFact]
    public async Task HistoryReturnsTenNewestWithStableTieBreakingAndFiltersByWarrant()
    {
        try
        {
            var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var trials = new List<WarrantTrialLog>();
            for (var number = 1; number <= 12; number++)
            {
                trials.Add(new WarrantTrialLog
                {
                    WarrantId = "03001C",
                    MarketPrice = 100m + number,
                    TheoryPrice = number * 0.1m,
                    HedgeQty = 80m,
                    CreatedTime = number <= 6 ? created.AddDays(1) : created
                });
            }
            await using (var connection = CreateConnection())
            {
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                foreach (var trial in trials)
                {
                    trial.LogId = await InsertTrialAsync(connection, transaction, trial);
                }

                await InsertTrialAsync(connection, transaction, new WarrantTrialLog
                {
                    WarrantId = "03002P", MarketPrice = 80m, TheoryPrice = 2m, HedgeQty = 80m,
                    CreatedTime = created.AddDays(1)
                });
                await transaction.CommitAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/api/warrants/03001C/trials");
            response.EnsureSuccessStatusCode();
            var history = Assert.IsType<List<TrialResponse>>(
                await response.Content.ReadFromJsonAsync<List<TrialResponse>>());

            Assert.Equal(10, history.Count);
            Assert.All(history, trial => Assert.Equal("03001C", trial.WarrantId));
            Assert.Equal(trials.OrderByDescending(trial => trial.CreatedTime)
                    .ThenByDescending(trial => trial.LogId).Take(10).Select(trial => trial.LogId),
                history.Select(trial => trial.LogId.GetValueOrDefault()));
        }
        finally
        {
            await DeleteTrialsAsync("03001C", "03002P");
        }
    }

    [DatabaseFact]
    public async Task InvalidPricesReturnBadRequestWithoutWritingLogs()
    {
        var before = await CountTrialsAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        foreach (var price in new[] { "0", "-1", "0.00001", "100000000000000", "不是價格" })
        {
            using var response = await client.PostAsJsonAsync("/api/warrants/03001C/trials",
                new { marketPrice = price });
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }

        Assert.Equal(before, await CountTrialsAsync());
    }

    [DatabaseFact]
    public async Task MissingWarrantsReturnNotFound()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        foreach (var suffix in new[] { "preview", "trials" })
        {
            using var response = await client.PostAsJsonAsync($"/api/warrants/not-found/{suffix}",
                new { marketPrice = "120.0000" });
            await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }

        using var historyResponse = await client.GetAsync("/api/warrants/not-found/trials");
        await AssertProblemAsync(historyResponse, HttpStatusCode.NotFound);
    }

    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Kgi", database.ConnectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Kgi"] = database.ConnectionString }));
        });

    /// <summary>
    /// 建立連向共用 KGI 的個別 SqlClient 連線。
    /// </summary>
    private SqlConnection CreateConnection() => new(database.ConnectionString);

    private async Task<int> CountTrialsAsync()
    {
        await using var connection = CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.Warrant_Trial_Log;"));
    }

    private async Task DeleteTrialsAsync(params string[] warrantIds)
    {
        await using var connection = CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Warrant_Trial_Log WHERE Warrant_ID IN @WarrantIds;",
            new { WarrantIds = warrantIds.Select(ToVarcharWarrantId).ToArray() }));
    }

    /// <summary>
    /// 讓 Dapper 清理用的清單參數沿用權證代碼 VARCHAR(10) 型別。
    /// </summary>
    private static DbString ToVarcharWarrantId(string warrantId) => new()
    {
        Value = warrantId,
        IsAnsi = true,
        Length = 10
    };

    /// <summary>
    /// 在同一交易內建立試算資料，並取得資料庫真正產生的流水號供排序驗證。
    /// </summary>
    private static Task<int> InsertTrialAsync(
        SqlConnection connection, SqlTransaction transaction, WarrantTrialLog trial)
    {
        var parameters = new DynamicParameters();
        parameters.Add("WarrantId", trial.WarrantId, DbType.AnsiString, size: 10);
        parameters.Add("MarketPrice", trial.MarketPrice, DbType.Decimal, precision: 18, scale: 4);
        parameters.Add("TheoryPrice", trial.TheoryPrice, DbType.Decimal, precision: 18, scale: 4);
        parameters.Add("HedgeQty", trial.HedgeQty, DbType.Decimal, precision: 18, scale: 2);
        parameters.Add("CreatedTime", trial.CreatedTime, DbType.DateTime);
        return connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.Warrant_Trial_Log
                (Warrant_ID, Market_Price, Theory_Price, Hedge_Qty, Created_Time)
            OUTPUT INSERTED.Log_ID
            VALUES (@WarrantId, @MarketPrice, @TheoryPrice, @HedgeQty, @CreatedTime);
            """, parameters, transaction));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = Assert.IsType<ProblemDetails>(await response.Content.ReadFromJsonAsync<ProblemDetails>());
        Assert.Equal((int)expected, problem.Status.GetValueOrDefault());
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }
}

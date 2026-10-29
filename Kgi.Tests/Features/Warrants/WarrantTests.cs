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

/// <summary>
/// 在既有 KGI 驗證權證 API；寫入案例只使用本次建立的權證，結束後只清理自身資料。
/// </summary>
[Collection("Database")]
[Trait("Category", "Database")]
public sealed class WarrantApiTests(DatabaseFixture database)
{
    [DatabaseFact]
    public async Task ListReturnsAtLeastEightHundredWarrantsAndSupportsKeywordSearch()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var listResponse = await client.GetAsync("/api/warrants");
        listResponse.EnsureSuccessStatusCode();
        var warrants = Assert.IsType<List<WarrantResponse>>(
            await listResponse.Content.ReadFromJsonAsync<List<WarrantResponse>>());
        Assert.True(warrants.Count >= 800, "既有 KGI 應保留至少 800 筆合成權證，可同時包含使用者資料。");
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
        Assert.All(matches, warrant => Assert.Contains("03001", warrant.WarrantId));
        var match = Assert.Single(matches, warrant => warrant.WarrantId == "03001C");
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
        var prefix = $"T{Guid.NewGuid():N}"[..7].ToUpperInvariant();
        var warrantIds = new[] { $"{prefix}%01", $"{prefix}_01", $"{prefix}[1]", $"{prefix}X01" };
        // 建立成功後才進入清理區段；若代碼碰撞，交易回滾且不刪除既有權證。
        await CreateTestWarrantsAsync(warrantIds);
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            var cases = new[]
            {
                (Keyword: $"{prefix}%", WarrantId: warrantIds[0]),
                (Keyword: $"{prefix}_", WarrantId: warrantIds[1]),
                (Keyword: $"{prefix}[", WarrantId: warrantIds[2]),
                (Keyword: $"{prefix}[1]", WarrantId: warrantIds[2])
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
            await DeleteTestWarrantsAsync(warrantIds);
        }
    }

    [DatabaseFact]
    public async Task PreviewCalculatesCallAndPutWithoutWritingLogs()
    {
        var before = await CountTrialsAsync("03001C", "03002P");
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

        Assert.Equal(before, await CountTrialsAsync("03001C", "03002P"));
    }

    [DatabaseFact]
    public async Task SaveRecalculatesInsteadOfTrustingClientResultsAndPersistsUtcTime()
    {
        var warrantId = NewTestWarrantId();
        await CreateTestWarrantsAsync(warrantId);
        try
        {
            var before = await CountTrialsAsync(warrantId);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync($"/api/warrants/{warrantId}/trials", new
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
            Assert.Equal(warrantId, persisted.WarrantId);
            Assert.Equal(120.1234m, persisted.MarketPrice);
            Assert.Equal(2.0123m, persisted.TheoryPrice);
            Assert.Equal(80m, persisted.HedgeQty);
            Assert.Equal(saved.CreatedTime.Value, persisted.CreatedTime);
            Assert.Equal(before + 1, await CountTrialsAsync(warrantId));
        }
        finally
        {
            await DeleteTestWarrantsAsync(warrantId);
        }
    }

    [DatabaseFact]
    public async Task HistoryReturnsTenNewestWithStableTieBreakingAndFiltersByWarrant()
    {
        var warrantId = NewTestWarrantId();
        var otherWarrantId = NewTestWarrantId();
        await CreateTestWarrantsAsync(warrantId, otherWarrantId);
        try
        {
            var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var trials = new List<WarrantTrialLog>();
            for (var number = 1; number <= 12; number++)
            {
                trials.Add(new WarrantTrialLog
                {
                    WarrantId = warrantId,
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
                    WarrantId = otherWarrantId, MarketPrice = 80m, TheoryPrice = 2m, HedgeQty = 80m,
                    CreatedTime = created.AddDays(1)
                });
                await transaction.CommitAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var response = await client.GetAsync($"/api/warrants/{warrantId}/trials");
            response.EnsureSuccessStatusCode();
            var history = Assert.IsType<List<TrialResponse>>(
                await response.Content.ReadFromJsonAsync<List<TrialResponse>>());

            Assert.Equal(10, history.Count);
            Assert.All(history, trial => Assert.Equal(warrantId, trial.WarrantId));
            Assert.Equal(trials.OrderByDescending(trial => trial.CreatedTime)
                    .ThenByDescending(trial => trial.LogId).Take(10).Select(trial => trial.LogId),
                history.Select(trial => trial.LogId.GetValueOrDefault()));
        }
        finally
        {
            await DeleteTestWarrantsAsync(warrantId, otherWarrantId);
        }
    }

    [DatabaseFact]
    public async Task InvalidPricesReturnBadRequestWithoutWritingLogs()
    {
        var warrantId = NewTestWarrantId();
        await CreateTestWarrantsAsync(warrantId);
        try
        {
            var before = await CountTrialsAsync(warrantId);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            foreach (var price in new[] { "0", "-1", "0.00001", "100000000000000", "不是價格" })
            {
                using var response = await client.PostAsJsonAsync($"/api/warrants/{warrantId}/trials",
                    new { marketPrice = price });
                await AssertProblemAsync(response, HttpStatusCode.BadRequest);
            }

            Assert.Equal(before, await CountTrialsAsync(warrantId));
        }
        finally
        {
            await DeleteTestWarrantsAsync(warrantId);
        }
    }

    [DatabaseFact]
    public async Task MissingWarrantsReturnNotFound()
    {
        var warrantId = NewTestWarrantId();
        await using (var connection = CreateConnection())
        {
            // 先確認隨機代碼不存在，避免誤對既有權證送出有效的儲存要求。
            var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.Warrant_Master WHERE Warrant_ID = @WarrantId;",
                new { WarrantId = ToVarcharWarrantId(warrantId) }));
            Assert.Equal(0, exists);
        }

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        foreach (var suffix in new[] { "preview", "trials" })
        {
            using var response = await client.PostAsJsonAsync($"/api/warrants/{warrantId}/{suffix}",
                new { marketPrice = "120.0000" });
            await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }

        using var historyResponse = await client.GetAsync($"/api/warrants/{warrantId}/trials");
        await AssertProblemAsync(historyResponse, HttpStatusCode.NotFound);
    }

    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Kgi", database.ConnectionString);
            builder.UseSetting("InterestSettlement:Enabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Kgi"] = database.ConnectionString,
                    ["InterestSettlement:Enabled"] = "false"
                }));
        });

    /// <summary>
    /// 建立連向既有 KGI 的獨立 SqlClient 連線，不重建資料庫。
    /// </summary>
    private SqlConnection CreateConnection() => new(database.ConnectionString);

    /// <summary>
    /// 只統計本次查驗的權證，避免其他功能或使用者新增紀錄影響全表計數。
    /// </summary>
    private async Task<int> CountTrialsAsync(params string[] warrantIds)
    {
        await using var connection = CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.Warrant_Trial_Log WHERE Warrant_ID IN @WarrantIds;",
            new { WarrantIds = warrantIds.Select(ToVarcharWarrantId).ToArray() }));
    }

    /// <summary>
    /// 產生測試專用的十碼代碼；若碰撞，由新增交易拒絕且不執行清理。
    /// </summary>
    private static string NewTestWarrantId() => $"T{Guid.NewGuid():N}"[..10].ToUpperInvariant();

    /// <summary>
    /// 在同一交易建立本次測試使用的 CALL 權證，全部成功後才交由測試負責清理。
    /// </summary>
    private async Task CreateTestWarrantsAsync(params string[] warrantIds)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        foreach (var warrantId in warrantIds)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.Warrant_Master
                    (Warrant_ID, Strike_Price, Conversion_Ratio, Warrant_Type, Position_Qty)
                VALUES (@WarrantId, 100.0000, 0.1000, 'CALL', 1000);
                """, new { WarrantId = ToVarcharWarrantId(warrantId) }, transaction));
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// 只刪除已由本次測試成功建立的權證與其試算，不接觸固定示範代碼或其他資料。
    /// </summary>
    private async Task DeleteTestWarrantsAsync(params string[] warrantIds)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.Warrant_Trial_Log WHERE Warrant_ID IN @WarrantIds;
            DELETE FROM dbo.Warrant_Master WHERE Warrant_ID IN @WarrantIds;
            """, new { WarrantIds = warrantIds.Select(ToVarcharWarrantId).ToArray() }, transaction));
        await transaction.CommitAsync();
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

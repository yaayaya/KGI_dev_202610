using System.Net;
using System.Net.Http.Json;
using Dapper;
using Kgi.Api.Features.StockAffairs;
using Kgi.Tests.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Features.StockAffairs;

/// <summary>
/// 以獨立公司資料驗證股務交易、快照、精確對帳與併發重送，保留原始示範資料。
/// </summary>
[Collection("Database")]
[Trait("Category", "Database")]
public sealed class StockAffairsApiTests(DatabaseFixture database)
{
    /// <summary>
    /// 僅核對本案例公司的快照部位，排除零股股東且不暴露完整身分欄位。
    /// </summary>
    [DatabaseFact]
    public async Task OwnedCompanySummaryAndMaskedRegisterMatchItsSnapshot()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var companies = Assert.IsType<List<StockCompanyResponse>>(
            await client.GetFromJsonAsync<List<StockCompanyResponse>>("/api/stock-affairs/companies"));
        var company = Assert.Single(companies, row => row.CompanyCode == data.Company);
        Assert.Equal(100, company.ShareholderCount);
        Assert.Equal("11322", company.TotalShares);
        Assert.Equal("1366", company.PledgedShares);
        Assert.Equal("12.07", company.PledgedRatio);

        using var response = await client.GetAsync(data.Path("register"));
        var register = await ReadAsync<StockRegisterResponse>(response);
        Assert.Equal(data.Company, register.CompanyCode);
        Assert.Equal(102, register.Shareholders.Count);
        Assert.All(Enumerable.Range(1, 103).Select(number => $"SH{number:0000}"),
            id => Assert.Contains(register.Candidates, row => row.ShareholderId == id));
        Assert.Equal(2, register.Shareholders.Count(row => row.ShareQty == 0));
        Assert.All(register.Shareholders, row => Assert.Contains('*', row.MaskedIdNo));
        Assert.DoesNotContain("TEST-ID-0001", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 相同請求重送不再次扣股；相同識別碼改內容必須回傳衝突。
    /// </summary>
    [DatabaseFact]
    public async Task TransferCreatesRecipientOnceAndRejectsChangedReplay()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var request = Request(Item("TRANSFER", "SH0003", "SH0103", 2));
        using var first = await client.PostAsJsonAsync(data.Path("transfers"), request);
        Assert.False((await ReadAsync<StockTransferResult>(first)).AlreadyApplied);
        using var replay = await client.PostAsJsonAsync(data.Path("transfers"), request);
        Assert.True((await ReadAsync<StockTransferResult>(replay)).AlreadyApplied);
        var changed = request with { Items = [Item("TRANSFER", "SH0003", "SH0103", 1)] };
        using var conflict = await client.PostAsJsonAsync(data.Path("transfers"), changed);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var register = await GetRegisterAsync(client, data);
        Assert.Equal(1, Assert.Single(register.Shareholders, row => row.ShareholderId == "SH0003").ShareQty);
        Assert.Equal(2, Assert.Single(register.Shareholders, row => row.ShareholderId == "SH0103").ShareQty);
        Assert.Equal(1, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Share_Transfer_Log WHERE Company_Code=@Company;"));
    }

    /// <summary>
    /// 批次後段失敗時，前段扣股、新受讓名簿、請求與紀錄必須全部回滾。
    /// </summary>
    [DatabaseFact]
    public async Task InvalidLaterItemRollsBackEntireBatch()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(data.Path("transfers"), Request(
            Item("TRANSFER", "SH0005", "SH0103", 10), Item("UNPLEDGE", "SH0004", null, 6)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var register = await GetRegisterAsync(client, data);
        Assert.Equal(10, Assert.Single(register.Shareholders, row => row.ShareholderId == "SH0005").ShareQty);
        Assert.DoesNotContain(register.Shareholders, row => row.ShareholderId == "SH0103");
        Assert.Equal(0, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Share_Operation_Request WHERE Company_Code=@Company;"));
        Assert.Equal(0, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Share_Transfer_Log WHERE Company_Code=@Company;"));
    }

    /// <summary>
    /// 同批可依序設定、全額撤銷質權及過戶；必須以每一步結果驗證下一步。
    /// </summary>
    [DatabaseFact]
    public async Task OrderedPledgeUnpledgeAndTransferUseUpdatedBalances()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(data.Path("transfers"), Request(
            Item("PLEDGE", "SH0003", null, 2), Item("UNPLEDGE", "SH0003", null, 3),
            Item("TRANSFER", "SH0003", "SH0103", 3)));
        Assert.Equal(3, (await ReadAsync<StockTransferResult>(response)).TransferCount);
        var register = await GetRegisterAsync(client, data);
        var owner = Assert.Single(register.Shareholders, row => row.ShareholderId == "SH0003");
        Assert.Equal(0, owner.ShareQty);
        Assert.Equal(0, owner.PledgeQty);
        Assert.Equal(3, Assert.Single(register.Shareholders, row => row.ShareholderId == "SH0103").ShareQty);
    }

    /// <summary>
    /// 最後一股同時被兩個請求過戶時，僅一個成功且整體股數守恆。
    /// </summary>
    [DatabaseFact]
    public async Task ConcurrentTransfersCannotSpendTheSameAvailableShare()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(data.Path("transfers"), Request(Item("TRANSFER", "SH0010", "SH0101", 1))),
            client.PostAsJsonAsync(data.Path("transfers"), Request(Item("TRANSFER", "SH0010", "SH0102", 1))));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            var register = await GetRegisterAsync(client, data);
            Assert.Equal(11322L, register.Shareholders.Sum(row => (long)row.ShareQty));
            Assert.Equal(0, Assert.Single(register.Shareholders, row => row.ShareholderId == "SH0010").AvailableQty);
            Assert.Equal(1, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Share_Transfer_Log WHERE Company_Code=@Company;"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    /// <summary>
    /// 同一請求以不同大小寫同時重送，資料庫與應用程式鎖仍視為同一次異動。
    /// </summary>
    [DatabaseFact]
    public async Task ConcurrentCaseNormalizedRequestIsAppliedOnlyOnce()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var request = Request(Item("TRANSFER", "SH0005", "SH0101", 5));
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(data.Path("transfers"), request with { RequestId = request.RequestId.ToLowerInvariant() }),
            client.PostAsJsonAsync(data.Path("transfers"), request with { RequestId = request.RequestId.ToUpperInvariant() }));
        try
        {
            var results = new List<StockTransferResult>();
            foreach (var response in responses) results.Add(await ReadAsync<StockTransferResult>(response));
            Assert.Single(results, result => !result.AlreadyApplied);
            Assert.Single(results, result => result.AlreadyApplied);
            var register = await GetRegisterAsync(client, data);
            Assert.Equal(5, Assert.Single(register.Shareholders, row => row.ShareholderId == "SH0005").ShareQty);
            Assert.Equal(5, Assert.Single(register.Shareholders, row => row.ShareholderId == "SH0101").ShareQty);
            Assert.Equal(1, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Share_Transfer_Log WHERE Company_Code=@Company;"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    /// <summary>
    /// 歷史通知使用對應基準日，並行寄發與重送不能產生重複收件人。
    /// </summary>
    [DatabaseFact]
    public async Task NoticesUseHistoricalSnapshotAndConcurrentSendIsIdempotent()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var changed = await client.PostAsJsonAsync(data.Path("transfers"), Request(Item("TRANSFER", "SH0006", "SH0008", 1)));
        changed.EnsureSuccessStatusCode();
        var url = data.Path($"meetings/{data.OldMeeting}/notices");
        var notices = Assert.IsType<StockNoticesResponse>(await client.GetFromJsonAsync<StockNoticesResponse>(url));
        Assert.False(notices.NoticeSent);
        Assert.Equal(100, Assert.Single(notices.Recipients, row => row.ShareholderId == "SH0006").ShareQty);
        Assert.Equal(100, Assert.Single(notices.Recipients, row => row.ShareholderId == "SH0008").ShareQty);
        var responses = await Task.WhenAll(client.PostAsync(url, null), client.PostAsync(url, null));
        try
        {
            foreach (var response in responses)
            {
                var sent = await ReadAsync<StockNoticesResponse>(response);
                Assert.True(sent.NoticeSent);
                Assert.Equal(100, sent.Recipients.Count);
                Assert.All(sent.Recipients, row => Assert.NotNull(row.SentTime));
            }
            Assert.Equal(100, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Meeting_Notice_Detail WHERE Meeting_ID=@OldMeeting;"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    /// <summary>
    /// 缺快照的會議明確拒絕，且不可讀取其他公司的會議或未知公司。
    /// </summary>
    [DatabaseFact]
    public async Task MissingSnapshotAndCrossCompanyResourcesNeverFallBackToCurrentRegister()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await data.ExecuteAsync("INSERT dbo.Shareholders_Meeting (Meeting_ID,Company_Code,Meeting_Type,Meeting_Date,Notice_Sent_Flag,Snapshot_ID) VALUES (@MissingMeeting,@Company,'EGM','20261231',0,NULL);");
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var missing = await client.PostAsync(data.Path($"meetings/{data.MissingMeeting}/notices"), null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var foreign = await client.GetAsync(data.Path("meetings/M-A-20261030/notices"));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var unknown = await client.GetAsync("/api/stock-affairs/UNKNOWN/register");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    /// <summary>
    /// 逐戶捨入與快照日期決定股利；確認及重送只產生一份凍結結果。
    /// </summary>
    [DatabaseFact]
    public async Task DividendRoundingAndConcurrentConfirmationPreserveExactDetailTotal()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await data.ExecuteAsync("INSERT dbo.Dividend_Batch (Batch_ID,Company_Code,Dividend_Type,Dividend_Per_Share,Total_Amount,Status,Snapshot_ID) VALUES (@OldCash,@Company,'CASH',0.0050,56.63,0,@OldSnapshot);");
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var oldResponse = await client.PostAsync(data.Path($"dividends/{data.OldCash}/preview"), null);
        Assert.Equal("56.63", (await ReadAsync<StockDividendPreview>(oldResponse)).CalculatedTotal);
        using var previewResponse = await client.PostAsync(data.Path($"dividends/{data.Cash}/preview"), null);
        var preview = await ReadAsync<StockDividendPreview>(previewResponse);
        Assert.Equal("56.64", preview.CalculatedTotal);
        Assert.True(preview.CanConfirm);
        Assert.Equal("0.01", Assert.Single(preview.Details, row => row.ShareholderId == "SH0001").DividendValue);
        var url = data.Path($"dividends/{data.Cash}/confirm");
        var responses = await Task.WhenAll(client.PostAsync(url, null), client.PostAsync(url, null));
        try
        {
            var results = new List<StockDividendResult>();
            foreach (var response in responses) results.Add(await ReadAsync<StockDividendResult>(response));
            Assert.Single(results, result => !result.AlreadyCompleted);
            Assert.Single(results, result => result.AlreadyCompleted);
            Assert.All(results, result => Assert.Equal(100, result.DetailCount));
            Assert.Equal(56.64m, await data.ScalarAsync<decimal>("SELECT SUM(Dividend_Value) FROM dbo.Dividend_Detail WHERE Batch_ID=@Cash;"));
            Assert.Equal(100, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Dividend_Detail WHERE Batch_ID=@Cash;"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    /// <summary>
    /// 總額錯誤及個別畸零股即使主檔型態合法，也不得留下任何完成狀態或明細。
    /// </summary>
    [DatabaseFact]
    public async Task InvalidDividendBatchesLeaveNoPartialDetails()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        foreach (var batch in new[] { data.BadCash, data.Fraction })
        {
            using var previewResponse = await client.PostAsync(data.Path($"dividends/{batch}/preview"), null);
            var preview = await ReadAsync<StockDividendPreview>(previewResponse);
            Assert.False(preview.CanConfirm);
            Assert.False(string.IsNullOrWhiteSpace(preview.ValidationMessage));
            using var confirmation = await client.PostAsync(data.Path($"dividends/{batch}/confirm"), null);
            Assert.Equal(HttpStatusCode.BadRequest, confirmation.StatusCode);
        }
        Assert.Equal(0, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Dividend_Detail WHERE Batch_ID IN (@BadCash,@Fraction);"));
        Assert.Equal(0, await data.ScalarAsync<int>("SELECT SUM(Status) FROM dbo.Dividend_Batch WHERE Batch_ID IN (@BadCash,@Fraction);"));
    }

    /// <summary>
    /// 股票配發使用股數單位，模擬確認不改變目前持股。
    /// </summary>
    [DatabaseFact]
    public async Task StockDividendUsesShareUnitsWithoutChangingCurrentHoldings()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var previewResponse = await client.PostAsync(data.Path($"dividends/{data.Stock}/preview"), null);
        var preview = await ReadAsync<StockDividendPreview>(previewResponse);
        Assert.Equal("SHARE", preview.Unit);
        Assert.Equal("11322.00", preview.CalculatedTotal);
        using var confirmResponse = await client.PostAsync(data.Path($"dividends/{data.Stock}/confirm"), null);
        Assert.Equal(100, (await ReadAsync<StockDividendResult>(confirmResponse)).DetailCount);
        Assert.Equal(11322L, (await GetRegisterAsync(client, data)).Shareholders.Sum(row => (long)row.ShareQty));
    }

    /// <summary>
    /// 不同批次首次插入明細時，也不能因共用空索引區間而互相死鎖。
    /// </summary>
    [DatabaseFact]
    public async Task DifferentDividendBatchesCanConfirmConcurrently()
    {
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(
            client.PostAsync(data.Path($"dividends/{data.Cash}/confirm"), null),
            client.PostAsync(data.Path($"dividends/{data.Stock}/confirm"), null));
        try
        {
            foreach (var response in responses)
            {
                Assert.Equal(100, (await ReadAsync<StockDividendResult>(response)).DetailCount);
            }
            Assert.Equal(2, await data.ScalarAsync<int>("SELECT SUM(Status) FROM dbo.Dividend_Batch WHERE Batch_ID IN (@Cash,@Stock);"));
            Assert.Equal(200, await data.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Dividend_Detail WHERE Batch_ID IN (@Cash,@Stock);"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    /// <summary>
    /// 空公司回傳零統計；金融結果超過資料庫精度時顯示可理解的錯誤而非部分寫入。
    /// </summary>
    [DatabaseFact]
    public async Task EmptyCompanyAndOverflowAreHandledWithoutServerErrors()
    {
        await using var empty = await TestCompany.CreateAsync(database.ConnectionString, includeData: false);
        await using var data = await TestCompany.CreateAsync(database.ConnectionString);
        await data.ExecuteAsync("INSERT dbo.Dividend_Batch (Batch_ID,Company_Code,Dividend_Type,Dividend_Per_Share,Total_Amount,Status,Snapshot_ID) VALUES (@OverflowBatch,@Company,'CASH',99999999999999.9999,0,0,@NewSnapshot);");
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var companies = Assert.IsType<List<StockCompanyResponse>>(await client.GetFromJsonAsync<List<StockCompanyResponse>>("/api/stock-affairs/companies"));
        var company = Assert.Single(companies, row => row.CompanyCode == empty.Company);
        Assert.Equal(0, company.ShareholderCount);
        Assert.Equal("0", company.TotalShares);
        Assert.Equal("0.00", company.PledgedRatio);
        Assert.Empty((await GetRegisterAsync(client, empty)).Shareholders);
        using var previewResponse = await client.PostAsync(data.Path($"dividends/{data.OverflowBatch}/preview"), null);
        Assert.False((await ReadAsync<StockDividendPreview>(previewResponse)).CanConfirm);
        using var confirmation = await client.PostAsync(data.Path($"dividends/{data.OverflowBatch}/confirm"), null);
        Assert.Equal(HttpStatusCode.BadRequest, confirmation.StatusCode);
    }

    /// <summary>
    /// 使用 fixture 的共用 KGI 並停用全站結算，不重建資料庫。
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
    /// 建立單批輸入，各測試使用不同識別碼避免互相依賴。
    /// </summary>
    private static StockTransferRequest Request(params StockTransferItem[] items) => new() { RequestId = Guid.NewGuid().ToString(), Items = items };

    /// <summary>
    /// 建立過戶或質權項目，保留正式 API 的型別驗證。
    /// </summary>
    private static StockTransferItem Item(string type, string from, string? to, int quantity) => new()
    {
        TransferType = type, FromShareholder = from, ToShareholder = to, Quantity = quantity
    };

    /// <summary>
    /// 取得 HTTP 成功內容，任何非成功狀態立即使案例失敗。
    /// </summary>
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>());
    }

    /// <summary>
    /// 取得測試公司目前名簿。
    /// </summary>
    private static async Task<StockRegisterResponse> GetRegisterAsync(HttpClient client, TestCompany data) =>
        Assert.IsType<StockRegisterResponse>(await client.GetFromJsonAsync<StockRegisterResponse>(data.Path("register")));

    /// <summary>
    /// 為每個案例複製不可變快照，清理時只刪除此案例擁有的公司。
    /// </summary>
    private sealed class TestCompany(string connectionString) : IAsyncDisposable
    {
        private readonly string key = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        public string Company => $"T{key}";
        public string OldSnapshot => $"S{key}A";
        public string NewSnapshot => $"S{key}B";
        public string OldMeeting => $"M{key}A";
        public string MissingMeeting => $"M{key}X";
        public string Cash => $"D{key}C";
        public string OldCash => $"D{key}O";
        public string Stock => $"D{key}S";
        public string BadCash => $"D{key}X";
        public string Fraction => $"D{key}F";
        public string OverflowBatch => $"D{key}V";

        /// <summary>
        /// 傳入公司範圍的所有參數，避免將識別碼拼接到 SQL。
        /// </summary>
        private object Parameters => new { Company, OldSnapshot, NewSnapshot, OldMeeting, MissingMeeting, Cash, OldCash, Stock, BadCash, Fraction, OverflowBatch };

        /// <summary>
        /// 從不可變快照建立自己的名簿及兩期資料，不依賴目前示範公司的可變持股。
        /// </summary>
        public static async Task<TestCompany> CreateAsync(string connectionString, bool includeData = true)
        {
            var data = new TestCompany(connectionString);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT dbo.Company_Master (Company_Code,Company_Name) VALUES (@Company,N'獨立股務測試公司');", data.Parameters, transaction));
            if (includeData)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT dbo.Shareholding_Register (Company_Code,Shareholder_ID,Share_Qty,Pledge_Qty,Last_Update_Time)
                    SELECT @Company,Shareholder_ID,Share_Qty,Pledge_Qty,'20261028'
                    FROM dbo.Shareholding_Snapshot_Detail WHERE Snapshot_ID='S-C-20261028';
                    INSERT dbo.Shareholding_Register (Company_Code,Shareholder_ID,Share_Qty,Pledge_Qty,Last_Update_Time)
                    VALUES (@Company,'SH0101',0,0,'20261028'),(@Company,'SH0102',0,0,'20261028');
                    INSERT dbo.Shareholding_Snapshot (Snapshot_ID,Company_Code,Record_Date,Created_Time)
                    VALUES (@OldSnapshot,@Company,'20260930','20260930'),(@NewSnapshot,@Company,'20261028','20261028');
                    INSERT dbo.Shareholding_Snapshot_Detail (Snapshot_ID,Shareholder_ID,Shareholder_Name,Address,Share_Qty,Pledge_Qty)
                    SELECT CASE Snapshot_ID WHEN 'S-C-20260930' THEN @OldSnapshot ELSE @NewSnapshot END,
                        Shareholder_ID,Shareholder_Name,Address,Share_Qty,Pledge_Qty
                    FROM dbo.Shareholding_Snapshot_Detail WHERE Snapshot_ID IN ('S-C-20260930','S-C-20261028');
                    INSERT dbo.Shareholders_Meeting (Meeting_ID,Company_Code,Meeting_Type,Meeting_Date,Notice_Sent_Flag,Snapshot_ID)
                    VALUES (@OldMeeting,@Company,'AGM','20261030',0,@OldSnapshot);
                    INSERT dbo.Dividend_Batch (Batch_ID,Company_Code,Dividend_Type,Dividend_Per_Share,Total_Amount,Status,Snapshot_ID)
                    VALUES (@Cash,@Company,'CASH',0.0050,56.64,0,@NewSnapshot),
                        (@Stock,@Company,'STOCK',1.0000,11322.00,0,@OldSnapshot),
                        (@BadCash,@Company,'CASH',0.0050,56.61,0,@NewSnapshot),
                        (@Fraction,@Company,'STOCK',0.5000,5661.00,0,@NewSnapshot);
                    """, data.Parameters, transaction));
            }
            await transaction.CommitAsync();
            return data;
        }

        /// <summary>
        /// 組成測試公司的正式 API 路徑。
        /// </summary>
        public string Path(string resource) => $"/api/stock-affairs/{Company}/{resource}";

        /// <summary>
        /// 執行案例所需的獨立資料準備。
        /// </summary>
        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync(new CommandDefinition(sql, Parameters));
        }

        /// <summary>
        /// 查核真實資料庫結果，避免只驗證 API 回應。
        /// </summary>
        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            return Assert.IsType<T>(await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, Parameters)));
        }

        /// <summary>
        /// 依外鍵順序清除本案例的資料，不動共用股東、Q1 或 Q2。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.ExecuteAsync(new CommandDefinition("""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DELETE d FROM dbo.Meeting_Notice_Detail d JOIN dbo.Shareholders_Meeting m ON m.Meeting_ID=d.Meeting_ID WHERE m.Company_Code=@Company;
                DELETE d FROM dbo.Dividend_Detail d JOIN dbo.Dividend_Batch b ON b.Batch_ID=d.Batch_ID WHERE b.Company_Code=@Company;
                DELETE dbo.Dividend_Batch WHERE Company_Code=@Company;
                DELETE dbo.Shareholders_Meeting WHERE Company_Code=@Company;
                DELETE dbo.Share_Transfer_Log WHERE Company_Code=@Company;
                DELETE dbo.Share_Operation_Request WHERE Company_Code=@Company;
                DELETE d FROM dbo.Shareholding_Snapshot_Detail d JOIN dbo.Shareholding_Snapshot s ON s.Snapshot_ID=d.Snapshot_ID WHERE s.Company_Code=@Company;
                DELETE dbo.Shareholding_Snapshot WHERE Company_Code=@Company;
                DELETE dbo.Shareholding_Register WHERE Company_Code=@Company;
                DELETE dbo.Company_Master WHERE Company_Code=@Company;
                COMMIT TRANSACTION;
                """, Parameters));
        }
    }
}

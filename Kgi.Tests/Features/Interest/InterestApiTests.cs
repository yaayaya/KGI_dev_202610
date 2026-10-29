using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Kgi.Api.Features.Interest;
using Kgi.Tests.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kgi.Tests.Features.Interest;

/// <summary>
/// 使用同一 KGI 與獨立測試批次，驗證 HTTP 防重及結算服務；不啟動全站背景掃描。
/// </summary>
[Collection("Database")]
[Trait("Category", "Database")]
public sealed class InterestApiTests(DatabaseFixture database)
{
    /// <summary>
    /// 以本次建立的千筆來源驗證分組寫入，並以獨立失敗批次確認多筆明細回滾。
    /// </summary>
    [DatabaseFact]
    public async Task ThousandSourceBatchAndMismatchExampleProduceExpectedResults()
    {
        var successId = NewBatchId();
        var failureId = NewBatchId();
        try
        {
            // 合成來源只寫入本次唯一批次，不依賴或複製既有示範資料。
            var sources = Enumerable.Range(1, 1000).Select(index =>
                ($"IC{index:000000}", 15000m + index * 457.1234m,
                    0.01m + (index * 17 % 4900) / 100000m)).ToArray();
            var failedSources = Enumerable.Range(1001, 10).Select(index =>
                ($"IC{index:000000}", 15000m + index * 457.1234m,
                    0.01m + (index * 17 % 4900) / 100000m)).ToArray();
            await CreateBatchAsync(successId, 22230.2344m, sources);
            await CreateBatchAsync(failureId, 441.3006m, failedSources);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var first = await client.PostAsync($"/api/interest/batches/{successId}/settlement", null);
            using var second = await client.PostAsync($"/api/interest/batches/{failureId}/settlement", null);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
            await ProcessOwnedBatchAsync(factory, successId);
            await ProcessOwnedBatchAsync(factory, failureId);
            var success = await WaitForStatusAsync(client, successId, 2);
            Assert.Equal(1000, success.Details.Count);
            Assert.Equal("22230.2344", success.CalculatedAmt);
            var failure = await WaitForStatusAsync(client, failureId, 9);
            Assert.Contains("0.0002", failure.Batch.ErrorMessage);
            Assert.Equal(0, await CountDetailsAsync(failureId));
        }
        finally
        {
            await DeleteBatchAsync(successId);
            await DeleteBatchAsync(failureId);
        }
    }

    /// <summary>
    /// 同批並行啟動只能受理一次，成功後禁止重跑且不增加明細。
    /// </summary>
    [DatabaseFact]
    public async Task ConcurrentStartsSettleOnceAndSuccessfulBatchCannotRunAgain()
    {
        var id = NewBatchId();
        try
        {
            await CreateBatchAsync(id, 8.2193m, [("C01", 100000m, 0.03m), ("C02", 365m, 0.00005m)]);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                client.PostAsync($"/api/interest/batches/{id}/settlement", null)));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
                Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
                Assert.Equal($"/api/interest/batches/{id}",
                    responses.Single(response => response.StatusCode == HttpStatusCode.Accepted).Headers.Location?.AbsolutePath);
            }
            finally
            {
                foreach (var response in responses) response.Dispose();
            }

            await ProcessOwnedBatchAsync(factory, id);
            var result = await WaitForStatusAsync(client, id, 2);
            Assert.Equal(100, result.Batch.ProgressRate);
            Assert.Equal("8.2193", result.Batch.ExpectedAmt);
            Assert.Equal("8.2193", result.CalculatedAmt);
            Assert.Equal(2, result.Details.Count);
            Assert.EndsWith("Z", result.Batch.StartedTime);
            Assert.EndsWith("Z", result.Batch.FinishedTime);
            using var duplicate = await client.PostAsync($"/api/interest/batches/{id}/settlement", null);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal(2, await CountDetailsAsync(id));
        }
        finally { await DeleteBatchAsync(id); }
    }

    /// <summary>
    /// 精確測試原題門檻：差異 0.0001 可接受，0.0002 必須完整回滾且保存日誌。
    /// </summary>
    [DatabaseFact]
    public async Task AmountToleranceKeepsFourDecimalsAndMismatchRollsBackAllDetails()
    {
        var acceptedId = NewBatchId();
        var rejectedId = NewBatchId();
        try
        {
            await CreateBatchAsync(acceptedId, 1.0001m, [("C01", 365m, 1m)]);
            await CreateBatchAsync(rejectedId, 2.0002m, [("C01", 365m, 1m), ("C02", 365m, 1m)]);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var first = await client.PostAsync($"/api/interest/batches/{acceptedId}/settlement", null);
            using var second = await client.PostAsync($"/api/interest/batches/{rejectedId}/settlement", null);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
            await ProcessOwnedBatchAsync(factory, acceptedId);
            await ProcessOwnedBatchAsync(factory, rejectedId);
            Assert.Equal("1.0000", (await WaitForStatusAsync(client, acceptedId, 2)).CalculatedAmt);
            var failed = await WaitForStatusAsync(client, rejectedId, 9);
            Assert.NotEmpty(failed.Batch.ErrorMessage ?? "");
            Assert.NotNull(failed.Batch.FinishedTime);
            Assert.InRange(failed.Batch.ProgressRate, 0, 99);
            Assert.Null(failed.CalculatedAmt);
            Assert.Empty(failed.Details);
            Assert.Equal(0, await CountDetailsAsync(rejectedId));
            using var retry = await client.PostAsync($"/api/interest/batches/{rejectedId}/settlement", null);
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        }
        finally
        {
            await DeleteBatchAsync(acceptedId);
            await DeleteBatchAsync(rejectedId);
        }
    }

    /// <summary>
    /// 僅鎖住本次批次的明細索引範圍，驗證進度可讀及第二處理者無法重複執行。
    /// </summary>
    [DatabaseFact]
    public async Task ProgressRemainsReadableWhileDetailTransactionIsBlocked()
    {
        var id = NewBatchId();
        try
        {
            await CreateBatchAsync(id, 1m, [("C01", 365m, 1m)]);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var blocker = new SqlConnection(database.ConnectionString);
            await blocker.OpenAsync();
            await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
            Task? processing = null;
            try
            {
                await blocker.ExecuteAsync(new CommandDefinition(
                    """
                    SELECT Detail_ID FROM dbo.Interest_Detail
                        WITH (UPDLOCK, HOLDLOCK, ROWLOCK, INDEX(UQ_Interest_Detail_Batch_ID_Contract_No))
                    WHERE Batch_ID = @Id;
                    """, new { Id = new DbString { Value = id, IsAnsi = true, Length = 20 } }, transaction));

                using var started = await client.PostAsync($"/api/interest/batches/{id}/settlement", null);
                Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
                processing = ProcessOwnedBatchAsync(factory, id, cancellation.Token);
                var running = await WaitUntilAsync(client, id, result => result.Batch.StartedTime is not null);
                Assert.Equal(1, running.Batch.ProcessStatus);
                Assert.InRange(running.Batch.ProgressRate, 1, 99);
                Assert.Empty(running.Details);
                Assert.Null(running.CalculatedAmt);

                await using var secondFactory = CreateFactory();
                using var secondClient = secondFactory.CreateClient();
                await ProcessOwnedBatchAsync(secondFactory, id, cancellation.Token);
                var otherView = await GetBatchAsync(secondClient, id);
                Assert.Equal(1, otherView.Batch.ProcessStatus);
                await transaction.RollbackAsync();
                await processing;
                Assert.Equal("1.0000", (await WaitForStatusAsync(client, id, 2)).CalculatedAmt);
            }
            finally
            {
                // 先釋放自己的範圍鎖，再取消並等待服務結束，避免資料清理與未完成交易交錯。
                if (transaction.Connection is not null) await transaction.RollbackAsync();
                cancellation.Cancel();
                if (processing is not null)
                {
                    try { await processing; }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                }
            }
        }
        finally { await DeleteBatchAsync(id); }
    }

    /// <summary>
    /// 持久受理但尚未開始的工作可接續；取得鎖後發現已開始的殘留工作須標記失敗。
    /// </summary>
    [DatabaseFact]
    public async Task RestartProcessesQueuedBatchButFailsInterruptedBatch()
    {
        var queuedId = NewBatchId();
        var interruptedId = NewBatchId();
        try
        {
            await CreateBatchAsync(queuedId, 1m, [("C01", 365m, 1m)], queued: true);
            await CreateBatchAsync(interruptedId, 1m, [("C01", 365m, 1m)], queued: true, interrupted: true);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            await ProcessOwnedBatchAsync(factory, queuedId);
            await ProcessOwnedBatchAsync(factory, interruptedId);
            Assert.Equal("1.0000", (await WaitForStatusAsync(client, queuedId, 2)).CalculatedAmt);
            var failed = await WaitForStatusAsync(client, interruptedId, 9);
            Assert.NotEmpty(failed.Batch.ErrorMessage ?? "");
            Assert.Equal(0, await CountDetailsAsync(interruptedId));
        }
        finally
        {
            await DeleteBatchAsync(queuedId);
            await DeleteBatchAsync(interruptedId);
        }
    }

    /// <summary>
    /// 查無批次回傳 404；空來源即使預計額為零，也不可被當作成功結算。
    /// </summary>
    [DatabaseFact]
    public async Task MissingBatchAndEmptySourcesAreRejected()
    {
        var id = NewBatchId();
        try
        {
            await CreateBatchAsync(id, 0m, []);
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            using var missing = await client.PostAsync($"/api/interest/batches/{NewBatchId()}/settlement", null);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var started = await client.PostAsync($"/api/interest/batches/{id}/settlement", null);
            Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
            await ProcessOwnedBatchAsync(factory, id);
            Assert.NotEmpty((await WaitForStatusAsync(client, id, 9)).Batch.ErrorMessage ?? "");
            Assert.Equal(0, await CountDetailsAsync(id));
        }
        finally { await DeleteBatchAsync(id); }
    }

    /// <summary>
    /// 測試主機沿用 KGI 並保留 HTTP 功能，移除全站掃描以避免處理既有待辦。
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Kgi", database.ConnectionString);
            builder.UseSetting("InterestSettlement:Enabled", "true");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Kgi"] = database.ConnectionString,
                    ["InterestSettlement:Enabled"] = "true"
                }));
            builder.ConfigureTestServices(services =>
            {
                var workers = services.Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(InterestSettlementWorker)).ToArray();
                foreach (var worker in workers) services.Remove(worker);
            });
        });

    /// <summary>
    /// 為每次測試建立二十碼以內的唯一識別，清理時只指定這個完整編號。
    /// </summary>
    private static string NewBatchId() => $"T{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    /// <summary>
    /// 僅執行此測試擁有的批次；scope 維持至處理完成，不掃描 KGI 的其他待辦。
    /// </summary>
    private static async Task ProcessOwnedBatchAsync(WebApplicationFactory<Program> factory, string id,
        CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InterestService>().ProcessBatchAsync(id, cancellationToken);
    }

    /// <summary>
    /// 建立獨立批次與不可變來源，不依賴示範批次的執行順序。
    /// </summary>
    private async Task CreateBatchAsync(string id, decimal expected,
        (string ContractNo, decimal Principal, decimal Rate)[] sources, bool queued = false, bool interrupted = false)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var parameters = new DynamicParameters();
        parameters.Add("Id", id, DbType.AnsiString, size: 20);
        parameters.Add("Expected", expected, DbType.Decimal, precision: 18, scale: 4);
        parameters.Add("Status", queued ? 1 : 0, DbType.Int32);
        parameters.Add("Started", interrupted ? DateTime.UtcNow.AddMinutes(-1) : null, DbType.DateTime2);
        parameters.Add("Sources", JsonSerializer.Serialize(sources.Select(source => new
        {
            contractNo = source.ContractNo, principalAmt = source.Principal, annualRate = source.Rate
        })), DbType.String, size: -1);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Interest_Batch
                (Batch_ID, Expected_Amt, Process_Status, Progress_Rate, Started_Time)
            VALUES (@Id, @Expected, @Status, 0, @Started);
            INSERT INTO dbo.Interest_Source (Batch_ID, Contract_No, Principal_Amt, Annual_Rate)
            SELECT @Id, ContractNo, PrincipalAmt, AnnualRate
            FROM OPENJSON(@Sources) WITH
                (ContractNo VARCHAR(20) '$.contractNo', PrincipalAmt DECIMAL(18,4) '$.principalAmt', AnnualRate DECIMAL(9,8) '$.annualRate');
            """, parameters));
    }

    /// <summary>
    /// 等待業務終態，失敗時立即顯示完整監控資訊。
    /// </summary>
    private static Task<InterestBatchDetailResponse> WaitForStatusAsync(HttpClient client, string id, int status) =>
        WaitUntilAsync(client, id, result =>
        {
            if (result.Batch.ProcessStatus is 2 or 9)
            {
                Assert.True(result.Batch.ProcessStatus == status, JsonSerializer.Serialize(result));
                return true;
            }
            return false;
        });

    /// <summary>
    /// 有界等待背景工作，避免使用固定長時間睡眠或永久等待。
    /// </summary>
    private static async Task<InterestBatchDetailResponse> WaitUntilAsync(HttpClient client, string id,
        Func<InterestBatchDetailResponse, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        InterestBatchDetailResponse? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await GetBatchAsync(client, id);
            if (predicate(last)) return last;
            await Task.Delay(50);
        }
        Assert.Fail($"批次未在期限內到達預期狀態：{JsonSerializer.Serialize(last)}");
        return last!;
    }

    /// <summary>
    /// 透過 HTTP 讀取監控快照，確保未直接繞過 API 行為。
    /// </summary>
    private static async Task<InterestBatchDetailResponse> GetBatchAsync(HttpClient client, string id) =>
        Assert.IsType<InterestBatchDetailResponse>(
            await client.GetFromJsonAsync<InterestBatchDetailResponse>($"/api/interest/batches/{id}"));

    /// <summary>
    /// 從資料庫確認未留下隱藏的部分明細。
    /// </summary>
    private async Task<int> CountDetailsAsync(string id)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.Interest_Detail WHERE Batch_ID=@Id;",
            new { Id = new DbString { Value = id, IsAnsi = true, Length = 20 } }));
    }

    /// <summary>
    /// 清除本測試的批次及來源，不變更其他功能或示範批次。
    /// </summary>
    private async Task DeleteBatchAsync(string id)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.Interest_Detail WHERE Batch_ID=@Id;
            DELETE FROM dbo.Interest_Source WHERE Batch_ID=@Id;
            DELETE FROM dbo.Interest_Batch WHERE Batch_ID=@Id;
            """, new { Id = new DbString { Value = id, IsAnsi = true, Length = 20 } }));
    }
}

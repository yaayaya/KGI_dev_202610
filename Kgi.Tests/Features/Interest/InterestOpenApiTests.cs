using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Features.Interest;

/// <summary>
/// 不連接資料庫，驗證 Q3 未啟用保護及前端產碼所依賴的金融契約。
/// </summary>
public sealed class InterestOpenApiTests
{
    /// <summary>
    /// 功能未啟用時，查詢與啟動皆回 503，且保留 Q1/Q2 契約與原入口。
    /// </summary>
    [Fact]
    public async Task DisabledFeatureDoesNotAccessDatabaseAndPreservesOpenApiContract()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["InterestSettlement:Enabled"] = "false",
                    ["ConnectionStrings:Kgi"] = "Server=127.0.0.1,1;Database=KGI;Integrated Security=True;Connect Timeout=1;"
                }));
        });
        using var client = factory.CreateClient();
        using var list = await client.GetAsync("/api/interest/batches");
        using var detail = await client.GetAsync("/api/interest/batches/TEST");
        using var start = await client.PostAsync("/api/interest/batches/TEST/settlement", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, list.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, detail.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, start.StatusCode);

        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/warrants", out _));
        Assert.True(paths.TryGetProperty("/api/lending/monitor", out _));
        var operation = paths.GetProperty("/api/interest/batches/{id}/settlement").GetProperty("post");
        Assert.Equal("StartInterestSettlement", operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("responses").TryGetProperty("202", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _));
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var batch = schemas.GetProperty("InterestBatchResponse");
        var properties = batch.GetProperty("properties");
        var required = batch.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToHashSet();
        Assert.All(properties.EnumerateObject(), property => Assert.Contains(property.Name, required));
        Assert.Equal("string", properties.GetProperty("expectedAmt").GetProperty("type").GetString());
        foreach (var name in new[] { "processStatus", "progressRate" })
        {
            Assert.Equal("integer", Resolve(root, properties.GetProperty(name)).GetProperty("type").GetString());
        }
        var detailProperties = schemas.GetProperty("InterestDetailResponse").GetProperty("properties");
        Assert.Equal("integer", Resolve(root, detailProperties.GetProperty("detailId")).GetProperty("type").GetString());
        Assert.Equal("string", detailProperties.GetProperty("dailyInterest").GetProperty("type").GetString());
        var calculated = schemas.GetProperty("InterestBatchDetailResponse").GetProperty("properties").GetProperty("calculatedAmt");
        Assert.Equal(new[] { "null", "string" }, calculated.GetProperty("type").EnumerateArray()
            .Select(value => value.GetString()).Order().ToArray());
    }

    /// <summary>
    /// 支援 OpenAPI 共用型別的本地引用，驗證契約而非檔案排版。
    /// </summary>
    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference)) return schema;
        var path = reference.GetString()!;
        Assert.StartsWith("#/", path);
        var value = root;
        foreach (var part in path[2..].Split('/')) value = value.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
        return value;
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Features.Lending;

/// <summary>
/// 從實際 API 取得 OpenAPI，防止 Orval 產出失真的金融、整數或檔案上傳型別；不連線資料庫。
/// </summary>
public sealed class LendingOpenApiTests
{
    /// <summary>
    /// 驗證 Q2 必填欄位、整數、可空金融字串、狀態 enum、multipart 檔案及重置契約。
    /// </summary>
    [Fact]
    public async Task LiveOpenApiPreservesExactTypesAndRequiredMultipartFile()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Kgi"] = "Server=127.0.0.1,1;Database=KGI_Test;Integrated Security=True;Connect Timeout=1;"
                }));
        });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");

        var integerProperties = new Dictionary<string, string[]>
        {
            ["LendingMonitorResponse"] = ["totalCount", "normalCount", "warningCount", "callCount", "unpricedCount"],
            ["LendingContractResponse"] = ["borrowedQty"],
            ["LendingImportResponse"] = ["importedCount"]
        };
        foreach (var entry in integerProperties)
        {
            var schema = schemas.GetProperty(entry.Key);
            var properties = schema.GetProperty("properties");
            var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();
            Assert.All(properties.EnumerateObject(), property => Assert.Contains(property.Name, required));
            foreach (var name in entry.Value)
            {
                var property = ResolveReference(root, properties.GetProperty(name));
                Assert.Equal("integer", property.GetProperty("type").GetString());
                Assert.Equal("int32", property.GetProperty("format").GetString());
            }
        }

        var contract = schemas.GetProperty("LendingContractResponse").GetProperty("properties");
        Assert.Equal("string", contract.GetProperty("collateralValue").GetProperty("type").GetString());
        foreach (var name in new[] { "closingPrice", "maintenanceRatio" })
        {
            var types = contract.GetProperty(name).GetProperty("type").EnumerateArray()
                .Select(item => item.GetString()).Order().ToArray();
            Assert.Equal(new[] { "null", "string" }, types);
        }

        var status = contract.GetProperty("status");
        Assert.Equal("string", status.GetProperty("type").GetString());
        Assert.Equal(new[] { "Call", "Normal", "Unpriced", "Warning" },
            status.GetProperty("enum").EnumerateArray().Select(item => item.GetString()).Order().ToArray());

        var requestBody = root.GetProperty("paths").GetProperty("/api/lending/market-prices/import")
            .GetProperty("post").GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());
        var form = ResolveReference(root,
            requestBody.GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema"));
        Assert.Contains("file", form.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        var file = ResolveReference(root, form.GetProperty("properties").GetProperty("file"));
        Assert.Equal("string", file.GetProperty("type").GetString());
        Assert.Equal("binary", file.GetProperty("format").GetString());

        var reset = root.GetProperty("paths").GetProperty("/api/lending/market-prices").GetProperty("delete");
        Assert.Equal("ResetMarketPrices", reset.GetProperty("operationId").GetString());
        Assert.False(reset.TryGetProperty("requestBody", out _));
        var noContent = reset.GetProperty("responses").GetProperty("204");
        Assert.False(noContent.TryGetProperty("content", out _));
        Assert.True(reset.GetProperty("responses").TryGetProperty("500", out _));
    }

    /// <summary>
    /// 接受契約將型別內嵌或以本文件的 JSON Pointer 引用，驗證最終型別而非序列化版面。
    /// </summary>
    private static JsonElement ResolveReference(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var path = Assert.IsType<string>(reference.GetString());
        Assert.StartsWith("#/", path);
        var current = root;
        foreach (var segment in path[2..].Split('/'))
        {
            current = current.GetProperty(segment.Replace("~1", "/").Replace("~0", "~"));
        }

        return current;
    }
}

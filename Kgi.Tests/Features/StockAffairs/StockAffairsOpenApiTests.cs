using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kgi.Tests.Features.StockAffairs;

/// <summary>
/// 不連資料庫即可驗證股務精確字串、整數、可空欄位及前端產碼操作名稱。
/// </summary>
public sealed class StockAffairsOpenApiTests
{
    /// <summary>
    /// 實際契約必須保留財務與聚合股數字串，並提供穩定 operationId 與狀態型別。
    /// </summary>
    [Fact]
    public async Task ContractPreservesExactValuesAndOperationIds()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("InterestSettlement:Enabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Kgi"] = "Server=127.0.0.1,1;Database=KGI;Integrated Security=True;Connect Timeout=1;",
                    ["InterestSettlement:Enabled"] = "false"
                }));
        });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var exactProperties = new Dictionary<string, string[]>
        {
            ["StockCompanyResponse"] = ["totalShares", "pledgedShares", "pledgedRatio"],
            ["StockDividendResponse"] = ["dividendPerShare", "totalAmount"],
            ["StockDividendPreview"] = ["dividendPerShare", "totalAmount", "calculatedTotal"],
            ["StockDividendDetailResponse"] = ["dividendValue"],
            ["StockDividendResult"] = ["totalAmount"]
        };
        foreach (var entry in exactProperties)
        {
            var schema = schemas.GetProperty(entry.Key);
            var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToHashSet();
            foreach (var name in entry.Value)
            {
                Assert.Equal("string", schema.GetProperty("properties").GetProperty(name).GetProperty("type").GetString());
                Assert.Contains(name, required);
            }
        }

        foreach (var (type, name) in new[]
        {
            ("StockCompanyResponse", "shareholderCount"), ("StockShareholderResponse", "shareQty"),
            ("StockShareholderResponse", "pledgeQty"), ("StockTransferItem", "quantity"),
            ("StockDividendResult", "detailCount")
        })
        {
            var property = Resolve(root, schemas.GetProperty(type).GetProperty("properties").GetProperty(name));
            Assert.Equal("integer", property.GetProperty("type").GetString());
        }

        var transferType = Resolve(root, schemas.GetProperty("StockTransferItem").GetProperty("properties").GetProperty("transferType"));
        Assert.Equal(new[] { "PLEDGE", "TRANSFER", "UNPLEDGE" },
            transferType.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).Order().ToArray());
        var nullable = schemas.GetProperty("StockDividendPreview").GetProperty("properties").GetProperty("validationMessage");
        Assert.Contains("null", nullable.GetProperty("type").EnumerateArray().Select(value => value.GetString()));

        var operations = root.GetProperty("paths").EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/stock-affairs", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(method => method.Name is "get" or "post")
                .Select(method => method.Value.GetProperty("operationId").GetString())).ToHashSet();
        Assert.Equal(10, operations.Count);
        Assert.Contains("ApplyStockTransfers", operations);
        Assert.Contains("SendStockNotices", operations);
        Assert.Contains("PreviewStockDividend", operations);
        Assert.Contains("ConfirmStockDividend", operations);
    }

    /// <summary>
    /// 將文件內引用解開後檢查最終型態，不依賴 schema 排版位置。
    /// </summary>
    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        while (schema.TryGetProperty("$ref", out var reference))
        {
            var current = root;
            foreach (var segment in reference.GetString()![2..].Split('/'))
            {
                current = current.GetProperty(segment.Replace("~1", "/").Replace("~0", "~"));
            }
            schema = current;
        }
        return schema;
    }
}

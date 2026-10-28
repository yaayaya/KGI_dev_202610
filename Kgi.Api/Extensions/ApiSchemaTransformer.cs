using System.Text.Json.Nodes;
using Kgi.Api.Features.Warrants;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Kgi.Api.Extensions;

/// <summary>
/// 補齊前端產碼使用的必要欄位、權證類型與精確股價契約，不改變 API 的既有輸入相容性。
/// </summary>
public sealed class ApiSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>
    /// 將權證 DTO 的 OpenAPI 定義整理為可直接產生 TypeScript 型別的契約。
    /// </summary>
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (schema.Properties is null ||
            (type != typeof(TrialRequest) && type != typeof(WarrantResponse) && type != typeof(TrialResponse)))
        {
            return Task.CompletedTask;
        }

        // 必填表示欄位一定存在；預覽結果的 logId、createdTime 仍保留原本的 nullable 定義。
        schema.Required = new HashSet<string>(schema.Properties.Keys);

        if (type == typeof(TrialRequest))
        {
            // 對外契約採字串，避免前端產碼轉成 number；C# 仍以 decimal 接收並相容舊有數字輸入。
            schema.Properties["marketPrice"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Pattern = @"^[0-9]+(?:\.[0-9]{1,4})?$",
                Description = "標的股價（元），以十進位字串傳送以保留精度；必須大於零、最多四位小數，且不超過 99999999999999.9999。API 亦相容 JSON 數字輸入。"
            };
        }
        else if (type == typeof(WarrantResponse))
        {
            // 回應實際輸出 JSON 數字，不沿用讀取數字時相容字串的 schema。
            schema.Properties["positionQty"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "發行商持有部位的庫存張數（張），為非負整數。"
            };
            schema.Properties["warrantType"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("CALL"), JsonValue.Create("PUT")],
                Description = "權證類型，CALL 為認購，PUT 為認售。"
            };
        }
        else if (type == typeof(TrialResponse))
        {
            schema.Properties["logId"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer | JsonSchemaType.Null,
                Format = "int32",
                Description = "試算紀錄流水號；預覽時為空值（null），成功儲存後為資料庫產生的正整數。"
            };
        }

        return Task.CompletedTask;
    }
}

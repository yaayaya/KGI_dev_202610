using System.Text.Json.Nodes;
using Kgi.Api.Features.Interest;
using Kgi.Api.Features.Lending;
using Kgi.Api.Features.Warrants;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Kgi.Api.Extensions;

/// <summary>
/// 補齊前端產碼使用的必要欄位、功能狀態與精確金融數值契約，不改變 API 的既有輸入相容性。
/// </summary>
public sealed class ApiSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>
    /// 將權證與借券 DTO 的 OpenAPI 定義整理為可直接產生 TypeScript 型別的契約。
    /// </summary>
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (schema.Properties is null ||
            (type != typeof(TrialRequest) && type != typeof(WarrantResponse) && type != typeof(TrialResponse)
             && type != typeof(LendingMonitorResponse) && type != typeof(LendingContractResponse) && type != typeof(LendingImportResponse)
             && type != typeof(InterestBatchResponse) && type != typeof(InterestBatchDetailResponse) && type != typeof(InterestDetailResponse)))
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
        else if (type == typeof(LendingContractResponse))
        {
            SetInteger(schema, "borrowedQty");
            schema.Properties["status"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("Normal"), JsonValue.Create("Warning"), JsonValue.Create("Call"), JsonValue.Create("Unpriced")],
                Description = "監控狀態：Normal 正常、Warning 預警、Call 處置、Unpriced 尚無價格。"
            };
        }
        else if (type == typeof(LendingMonitorResponse))
        {
            foreach (var property in new[] { "totalCount", "normalCount", "warningCount", "callCount", "unpricedCount" })
            {
                SetInteger(schema, property);
            }
        }
        else if (type == typeof(LendingImportResponse))
        {
            SetInteger(schema, "importedCount");
        }
        else if (type == typeof(InterestBatchResponse))
        {
            schema.Properties["processStatus"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Enum = [JsonValue.Create(0), JsonValue.Create(1), JsonValue.Create(2), JsonValue.Create(9)],
                Description = "批次狀態：0 待處理、1 處理中、2 成功、9 失敗。"
            };
            SetInteger(schema, "progressRate");
            foreach (var property in new[] { "startedTime", "finishedTime" })
            {
                schema.Properties[property] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                    Format = "date-time",
                    Description = "UTC ISO 8601 時間；尚未開始或結束時為 null。"
                };
            }
        }
        else if (type == typeof(InterestDetailResponse))
        {
            SetInteger(schema, "detailId");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 整數實際輸出 JSON number，排除全域字串數字相容設定對產碼契約的影響。
    /// </summary>
    private static void SetInteger(OpenApiSchema schema, string property)
    {
        schema.Properties![property] = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Format = "int32",
            Description = schema.Properties[property].Description
        };
    }
}

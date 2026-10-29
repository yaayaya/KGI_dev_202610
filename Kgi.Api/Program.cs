using System.Reflection;
using Kgi.Api.Data;
using Kgi.Api.Exceptions;
using Kgi.Api.Extensions;
using Kgi.Api.Features.Interest;
using Kgi.Api.Features.Lending;
using Kgi.Api.Features.StockAffairs;
using Kgi.Api.Features.Warrants;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
    options.AddSchemaTransformer<ApiSchemaTransformer>();
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddScoped<WarrantDao>();
builder.Services.AddScoped<WarrantService>();
builder.Services.AddScoped<LendingDao>();
builder.Services.AddScoped<LendingService>();
builder.Services.AddSingleton<MarketPriceCsvParser>();
builder.Services.AddScoped<StockAffairsDao>();
builder.Services.AddScoped<StockAffairsService>();
builder.Services.AddScoped<InterestDao>();
builder.Services.AddScoped<InterestService>();
// OpenAPI 建置產碼也會啟動主機，但不應連線資料庫或處理結算批次。
if (builder.Configuration.GetValue<bool>("InterestSettlement:Enabled") &&
    Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
{
    builder.Services.AddHostedService<InterestSettlementWorker>();
}

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "KGI API v1"));
}

app.MapControllers();
app.Run();

/// <summary>
/// API 啟動入口，註冊四項功能；背景結算預設啟用，僅處理已由 API 受理的批次。
/// </summary>
public partial class Program
{
}

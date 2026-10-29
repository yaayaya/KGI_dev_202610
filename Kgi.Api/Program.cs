using Kgi.Api.Data;
using Kgi.Api.Exceptions;
using Kgi.Api.Extensions;
using Kgi.Api.Features.Interest;
using Kgi.Api.Features.Lending;
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
builder.Services.AddScoped<InterestDao>();
builder.Services.AddScoped<InterestService>();
if (builder.Configuration.GetValue<bool>("InterestSettlement:Enabled"))
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
/// API 啟動入口，註冊權證、借券及結算服務；只有明確啟用時才啟動背景結算。
/// </summary>
public partial class Program
{
}

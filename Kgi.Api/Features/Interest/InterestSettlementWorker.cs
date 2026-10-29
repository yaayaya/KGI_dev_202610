using Microsoft.Data.SqlClient;

namespace Kgi.Api.Features.Interest;

/// <summary>
/// 在單一 API 內處理資料庫持久佇列；失敗批次不自動重跑，背景例外不停止既有功能。
/// </summary>
public sealed class InterestSettlementWorker(IServiceScopeFactory scopeFactory, ILogger<InterestSettlementWorker> logger)
    : BackgroundService
{
    /// <summary>
    /// 每兩秒查看已受理工作，逐批建立 scope，並將停機取消與 HTTP 請求取消分開。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var dao = scope.ServiceProvider.GetRequiredService<InterestDao>();
                    var service = scope.ServiceProvider.GetRequiredService<InterestService>();
                    var batchIds = await dao.GetPendingBatchIdsAsync(stoppingToken);
                    foreach (var batchId in batchIds)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        await service.ProcessBatchAsync(batchId, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (SqlException exception) when (exception.Number is 207 or 208)
                {
                    // 缺少 Q3 腳本只停止結算工作，不讓例外結束整個 API 或持續刷出相同日誌。
                    logger.LogError(exception, "Q3 資料庫結構尚未就緒，結算工作已停止；部署後請重新啟動 API。");
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "讀取或處理利息結算工作時發生錯誤，稍後重新檢查持久佇列。");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停機已在批次內回滾並保存失敗；此處不使主機視為背景服務崩潰。
        }
    }
}

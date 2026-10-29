using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Kgi.Api.Features.Interest;

/// <summary>
/// 提供利息批次列表、即時監控與非同步結算受理，停用時不讀取 Q3 資料庫。
/// </summary>
[ApiController]
[Route("api/interest/batches")]
public sealed class InterestController(InterestService service, ILogger<InterestController> logger) : ControllerBase
{
    /// <summary>
    /// 取得全部結算批次的已提交狀態與進度。
    /// </summary>
    [HttpGet(Name = "GetInterestBatches")]
    [ProducesResponseType<List<InterestBatchResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<InterestBatchResponse>>> GetBatches(CancellationToken cancellationToken)
    {
        if (!service.IsEnabled)
        {
            return Unavailable();
        }

        try
        {
            return Ok(await service.GetBatchesAsync(cancellationToken));
        }
        catch (SqlException exception) when (exception.Number is 207 or 208)
        {
            logger.LogError(exception, "利息結算資料庫結構尚未就緒。");
            return Unavailable();
        }
    }

    /// <summary>
    /// 取得單批狀態及已提交的明細，處理中不讀取帳務明細。
    /// </summary>
    [HttpGet("{id}", Name = "GetInterestBatch")]
    [ProducesResponseType<InterestBatchDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InterestBatchDetailResponse>> GetBatch(string id, CancellationToken cancellationToken)
    {
        if (!service.IsEnabled)
        {
            return Unavailable();
        }

        try
        {
            return Ok(await service.GetBatchAsync(id, cancellationToken));
        }
        catch (SqlException exception) when (exception.Number is 207 or 208)
        {
            logger.LogError(exception, "利息結算資料庫結構尚未就緒。");
            return Unavailable();
        }
    }

    /// <summary>
    /// 原子受理待處理批次並立即回傳 202；帳務由背景工作處理，不隨 HTTP 中斷取消。
    /// </summary>
    [HttpPost("{id}/settlement", Name = "StartInterestSettlement")]
    [ProducesResponseType<InterestBatchResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InterestBatchResponse>> StartSettlement(string id, CancellationToken cancellationToken)
    {
        if (!service.IsEnabled)
        {
            return Unavailable();
        }

        try
        {
            var batch = await service.TryStartAsync(id, cancellationToken);
            if (batch is null)
            {
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "批次無法啟動",
                    detail: "此批次已受理、已成功或已失敗，只能啟動待處理的批次。請重新整理監控狀態。");
            }

            return AcceptedAtRoute("GetInterestBatch", new { id = batch.BatchId }, batch);
        }
        catch (SqlException exception) when (exception.Number is 207 or 208)
        {
            logger.LogError(exception, "利息結算資料庫結構尚未就緒。");
            return Unavailable();
        }
    }

    /// <summary>
    /// 功能尚未啟用或尚未部署完成時，回傳可供操作端呈現的 503。
    /// </summary>
    private ObjectResult Unavailable() => Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "利息結算尚未就緒", detail: "利息結算功能尚未就緒，請稍後再試或聯絡維運人員。");
}

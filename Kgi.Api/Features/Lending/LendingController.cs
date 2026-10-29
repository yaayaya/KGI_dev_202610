using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Kgi.Api.Features.Lending;

/// <summary>
/// 提供借券維持率監控、單日 CSV 行情匯入與行情重置的 HTTP 入口。
/// </summary>
[ApiController]
[Route("api/lending")]
public sealed class LendingController(LendingService service) : ControllerBase
{
    /// <summary>
    /// 取得指定行情日的借券監控；省略日期時使用最新行情日，尚無行情的合約標為未計算。
    /// </summary>
    [HttpGet("monitor", Name = "GetLendingMonitor")]
    [ProducesResponseType<LendingMonitorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LendingMonitorResponse>> GetMonitor(
        [FromQuery] string? tradeDate, CancellationToken cancellationToken)
    {
        return Ok(await service.GetMonitorAsync(tradeDate, cancellationToken));
    }

    /// <summary>
    /// 匯入一份完整的單日 UTF-8 CSV，檔案上限 2 MiB；同日行情整批替換，格式錯誤或缺少標的時全部拒絕。
    /// </summary>
    [HttpPost("market-prices/import", Name = "ImportMarketPrices")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
    [ProducesResponseType<LendingImportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LendingImportResponse>> Import(
        [Required] IFormFile file, CancellationToken cancellationToken)
    {
        return Ok(await service.ImportAsync(file, cancellationToken));
    }

    /// <summary>
    /// 清除全部交易日的匯入行情，保留借券合約及權證資料，監控回到尚未匯入狀態。
    /// </summary>
    [HttpDelete("market-prices", Name = "ResetMarketPrices")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResetMarketPrices(CancellationToken cancellationToken)
    {
        await service.ResetMarketPricesAsync(cancellationToken);
        return NoContent();
    }
}

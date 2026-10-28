using Microsoft.AspNetCore.Mvc;

namespace Kgi.Api.Features.Warrants;

/// <summary>
/// 提供權證基本資料、試算預覽、試算存檔及最近十筆紀錄的 API 入口。
/// </summary>
[ApiController]
[Route("api/warrants")]
public sealed class WarrantController(WarrantService service) : ControllerBase
{
    /// <summary>
    /// 查詢權證基本資料，可依代碼關鍵字篩選。
    /// </summary>
    [HttpGet(Name = "GetWarrants")]
    [ProducesResponseType<List<WarrantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<WarrantResponse>>> GetWarrants(
        [FromQuery] string? keyword, CancellationToken cancellationToken)
    {
        return Ok(await service.GetWarrantsAsync(keyword, cancellationToken));
    }

    /// <summary>
    /// 預覽理論價值與避險張數，不新增紀錄。
    /// </summary>
    [HttpPost("{id}/preview", Name = "PreviewWarrant")]
    [ProducesResponseType<TrialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrialResponse>> Preview(
        string id, TrialRequest request, CancellationToken cancellationToken)
    {
        return Ok(await service.PreviewAsync(id, request, cancellationToken));
    }

    /// <summary>
    /// 依標的股價重新計算並儲存試算紀錄。
    /// </summary>
    [HttpPost("{id}/trials", Name = "SaveWarrantTrial")]
    [ProducesResponseType<TrialResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrialResponse>> SaveTrial(
        string id, TrialRequest request, CancellationToken cancellationToken)
    {
        return StatusCode(StatusCodes.Status201Created,
            await service.SaveTrialAsync(id, request, cancellationToken));
    }

    /// <summary>
    /// 取得指定權證最近十筆試算紀錄。
    /// </summary>
    [HttpGet("{id}/trials", Name = "GetWarrantTrials")]
    [ProducesResponseType<List<TrialResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<TrialResponse>>> GetRecentTrials(
        string id, CancellationToken cancellationToken)
    {
        return Ok(await service.GetRecentTrialsAsync(id, cancellationToken));
    }
}

using Microsoft.AspNetCore.Mvc;

namespace Kgi.Api.Features.StockAffairs;

/// <summary>
/// 股務代理的公司、名簿、交易、會議通知及股利批次 API 入口。
/// </summary>
[ApiController]
[Route("api/stock-affairs")]
public sealed class StockAffairsController(StockAffairsService service) : ControllerBase
{
    /// <summary>
    /// 取得公司切換清單及全體名簿統計。
    /// </summary>
    [HttpGet("companies", Name = "GetStockCompanies")]
    [ProducesResponseType<List<StockCompanyResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StockCompanyResponse>>> GetStockCompanies(CancellationToken cancellationToken)
    {
        return Ok(await service.GetCompaniesAsync(cancellationToken));
    }

    /// <summary>
    /// 取得公司名簿與受讓人候選股東，身分資訊已遮罩。
    /// </summary>
    [HttpGet("{companyCode}/register", Name = "GetStockRegister")]
    [ProducesResponseType<StockRegisterResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockRegisterResponse>> GetStockRegister(string companyCode, CancellationToken cancellationToken)
    {
        return Ok(await service.GetRegisterAsync(companyCode, cancellationToken));
    }

    /// <summary>
    /// 取得公司的股權異動與稽核前後值。
    /// </summary>
    [HttpGet("{companyCode}/transfers", Name = "GetStockTransfers")]
    [ProducesResponseType<List<StockTransferResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<StockTransferResponse>>> GetStockTransfers(string companyCode, CancellationToken cancellationToken)
    {
        return Ok(await service.GetTransfersAsync(companyCode, cancellationToken));
    }

    /// <summary>
    /// 以同一交易執行單筆或批次過戶及質權異動。
    /// </summary>
    [HttpPost("{companyCode}/transfers", Name = "ApplyStockTransfers")]
    [ProducesResponseType<StockTransferResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockTransferResult>> ApplyStockTransfers(string companyCode, StockTransferRequest request, CancellationToken cancellationToken)
    {
        return Ok(await service.ApplyTransfersAsync(companyCode, request, cancellationToken));
    }

    /// <summary>
    /// 取得公司股東會與通知狀態。
    /// </summary>
    [HttpGet("{companyCode}/meetings", Name = "GetStockMeetings")]
    [ProducesResponseType<List<StockMeetingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<StockMeetingResponse>>> GetStockMeetings(string companyCode, CancellationToken cancellationToken)
    {
        return Ok(await service.GetMeetingsAsync(companyCode, cancellationToken));
    }

    /// <summary>
    /// 取得固定基準日通知預覽或已寄發的凍結明細。
    /// </summary>
    [HttpGet("{companyCode}/meetings/{meetingId}/notices", Name = "GetStockNotices")]
    [ProducesResponseType<StockNoticesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockNoticesResponse>> GetStockNotices(string companyCode, string meetingId, CancellationToken cancellationToken)
    {
        return Ok(await service.GetNoticesAsync(companyCode, meetingId, cancellationToken));
    }

    /// <summary>
    /// 模擬寄發通知，重送不重複產生或更新寄發時間。
    /// </summary>
    [HttpPost("{companyCode}/meetings/{meetingId}/notices", Name = "SendStockNotices")]
    [ProducesResponseType<StockNoticesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockNoticesResponse>> SendStockNotices(string companyCode, string meetingId, CancellationToken cancellationToken)
    {
        return Ok(await service.SendNoticesAsync(companyCode, meetingId, cancellationToken));
    }

    /// <summary>
    /// 取得現金及股票股利批次列表。
    /// </summary>
    [HttpGet("{companyCode}/dividends", Name = "GetStockDividends")]
    [ProducesResponseType<List<StockDividendResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<StockDividendResponse>>> GetStockDividends(string companyCode, CancellationToken cancellationToken)
    {
        return Ok(await service.GetDividendsAsync(companyCode, cancellationToken));
    }

    /// <summary>
    /// 逐戶試算股利與精確對帳，不改變批次狀態。
    /// </summary>
    [HttpPost("{companyCode}/dividends/{batchId}/preview", Name = "PreviewStockDividend")]
    [ProducesResponseType<StockDividendPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockDividendPreview>> PreviewStockDividend(string companyCode, string batchId, CancellationToken cancellationToken)
    {
        return Ok(await service.PreviewDividendAsync(companyCode, batchId, cancellationToken));
    }

    /// <summary>
    /// 交易內重算及確認模擬發放，同批次不可重複執行。
    /// </summary>
    [HttpPost("{companyCode}/dividends/{batchId}/confirm", Name = "ConfirmStockDividend")]
    [ProducesResponseType<StockDividendResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockDividendResult>> ConfirmStockDividend(string companyCode, string batchId, CancellationToken cancellationToken)
    {
        return Ok(await service.ConfirmDividendAsync(companyCode, batchId, cancellationToken));
    }

}

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Kgi.Api.Exceptions;

/// <summary>
/// 將業務輸入錯誤、查無資料與系統例外統一轉為 ProblemDetails，並提供追蹤識別碼。
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// 輸入錯誤回傳 400、查無資料回傳 404、識別碼衝突回傳 409；其餘例外回傳不含內部資訊的 500。
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int status;
        string title;
        string detail;
        if (exception is ArgumentException)
        {
            status = StatusCodes.Status400BadRequest;
            title = "輸入資料無效";
            detail = exception.Message;
        }
        else if (exception is KeyNotFoundException)
        {
            status = StatusCodes.Status404NotFound;
            title = "資料不存在";
            detail = exception.Message;
        }
        else if (exception is ConflictException)
        {
            status = StatusCodes.Status409Conflict;
            title = "操作內容衝突";
            detail = exception.Message;
        }
        else
        {
            status = StatusCodes.Status500InternalServerError;
            title = "系統暫時無法處理要求";
            detail = "請稍後再試，或提供追蹤識別碼以利查詢。";
            logger.LogError(exception, "處理要求時發生未預期的錯誤，追蹤識別碼：{TraceId}",
                httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (!await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        }))
        {
            // 沒有可用的 ProblemDetails 寫入器時，仍回傳相同的錯誤資料。
            await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        }

        return true;
    }
}

using DineFlow.Application.Common;
using DineFlow.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Common;

/// <summary>
/// Turns expected failures into ProblemDetails with a stable "code" extension, e.g.
/// <c>{ "status": 404, "title": "This QR code isn't active…", "code": "qr_invalid" }</c>.
/// Anything else falls through to the default 500 handler.
/// </summary>
public class AppExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, code) = exception switch
        {
            AppException app => (app.StatusCode, app.Code),
            DomainException => (StatusCodes.Status400BadRequest, "invalid_request"),
            _ => (0, string.Empty)
        };
        if (status == 0) return false;

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = exception.Message,
                Extensions = { ["code"] = code }
            }
        });
    }
}

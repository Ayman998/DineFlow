using DineFlow.Application.Common;
using DineFlow.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace DineFlow.Api.Common;

/// <summary>
/// Turns expected failures into ProblemDetails with a stable "code" extension, e.g.
/// <c>{ "status": 404, "title": "This QR code isn't active…", "code": "qr_invalid" }</c>.
/// Handling them inside MVC keeps them out of the error logs; anything else still reaches
/// the global exception handler as a 500. The title is Arabic when the request prefers Arabic (see <see cref="ArabicErrors"/>).
/// </summary>
public class AppExceptionFilter(ProblemDetailsFactory problemDetails) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, code) = context.Exception switch
        {
            AppException app => (app.StatusCode, app.Code),
            DomainException => (StatusCodes.Status400BadRequest, "invalid_request"),
            _ => (0, string.Empty)
        };
        if (status == 0) return;

        // Arabic when the caller asked for it (Accept-Language: ar) and we have a translation; otherwise the English.
        var title = context.Exception.Message;
        if (ArabicErrors.PrefersArabic(context.HttpContext.Request) && ArabicErrors.Translate(code, title) is { } arabic)
            title = arabic;

        var details = problemDetails.CreateProblemDetails(context.HttpContext, status, title: title);
        details.Extensions["code"] = code;
        if (context.Exception is AppException { Details: { } extra }) details.Extensions["details"] = extra;

        context.Result = new ObjectResult(details) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}

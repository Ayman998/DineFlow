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
/// the global exception handler as a 500.
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

        var details = problemDetails.CreateProblemDetails(context.HttpContext, status, title: context.Exception.Message);
        details.Extensions["code"] = code;
        if (context.Exception is AppException { Details: { } extra }) details.Extensions["details"] = extra;

        context.Result = new ObjectResult(details) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}

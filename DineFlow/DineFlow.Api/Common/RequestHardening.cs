using DineFlow.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace DineFlow.Api.Common;

/// <summary>
/// Gives every request a correlation id (blueprint 12 "end-to-end correlation IDs"): the caller's
/// X-Correlation-Id when it's a sensible value, otherwise a new one. It's echoed in the response,
/// becomes the request's TraceIdentifier (so ProblemDetails' traceId matches), and is attached to
/// every log line written while handling the request.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[Header].ToString();
        var id = IsSafe(incoming) ? incoming : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = id;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", id))
            await next(context);
    }

    /// <summary>8–64 letters, digits, '.', '_' or '-': safe to echo and to put in logs.</summary>
    private static bool IsSafe(string value) =>
        value.Length is >= 8 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

/// <summary>
/// Security headers on every response (blueprint 11, E15). API responses are data, never pages, so
/// they also get a CSP that forbids loading or framing anything. The sandbox payment page and
/// Swagger render HTML and keep their own rules.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers.XFrameOptions = "DENY";
        if (context.Request.Path.StartsWithSegments("/v1") || context.Request.Path.StartsWithSegments("/health"))
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        return next(context);
    }
}

/// <summary>Answers 404 feature_disabled when the feature is switched off in configuration (see <see cref="FeatureOptions"/>).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequiresFeatureAttribute(string feature) : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var features = context.HttpContext.RequestServices.GetRequiredService<IOptions<FeatureOptions>>().Value;
        if (features.IsEnabled(feature)) return;

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "This feature isn't available.",
            Extensions = { ["code"] = "feature_disabled", ["traceId"] = context.HttpContext.TraceIdentifier }
        }) { StatusCode = StatusCodes.Status404NotFound };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}

namespace DineFlow.Application.Common;

/// <summary>
/// An expected failure the API turns into a ProblemDetails response. <see cref="Code"/> is a
/// stable machine-readable value (e.g. "qr_invalid") the web apps switch on; <see cref="Details"/>
/// is optional data the client needs to recover, such as an updated cart quote.
/// </summary>
public class AppException(int statusCode, string code, string message, object? details = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public object? Details { get; } = details;

    public static AppException BadRequest(string code, string message) => new(400, code, message);
    public static AppException Unauthorized(string code, string message) => new(401, code, message);
    public static AppException Forbidden(string code, string message) => new(403, code, message);
    public static AppException NotFound(string code, string message) => new(404, code, message);
    public static AppException Conflict(string code, string message, object? details = null) => new(409, code, message, details);
    public static AppException Gone(string code, string message) => new(410, code, message);
}

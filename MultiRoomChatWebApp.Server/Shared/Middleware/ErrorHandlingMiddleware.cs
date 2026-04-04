using System.Net;
using System.Text.Json;

namespace MultiRoomChatWebApp.Server.Shared.Middleware;

/// <summary>
/// Middleware xử lý lỗi toàn cục.
/// Bắt mọi exception chưa được handle, ghi log qua Serilog,
/// và trả về một response định dạng JSON thống nhất cho client.
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = exception switch
        {
            KeyNotFoundException => (HttpStatusCode.NotFound, "Resource not found."),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, exception.Message),
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        _logger.LogError(exception,
            "Unhandled exception | Path: {Path} | StatusCode: {StatusCode}",
            context.Request.Path, (int)statusCode);

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = JsonSerializer.Serialize(new
        {
            status = (int)statusCode,
            error = message,
            traceId = context.TraceIdentifier
        });

        await context.Response.WriteAsync(response);
    }
}

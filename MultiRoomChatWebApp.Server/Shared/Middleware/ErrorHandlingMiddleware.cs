using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MultiRoomChatWebApp.Server.Shared.Exceptions;

namespace MultiRoomChatWebApp.Server.Shared.Middleware;

/// <summary>
/// Middleware xử lý lỗi toàn cục.
/// Bắt exception chưa được handle, ghi log qua Serilog,
/// và trả về response JSON thống nhất cho client.
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
        var (statusCode, code, message) = exception switch
        {
            ApiException apiException => (
                apiException.StatusCode,
                apiException.Code,
                apiException.ClientMessage),
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "not_found",
                "Không tìm thấy tài nguyên."),
            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "Bạn cần đăng nhập hoặc phiên đăng nhập đã hết hạn."),
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "Dữ liệu gửi lên không hợp lệ."),
            InvalidOperationException => (
                StatusCodes.Status409Conflict,
                "invalid_state",
                "Trạng thái hiện tại không cho phép thực hiện thao tác này."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "server_error",
                "Đã xảy ra lỗi hệ thống.")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception,
                "Lỗi chưa xử lý. Path={Path}; StatusCode={StatusCode}; ErrorCode={ErrorCode}",
                context.Request.Path,
                statusCode,
                code);
        }
        else if (exception is ApiException { Code: "refresh_token_missing" })
        {
            _logger.LogInformation(
                "Request không có phiên đăng nhập. Path={Path}; StatusCode={StatusCode}; ErrorCode={ErrorCode}",
                context.Request.Path,
                statusCode,
                code);
        }
        else
        {
            _logger.LogWarning(
                "Request bị từ chối. Path={Path}; StatusCode={StatusCode}; ErrorCode={ErrorCode}",
                context.Request.Path,
                statusCode,
                code);
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = message,
            status = statusCode,
            code,
            message,
            detail = message,
            traceId = context.TraceIdentifier
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await context.Response.WriteAsync(response);
    }
}

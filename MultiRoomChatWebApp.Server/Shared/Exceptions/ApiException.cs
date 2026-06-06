using Microsoft.AspNetCore.Http;

namespace MultiRoomChatWebApp.Server.Shared.Exceptions;

/// <summary>
/// Exception dành cho các lỗi nghiệp vụ được phép trả về client.
/// Message phải là thông điệp an toàn, không chứa token, stack trace hoặc chi tiết nội bộ.
/// </summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public string ClientMessage { get; }

    public ApiException(int statusCode, string code, string clientMessage)
        : base(clientMessage)
    {
        StatusCode = statusCode;
        Code = code;
        ClientMessage = clientMessage;
    }

    public static ApiException BadRequest(string code, string clientMessage)
        => new(StatusCodes.Status400BadRequest, code, clientMessage);

    public static ApiException Unauthorized(string code, string clientMessage)
        => new(StatusCodes.Status401Unauthorized, code, clientMessage);

    public static ApiException Forbidden(string code, string clientMessage)
        => new(StatusCodes.Status403Forbidden, code, clientMessage);

    public static ApiException NotFound(string code, string clientMessage)
        => new(StatusCodes.Status404NotFound, code, clientMessage);

    public static ApiException Conflict(string code, string clientMessage)
        => new(StatusCodes.Status409Conflict, code, clientMessage);
}

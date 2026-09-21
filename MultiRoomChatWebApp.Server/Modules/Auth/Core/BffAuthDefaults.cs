namespace MultiRoomChatWebApp.Server.Modules.Auth.Core;

/// <summary>
/// Các hằng số mặc định cấu hình cho mô hình bảo mật Backend-For-Frontend (BFF).
/// </summary>
public static class BffAuthDefaults
{
    /// <summary>
    /// Tên scheme xác thực được sử dụng riêng cho Cookie Session nội bộ.
    /// </summary>
    public const string SessionScheme = "BffSession";
    
    /// <summary>
    /// Tên claim dùng để trích xuất hoặc lưu ID của Session đăng nhập.
    /// </summary>
    public const string SessionIdClaim = "session_id";
    
    /// <summary>
    /// Tên cookie mặc định của ứng dụng, có cờ '__Host-' để tăng bảo mật.
    /// </summary>
    public const string DefaultCookieName = "__Host-chatapp_session";
}

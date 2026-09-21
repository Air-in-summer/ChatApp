using System.Security.Claims;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;

/// <summary>
/// Cung cấp quyền truy cập vào thông tin của người dùng hiện tại đang thực hiện request.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// Đối tượng chứa các claims của người dùng hiện tại, có thể null nếu chưa xác thực.
    /// </summary>
    ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// Thử lấy ID của người dùng từ context. Trả về true nếu thành công.
    /// </summary>
    bool TryGetUserId(out Guid userId);

    /// <summary>
    /// Lấy ID của người dùng từ context, ném lỗi Unauthorized nếu không tìm thấy.
    /// </summary>
    Guid GetUserIdOrThrow();

    /// <summary>
    /// Lấy tên hiển thị của người dùng từ claims, có thể truyền ID dự phòng nếu claim bị thiếu.
    /// </summary>
    string GetDisplayName(Guid? fallbackUserId = null);
}

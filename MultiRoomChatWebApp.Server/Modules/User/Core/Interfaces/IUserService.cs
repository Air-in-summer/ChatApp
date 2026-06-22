using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

public interface IUserService
{
    /// <summary>
    /// Tìm kiếm người dùng theo Username hoặc DisplayName (Case-Insensitive, Contains).
    /// </summary>
    /// <param name="keyword">Từ khóa tìm kiếm (tối thiểu 1 ký tự)</param>
    /// <param name="currentUserId">ID của người đang tìm kiếm (để loại trừ chính mình)</param>
    /// <param name="page">Trang kết quả cần lấy, bắt đầu từ 1.</param>
    /// <param name="pageSize">Số kết quả mỗi trang.</param>
    /// <returns>Kết quả tìm kiếm kèm thông tin phân trang.</returns>
    Task<UserSearchResponseDto> SearchByKeywordAsync(string keyword, Guid currentUserId, int page, int pageSize);

    /// <summary>
    /// Lay profile cua user dang dang nhap.
    /// </summary>
    Task<UserProfileDto> GetProfileAsync(Guid userId);

    /// <summary>
    /// Cap nhat displayName/avatarUrl cua user dang dang nhap.
    /// </summary>
    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request);

    /// <summary>
    /// Doi mat khau cho tai khoan password local.
    /// </summary>
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
}

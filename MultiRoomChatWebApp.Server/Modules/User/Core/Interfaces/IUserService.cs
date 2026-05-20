using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Interfaces;

public interface IUserService
{
    /// <summary>
    /// Tìm kiếm người dùng theo Username hoặc DisplayName (Case-Insensitive, StartsWith).
    /// </summary>
    /// <param name="keyword">Từ khóa tìm kiếm (tối thiểu 1 ký tự)</param>
    /// <param name="currentUserId">ID của người đang tìm kiếm (để loại trừ chính mình)</param>
    /// <returns>Danh sách tối đa 10 người dùng khớp với từ khóa</returns>
    Task<IEnumerable<UserSearchDto>> SearchByKeywordAsync(string keyword, Guid currentUserId);

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

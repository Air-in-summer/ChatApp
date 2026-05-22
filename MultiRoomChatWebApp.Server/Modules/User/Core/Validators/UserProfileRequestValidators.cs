using FluentValidation;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Validators;

public class UpdateUserProfileRequestValidator : AbstractValidator<UpdateUserProfileRequest>
{
    private const int MaxDisplayNameLength = 100;
    private const int MaxAvatarUrlLength = 2048;

    public UpdateUserProfileRequestValidator()
    {
        RuleFor(request => request.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Tên hiển thị không được để trống")
            .Must(displayName => !string.IsNullOrWhiteSpace(displayName)).WithMessage("Tên hiển thị không được để trống")
            .MaximumLength(MaxDisplayNameLength).WithMessage($"Tên hiển thị không được vượt quá {MaxDisplayNameLength} ký tự");

        RuleFor(request => request.AvatarUrl)
            .MaximumLength(MaxAvatarUrlLength).WithMessage($"Avatar URL không được vượt quá {MaxAvatarUrlLength} ký tự")
            .Must(BeEmptyOrAbsoluteHttpUrl).WithMessage("Avatar URL phải là đường dẫn http/https hợp lệ");
    }

    private static bool BeEmptyOrAbsoluteHttpUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return true;

        return Uri.TryCreate(avatarUrl.Trim(), UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https";
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 100;

    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.CurrentPassword)
            .NotEmpty().WithMessage("Mật khẩu hiện tại không được để trống");

        RuleFor(request => request.NewPassword)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mật khẩu mới không được để trống")
            .MinimumLength(MinPasswordLength).WithMessage($"Mật khẩu mới phải có ít nhất {MinPasswordLength} ký tự")
            .MaximumLength(MaxPasswordLength).WithMessage($"Mật khẩu mới không được vượt quá {MaxPasswordLength} ký tự")
            .Matches("[A-Z]").WithMessage("Mật khẩu mới phải có ít nhất 1 chữ hoa")
            .Matches("[a-z]").WithMessage("Mật khẩu mới phải có ít nhất 1 chữ thường")
            .Matches("[0-9]").WithMessage("Mật khẩu mới phải có ít nhất 1 chữ số")
            .Matches("[^a-zA-Z0-9]").WithMessage("Mật khẩu mới phải có ít nhất 1 ký tự đặc biệt");
    }
}

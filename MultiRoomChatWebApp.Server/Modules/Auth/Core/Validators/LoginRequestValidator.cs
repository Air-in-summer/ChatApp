using FluentValidation;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Validators;

/// <summary>
/// Validator cho LoginRequest - kiểm tra dữ liệu đầu vào khi đăng nhập.
/// </summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống")
            .EmailAddress().WithMessage("Email không đúng định dạng");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống");
    }
}

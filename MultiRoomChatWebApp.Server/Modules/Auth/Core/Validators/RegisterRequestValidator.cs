using FluentValidation;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Core.Validators;

/// <summary>
/// Validator cho RegisterRequest - kiểm tra dữ liệu đầu vào khi đăng ký.
/// </summary>
/// <remarks>
/// Luồng xử lý:
/// 1. FluentValidation tự động chặn request trước khi vào Controller.
/// 2. Nếu có lỗi => trả về 400 Bad Request với danh sách lỗi cụ thể.
/// 3. Regex trên Username đảm bảo không có Emoji, ký tự lạ.
/// </remarks>
public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        // Username: Chỉ cho phép chữ cái, số, dấu chấm, gạch dưới (KHÔNG cho Emoji)
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username không được để trống")
            .MinimumLength(3).WithMessage("Username phải có ít nhất 3 ký tự")
            .MaximumLength(30).WithMessage("Username không được quá 30 ký tự")
            .Matches(@"^[a-zA-Z0-9._]+$").WithMessage("Username chỉ được chứa chữ cái, số, dấu chấm hoặc dấu gạch dưới");

        // DisplayName: Chỉ kiểm tra không rỗng và độ dài (HỖ TRỢ tiếng Việt, KHÔNG Emoji)
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Tên hiển thị không được để trống")
            .MinimumLength(2).WithMessage("Tên hiển thị phải có ít nhất 2 ký tự")
            .MaximumLength(50).WithMessage("Tên hiển thị không được quá 50 ký tự")
            .Matches(@"^[^<>""'%;()&+\p{So}\p{Cs}]+$").WithMessage("Tên hiển thị chứa ký tự không hợp lệ (Emoji không được phép)");

        // Email: Kiểm tra format chuẩn
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống")
            .EmailAddress().WithMessage("Email không đúng định dạng");

        // Password: Tối thiểu 8 ký tự, phải có chữ hoa, chữ thường, số và ký tự đặc biệt
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống")
            .MinimumLength(8).WithMessage("Mật khẩu phải có ít nhất 8 ký tự")
            .MaximumLength(100).WithMessage("Mật khẩu không được quá 100 ký tự")
            .Matches(@"[A-Z]").WithMessage("Mật khẩu phải có ít nhất 1 chữ hoa")
            .Matches(@"[a-z]").WithMessage("Mật khẩu phải có ít nhất 1 chữ thường")
            .Matches(@"[0-9]").WithMessage("Mật khẩu phải có ít nhất 1 chữ số")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Mật khẩu phải có ít nhất 1 ký tự đặc biệt (!, @, #, ...)") ;
    }
}

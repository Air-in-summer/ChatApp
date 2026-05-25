using FluentValidation;
using MultiRoomChatWebApp.Server.Modules.User.Core.DTOs;

namespace MultiRoomChatWebApp.Server.Modules.User.Core.Validators;

public class CreateFriendRequestRequestValidator : AbstractValidator<CreateFriendRequestRequest>
{
    public CreateFriendRequestRequestValidator()
    {
        RuleFor(request => request.ReceiverId)
            .NotEmpty().WithMessage("Người nhận lời mời kết bạn không hợp lệ.");
    }
}

public class BlockUserRequestValidator : AbstractValidator<BlockUserRequest>
{
    public BlockUserRequestValidator()
    {
        RuleFor(request => request.BlockedUserId)
            .NotEmpty().WithMessage("Người dùng cần chặn không hợp lệ.");
    }
}

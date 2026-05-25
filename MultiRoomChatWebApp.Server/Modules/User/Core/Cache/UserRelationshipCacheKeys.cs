namespace MultiRoomChatWebApp.Server.Modules.User.Core.Cache;

/// <summary>
/// Tao Redis key dung chung cho cac policy relationship can duoc check nhanh ngoai User module.
/// </summary>
public static class UserRelationshipCacheKeys
{
    /// <summary>
    /// Key danh dau hai user dang co block hai chieu theo cap da normalize.
    /// </summary>
    public static string BlockBetween(Guid firstUserId, Guid secondUserId)
    {
        var (userAId, userBId) = NormalizePair(firstUserId, secondUserId);
        return $"relationship:block_between:{userAId}:{userBId}";
    }

    /// <summary>
    /// Key cache ngan han cho phep DM khi cap user khong co block tai thoi diem check.
    /// </summary>
    public static string AllowBetween(Guid firstUserId, Guid secondUserId)
    {
        var (userAId, userBId) = NormalizePair(firstUserId, secondUserId);
        return $"relationship:allow_between:{userAId}:{userBId}";
    }

    private static (Guid UserAId, Guid UserBId) NormalizePair(Guid firstUserId, Guid secondUserId)
    {
        return firstUserId.CompareTo(secondUserId) < 0
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);
    }
}

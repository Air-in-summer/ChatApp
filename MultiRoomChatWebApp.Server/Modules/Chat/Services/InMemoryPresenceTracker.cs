using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

/// <summary>
/// Triển khai In-Memory đếm số lượng kết nối Websocket của User.
/// Dùng lock (Thread-Safe) để ngăn chặn Race Condition.
/// </summary>
public class InMemoryPresenceTracker : IPresenceTracker
{
    // Dictionary lưu `UserId` map với chuỗi các `ConnectionId` đang liên kết
    private static readonly Dictionary<Guid, List<string>> OnlineUsers = new();

    public Task<bool> UserConnected(Guid userId, string connectionId)
    {
        bool isOnline = false;
        
        lock (OnlineUsers)
        {
            if (OnlineUsers.ContainsKey(userId))
            {
                OnlineUsers[userId].Add(connectionId);
            }
            else
            {
                OnlineUsers.Add(userId, new List<string> { connectionId });
                isOnline = true; // Người này lần đầu kết nối trong phiên => Trở thành chớp nhoáng "Online"
            }
        }

        return Task.FromResult(isOnline);
    }

    public Task<bool> UserDisconnected(Guid userId, string connectionId)
    {
        bool isOffline = false;
        
        lock (OnlineUsers)
        {
            if (!OnlineUsers.ContainsKey(userId)) return Task.FromResult(isOffline);

            OnlineUsers[userId].Remove(connectionId);

            if (OnlineUsers[userId].Count == 0)
            {
                OnlineUsers.Remove(userId);
                isOffline = true; // Kết nối cuối cùng đã rụng => Báo Offline toàn mạng
            }
        }

        return Task.FromResult(isOffline);
    }

    public Task<IEnumerable<Guid>> GetOnlineUsersAsync(IEnumerable<Guid> userIds)
    {
        IEnumerable<Guid> onlineSubset;
        
        lock (OnlineUsers)
        {
            onlineSubset = userIds.Where(id => OnlineUsers.ContainsKey(id)).ToList();
        }

        return Task.FromResult(onlineSubset);
    }
}

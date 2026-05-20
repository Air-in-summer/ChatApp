namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;

/// <summary>
/// Dịch vụ tạo LiveKit Access Token cho Voice Module.
/// Backend CHỈ làm duy nhất 1 việc: kiểm tra quyền + cấp Token.
/// Toàn bộ media routing do LiveKit Server xử lý.
/// </summary>
public interface IVoiceTokenService
{
    /// <summary>
    /// Tạo LiveKit Access Token (JWT) cho một participant tham gia phòng Voice.
    /// </summary>
    /// <param name="roomId">ID của phòng Voice (mapping 1:1 với Room.Id trong PostgreSQL)</param>
    /// <param name="userId">ID người dùng xin token</param>
    /// <param name="displayName">Tên hiển thị trong phòng Voice (để LiveKit SDK hiển thị)</param>
    /// <returns>JWT string để client dùng connect trực tiếp đến LiveKit Server</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Đọc ApiKey/ApiSecret từ config (appsettings.json)
    /// 2. Tạo AccessToken với Identity = userId, Name = displayName
    /// 3. Gắn VideoGrants: RoomJoin = true, Room = roomId, CanPublish = true, CanSubscribe = true
    /// 4. Set TTL = 1 giờ (client sẽ silent refresh khi gần hết hạn)
    /// 5. Trả về JWT string
    /// 
    /// Lưu ý:
    /// - Service KHÔNG check quyền member → Controller chịu trách nhiệm check trước khi gọi
    /// - Không phân quyền chi tiết (mọi người đều Publish + Subscribe như nhau)
    /// </remarks>
    string GenerateToken(Guid roomId, Guid userId, string displayName);

    /// <summary>
    /// Tạo LiveKit Access Token theo tên phòng LiveKit cụ thể.
    /// Dùng để voice room và DM call dùng chung logic ký JWT.
    /// </summary>
    /// <param name="liveKitRoomName">Tên phòng thật trên LiveKit, ví dụ roomId hoặc call:{sessionId}</param>
    /// <param name="userId">ID người dùng xin token</param>
    /// <param name="displayName">Tên hiển thị trong LiveKit participant</param>
    /// <returns>JWT string để client dùng connect trực tiếp đến LiveKit Server</returns>
    string GenerateTokenForLiveKitRoom(string liveKitRoomName, Guid userId, string displayName);

    /// <summary>
    /// Lấy TTL chuẩn của LiveKit token.
    /// Controller dùng giá trị này để trả metadata expiresAt/expiresIn cho client.
    /// </summary>
    /// <returns>Thời gian sống của LiveKit token</returns>
    TimeSpan GetTokenTtl();

    /// <summary>
    /// Lấy LiveKit Server Host URL từ config.
    /// Client cần URL này để mở WebSocket connection đến LiveKit.
    /// </summary>
    /// <returns>URL dạng "ws://localhost:7880" (dev) hoặc "wss://..." (production)</returns>
    string GetLiveKitHost();
}

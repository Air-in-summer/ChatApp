using Livekit.Server.Sdk.Dotnet;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Services;

/// <summary>
/// Implementation tạo LiveKit Access Token cho Voice Module.
/// Đọc ApiKey/ApiSecret từ IConfiguration, tạo JWT với VideoGrants phù hợp.
/// </summary>
/// <remarks>
/// Nguyên tắc:
/// - Service này KHÔNG check quyền → Controller phải check trước khi gọi
/// - Token TTL = 1 giờ → Client tự silent refresh khi gần hết hạn
/// - Mọi participant đều có quyền Publish + Subscribe (không phân quyền chi tiết)
/// - LiveKit Room Name = roomId.ToString() (Guid) → mapping 1:1 với Room.Id trong PostgreSQL
/// </remarks>
public class VoiceTokenService : IVoiceTokenService
{
    //private static readonly TimeSpan TokenTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(2);

    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _liveKitHost;
    private readonly ILogger<VoiceTokenService> _logger;

    /// <summary>
    /// Constructor: đọc cấu hình LiveKit từ appsettings.json section "LiveKit".
    /// </summary>
    /// <param name="configuration">IConfiguration chứa section LiveKit { ApiKey, ApiSecret, Host }</param>
    /// <param name="logger">Logger để ghi log khi tạo token</param>
    /// <exception cref="InvalidOperationException">Nếu thiếu cấu hình LiveKit trong appsettings</exception>
    public VoiceTokenService(IConfiguration configuration, ILogger<VoiceTokenService> logger)
    {
        _logger = logger;

        // Đọc config từ section "LiveKit" trong appsettings.json
        var liveKitSection = configuration.GetSection("LiveKit");

        _apiKey = liveKitSection["ApiKey"]
            ?? throw new InvalidOperationException("Thiếu cấu hình LiveKit:ApiKey trong appsettings.json");

        _apiSecret = liveKitSection["ApiSecret"]
            ?? throw new InvalidOperationException("Thiếu cấu hình LiveKit:ApiSecret trong appsettings.json");

        _liveKitHost = liveKitSection["Host"]
            ?? throw new InvalidOperationException("Thiếu cấu hình LiveKit:Host trong appsettings.json");
    }

    /// <summary>
    /// Tạo LiveKit Access Token (JWT) cho một participant tham gia phòng Voice.
    /// </summary>
    /// <param name="roomId">ID của phòng Voice (dùng làm Room Name trong LiveKit)</param>
    /// <param name="userId">ID người dùng - dùng làm Identity trong token</param>
    /// <param name="displayName">Tên hiển thị của participant trong phòng Voice</param>
    /// <returns>JWT string để client connect trực tiếp đến LiveKit Server</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tạo AccessToken với ApiKey/ApiSecret
    /// 2. Set Identity = userId (unique, dùng để nhận diện participant)
    /// 3. Set Name = displayName (hiển thị trong UI)
    /// 4. Gắn VideoGrants: cho phép Join phòng, Publish (mic/cam/share), Subscribe (nhận stream)
    /// 5. Set TTL = 1 giờ
    /// 6. Generate JWT và trả về
    /// 
    /// Lưu ý:
    /// - Identity dùng userId.ToString() để LiveKit quản lý participant duy nhất
    /// - Nếu cùng 1 userId connect lại, LiveKit sẽ tự thay thế session cũ (tránh duplicate)
    /// </remarks>
    public string GenerateToken(Guid roomId, Guid userId, string displayName)
    {
        return GenerateTokenForLiveKitRoom(roomId.ToString(), userId, displayName);
    }

    /// <summary>
    /// Tạo LiveKit Access Token theo tên phòng LiveKit cụ thể.
    /// </summary>
    /// <param name="liveKitRoomName">Tên phòng thật trên LiveKit, ví dụ roomId hoặc call:{sessionId}</param>
    /// <param name="userId">ID người dùng - dùng làm Identity trong token</param>
    /// <param name="displayName">Tên hiển thị của participant trong phòng LiveKit</param>
    /// <returns>JWT string để client connect trực tiếp đến LiveKit Server</returns>
    /// <remarks>
    /// Luồng xử lý:
    /// 1. Tạo AccessToken với ApiKey/ApiSecret.
    /// 2. Set Identity = userId và Name = displayName.
    /// 3. Gắn VideoGrants vào đúng `liveKitRoomName`.
    /// 4. Set TTL chuẩn của Voice module.
    /// 5. Generate JWT và trả về.
    /// </remarks>
    public string GenerateTokenForLiveKitRoom(string liveKitRoomName, Guid userId, string displayName)
    {
        // Tạo token với ApiKey/ApiSecret khớp với livekit.yaml
        var token = new AccessToken(_apiKey, _apiSecret)
            // Identity phải unique cho mỗi participant trong phòng
            .WithIdentity(userId.ToString())
            // Tên hiển thị cho UI (LiveKit SDK expose qua Participant.name)
            .WithName(displayName)
            // Quyền hạn: cho phép Join + Publish + Subscribe (không phân quyền chi tiết)
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = liveKitRoomName,
                CanPublish = true,        // Được phép bật Mic/Cam/Share Screen
                CanSubscribe = true       // Được phép nhận stream từ người khác
            })
            // Token hết hạn sau 1 giờ, client sẽ gọi lại API lấy token mới trước khi hết hạn
            .WithTtl(TokenTtl);

        var jwt = token.ToJwt();

        _logger.LogInformation(
            "Đã tạo LiveKit token cho User {UserId} vào LiveKit Room {LiveKitRoomName}, TTL={TokenTtlMinutes} phút",
            userId, liveKitRoomName, TokenTtl.TotalMinutes);

        return jwt;
    }

    /// <summary>
    /// Lấy TTL chuẩn của LiveKit token.
    /// </summary>
    /// <returns>Thời gian sống của LiveKit token</returns>
    public TimeSpan GetTokenTtl()
    {
        return TokenTtl;
    }

    /// <summary>
    /// Lấy LiveKit Server Host URL từ config.
    /// </summary>
    /// <returns>URL dạng "ws://localhost:7880" (dev) hoặc "wss://..." (production)</returns>
    public string GetLiveKitHost()
    {
        return _liveKitHost;
    }
}

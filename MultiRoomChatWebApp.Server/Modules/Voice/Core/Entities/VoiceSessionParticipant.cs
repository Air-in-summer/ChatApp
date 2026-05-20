using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Entities;

/// <summary>
/// Embedded subdocument trong VoiceSession, lưu trạng thái từng user trong call.
/// Không phải collection riêng.
/// </summary>
public class VoiceSessionParticipant
{
    [BsonElement("user_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public VoiceParticipantStatus Status { get; set; } = VoiceParticipantStatus.Invited;

    [BsonElement("invited_at")]
    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("joined_at")]
    public DateTime? JoinedAt { get; set; }

    [BsonElement("left_at")]
    public DateTime? LeftAt { get; set; }
}

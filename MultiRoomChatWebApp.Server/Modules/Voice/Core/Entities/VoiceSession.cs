using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Core.Entities;

/// <summary>
/// Mongo document lưu lifecycle của một phiên media trong Voice module.
/// Phase đầu chỉ persist DirectCall; voice room channel không ghi document khi join/leave.
/// </summary>
public class VoiceSession
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [BsonElement("kind")]
    [BsonRepresentation(BsonType.String)]
    public VoiceSessionKind Kind { get; set; } = VoiceSessionKind.DirectCall;

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public VoiceSessionStatus Status { get; set; } = VoiceSessionStatus.Ringing;

    [BsonElement("source_room_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid SourceRoomId { get; set; }

    [BsonElement("created_by_user_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid CreatedByUserId { get; set; }

    [BsonElement("livekit_room_name")]
    public string LiveKitRoomName { get; set; } = string.Empty;

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("started_at")]
    public DateTime? StartedAt { get; set; }

    [BsonElement("ended_at")]
    public DateTime? EndedAt { get; set; }

    [BsonElement("participants")]
    public List<VoiceSessionParticipant> Participants { get; set; } = new();
}

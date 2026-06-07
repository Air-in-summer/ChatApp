using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

/// <summary>
/// Reaction duoc nhung truc tiep trong document tin nhan.
/// </summary>
public sealed class MessageReaction
{
    [BsonElement("emoji")]
    public string Emoji { get; set; } = string.Empty;

    [BsonElement("user_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }
}

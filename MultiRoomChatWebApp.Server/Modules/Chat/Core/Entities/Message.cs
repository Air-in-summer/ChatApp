using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

public class Message
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("room_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid RoomId { get; set; }

    [BsonElement("sender_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid SenderId { get; set; }

    [BsonElement("client_message_id")]
    [BsonRepresentation(BsonType.String)]
    public Guid? ClientMessageId { get; set; }

    [BsonElement("accepted_at")]
    public DateTime? AcceptedAt { get; set; }

    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public MessageType Type { get; set; } = MessageType.Text;

    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;

    [BsonElement("attachments")]
    public List<Attachment>? Attachments { get; set; }

    [BsonElement("meta")]
    public MessageMeta? Meta { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public MessageStatus Status { get; set; } = MessageStatus.Sent;

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("edited_at")]
    public DateTime? EditedAt { get; set; }

    [BsonElement("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [BsonElement("deleted_by")]
    [BsonRepresentation(BsonType.String)]
    public Guid? DeletedBy { get; set; }

    [BsonElement("reactions")]
    public List<MessageReaction> Reactions { get; set; } = [];

    [BsonElement("pinned_at")]
    public DateTime? PinnedAt { get; set; }

    [BsonElement("pinned_by")]
    [BsonRepresentation(BsonType.String)]
    public Guid? PinnedBy { get; set; }
}

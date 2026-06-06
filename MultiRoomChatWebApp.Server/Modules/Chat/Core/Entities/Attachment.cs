using MongoDB.Bson.Serialization.Attributes;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

public class Attachment
{
    [BsonElement("media_id")]
    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    public Guid? MediaId { get; set; }

    [BsonElement("kind")]
    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    public MediaKind Kind { get; set; } = MediaKind.File;

    [BsonElement("url")]
    public string Url { get; set; } = string.Empty;

    [BsonElement("filename")]
    public string Filename { get; set; } = string.Empty;

    [BsonElement("size")]
    public long Size { get; set; }

    [BsonElement("mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [BsonElement("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [BsonElement("expires_at")]
    public DateTime? ExpiresAt { get; set; }
}

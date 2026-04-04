using MongoDB.Bson.Serialization.Attributes;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

public class Attachment
{
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
}

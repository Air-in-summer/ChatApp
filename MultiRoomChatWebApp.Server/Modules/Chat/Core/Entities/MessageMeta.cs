using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;

public class MessageMeta
{
    [BsonElement("edited")]
    public bool Edited { get; set; } = false;

    [BsonElement("edit_count")]
    public int EditCount { get; set; } = 0;

    [BsonElement("reply_to")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? ReplyTo { get; set; } // Reference to Parent Message _id

    [BsonElement("mentions")]
    [BsonRepresentation(BsonType.String)]
    public List<Guid>? Mentions { get; set; } = new List<Guid>();
}

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Exceptions;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public static class ChatBrokerEntryFailureClassifier
{
    public static ChatBrokerEntryFailureKind Classify(Exception exception)
    {
        var candidate = exception is AggregateException aggregateException
            ? aggregateException.GetBaseException()
            : exception;

        return candidate switch
        {
            ChatBrokerEntryProcessingException processingException
                => processingException.FailureKind,
            RedisException
                => ChatBrokerEntryFailureKind.Transient,
            MongoException
                => ChatBrokerEntryFailureKind.Transient,
            DbUpdateException
                => ChatBrokerEntryFailureKind.Transient,
            DbException
                => ChatBrokerEntryFailureKind.Transient,
            TimeoutException
                => ChatBrokerEntryFailureKind.Transient,
            IOException
                => ChatBrokerEntryFailureKind.Transient,
            HttpRequestException
                => ChatBrokerEntryFailureKind.Transient,
            _ => ChatBrokerEntryFailureKind.Transient
        };
    }
}

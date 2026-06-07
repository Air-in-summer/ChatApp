using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Exceptions;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Logging;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Options;
using MultiRoomChatWebApp.Server.Modules.Chat.Hubs;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

public sealed class MessagePersistenceWorkerV2 : BackgroundService
{
    private const int WorkerId = 0;
    private static readonly RedisValue ReadNewEntries = ">";

    private readonly IChatBrokerConnection _brokerConnection;
    private readonly IChatBrokerKeyProvider _keyProvider;
    private readonly IChatBrokerConsumerIdentityProvider _consumerIdentityProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ChatBrokerOptions _brokerOptions;
    private readonly ILogger<MessagePersistenceWorkerV2> _logger;

    public MessagePersistenceWorkerV2(
        IChatBrokerConnection brokerConnection,
        IChatBrokerKeyProvider keyProvider,
        IChatBrokerConsumerIdentityProvider consumerIdentityProvider,
        IServiceScopeFactory scopeFactory,
        IHubContext<ChatHub, IChatClient> hubContext,
        IOptions<ChatBrokerOptions> brokerOptions,
        ILogger<MessagePersistenceWorkerV2> logger)
    {
        _brokerConnection = brokerConnection;
        _keyProvider = keyProvider;
        _consumerIdentityProvider = consumerIdentityProvider;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _brokerOptions = brokerOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var database = _brokerConnection.GetDatabase();
        var consumerName = _consumerIdentityProvider.GetConsumerName(
            ChatBrokerConsumerKind.Persistence,
            WorkerId);
        var pendingEntryReclaimer = new ChatBrokerPendingEntryReclaimer(
            _keyProvider.MessageStream,
            _keyProvider.PersistenceGroup,
            consumerName,
            _brokerOptions.VisibilityTimeout,
            _brokerOptions.ReadBatchSize);
        var retryTracker = new ChatBrokerRetryTracker(
            database,
            _keyProvider,
            ChatBrokerConsumerKind.Persistence,
            _brokerOptions);
        var deadLetterPublisher = new ChatBrokerDeadLetterPublisher(
            database,
            _keyProvider,
            _brokerOptions);

        _logger.LogInformation(
            "MessagePersistenceWorkerV2 da khoi dong. Stream={Stream}; Group={Group}; Consumer={Consumer}; BatchSize={BatchSize}",
            _keyProvider.MessageStream,
            _keyProvider.PersistenceGroup,
            consumerName,
            _brokerOptions.ReadBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReclaimPendingEntriesAsync(
                    database,
                    pendingEntryReclaimer,
                    retryTracker,
                    deadLetterPublisher,
                    consumerName,
                    stoppingToken);

                var entries = await database.StreamReadGroupAsync(
                    _keyProvider.MessageStream,
                    _keyProvider.PersistenceGroup,
                    consumerName,
                    ReadNewEntries,
                    count: _brokerOptions.ReadBatchSize);

                if (entries.Length == 0)
                {
                    await Task.Delay(50, stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                {
                    await ProcessEntryIsolatedAsync(
                        database,
                        retryTracker,
                        entry,
                        consumerName,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "MessagePersistenceWorkerV2 gap loi khi doc broker. Thu lai sau 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ReclaimPendingEntriesAsync(
        IDatabase database,
        ChatBrokerPendingEntryReclaimer reclaimer,
        ChatBrokerRetryTracker retryTracker,
        ChatBrokerDeadLetterPublisher deadLetterPublisher,
        RedisValue consumerName,
        CancellationToken stoppingToken)
    {
        var claimResult = await reclaimer.ClaimDueAsync(database, stoppingToken);
        if (!claimResult.HasValue)
        {
            return;
        }

        var result = claimResult.Value;
        if (result.DeletedIds.Length > 0)
        {
            _logger.LogWarning(
                "MessagePersistenceWorkerV2 XAUTOCLAIM da loai {DeletedCount} pending reference khong con stream entry. Consumer={Consumer}",
                result.DeletedIds.Length,
                consumerName);
        }

        if (result.ClaimedEntries.Length == 0)
        {
            return;
        }

        _logger.LogInformation(
            "MessagePersistenceWorkerV2 da reclaim {ClaimedCount} pending entries. Consumer={Consumer}; VisibilityTimeoutMs={VisibilityTimeoutMs}; NextCursor={NextCursor}",
            result.ClaimedEntries.Length,
            consumerName,
            (long)Math.Ceiling(_brokerOptions.VisibilityTimeout.TotalMilliseconds),
            result.NextStartId);

        foreach (var entry in result.ClaimedEntries)
        {
            var retryDecision = await retryTracker.GetDecisionAsync(
                entry.Id,
                stoppingToken);
            if (retryDecision.IsExhausted)
            {
                await DeadLetterExhaustedEntryIsolatedAsync(
                    deadLetterPublisher,
                    retryDecision,
                    entry,
                    consumerName,
                    stoppingToken);
                continue;
            }

            if (!retryDecision.IsDue)
            {
                var payload = entry.Values
                    .FirstOrDefault(value => value.Name == "payload")
                    .Value
                    .ToString();
                using var logScope = ChatMessageLogScope.BeginForEntry(
                    _logger,
                    entry.Id.ToString(),
                    _keyProvider.PersistenceGroup.ToString(),
                    consumerName.ToString(),
                    retryDecision.Attempt,
                    payload);
                _logger.LogDebug(
                    "MessagePersistenceWorkerV2 bo qua entry chua den han retry. EntryId={EntryId}; Attempt={Attempt}; NextRetryAtUtc={NextRetryAtUtc}",
                    entry.Id,
                    retryDecision.Attempt,
                    retryDecision.NextRetryAtUtc);
                continue;
            }

            await ProcessEntryIsolatedAsync(
                database,
                retryTracker,
                entry,
                consumerName,
                stoppingToken);
        }
    }

    private async Task DeadLetterExhaustedEntryIsolatedAsync(
        ChatBrokerDeadLetterPublisher deadLetterPublisher,
        ChatBrokerRetryDecision retryDecision,
        StreamEntry entry,
        RedisValue consumerName,
        CancellationToken stoppingToken)
    {
        var payload = entry.Values
            .FirstOrDefault(value => value.Name == "payload")
            .Value
            .ToString();
        using var logScope = ChatMessageLogScope.BeginForEntry(
            _logger,
            entry.Id.ToString(),
            _keyProvider.PersistenceGroup.ToString(),
            consumerName.ToString(),
            retryDecision.Attempt,
            payload);

        try
        {
            var originalPayload = payload;
            var originalFields = JsonSerializer.Serialize(
                entry.Values.Select(value => new
                {
                    Name = value.Name.ToString(),
                    Value = value.Value.ToString()
                }));

            if (retryDecision.FailureKind == ChatBrokerEntryFailureKind.Permanent &&
                TryReadCompensationEvent(originalPayload, out var acceptedEvent))
            {
                await CompensatePermanentFailureAsync(
                    acceptedEvent,
                    retryDecision.ErrorCode ?? "message_persistence_failed",
                    stoppingToken);
            }
            else if (retryDecision.FailureKind == ChatBrokerEntryFailureKind.Permanent)
            {
                _logger.LogWarning(
                    "MessagePersistenceWorkerV2 khong the compensation vi payload khong du dinh danh; entry van duoc luu vao DLQ. EntryId={EntryId}; Code={Code}",
                    entry.Id,
                    retryDecision.ErrorCode);
            }

            var result = await deadLetterPublisher.PublishPersistenceAsync(
                entry.Id,
                consumerName,
                originalPayload,
                originalFields,
                stoppingToken);

            if (!result.IsDeadLettered)
            {
                _logger.LogWarning(
                    "MessagePersistenceWorkerV2 khong dua entry vao DLQ vi attempt chua dat nguong tai thoi diem atomic check. EntryId={EntryId}; Attempt={Attempt}; MaxAttempts={MaxAttempts}",
                    entry.Id,
                    result.Attempt,
                    _brokerOptions.MaxAttempts);
                return;
            }

            if (result.AcknowledgedCount == 0)
            {
                _logger.LogWarning(
                    "MessagePersistenceWorkerV2 da ghi persistence DLQ nhung chua ACK duoc entry goc. EntryId={EntryId}; DlqEntryId={DlqEntryId}; Attempt={Attempt}",
                    entry.Id,
                    result.DeadLetterEntryId,
                    result.Attempt);
                return;
            }

            _logger.LogError(
                "MessagePersistenceWorkerV2 da dua entry vao persistence DLQ va ACK entry goc. EntryId={EntryId}; DlqEntryId={DlqEntryId}; Attempt={Attempt}; Consumer={Consumer}",
                entry.Id,
                result.DeadLetterEntryId,
                result.Attempt,
                consumerName);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "MessagePersistenceWorkerV2 khong ghi duoc persistence DLQ; entry van pending. EntryId={EntryId}; Consumer={Consumer}",
                entry.Id,
                consumerName);
        }
    }

    private async Task CompensatePermanentFailureAsync(
        MessageAcceptedEventV1 acceptedEvent,
        string errorCode,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var mongoDatabase = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        var messagesCollection = mongoDatabase.GetCollection<Message>("messages");
        var deleteFilter = Builders<Message>.Filter.And(
            Builders<Message>.Filter.Eq(message => message.Id, acceptedEvent.MessageId),
            Builders<Message>.Filter.Eq(message => message.RoomId, acceptedEvent.RoomId),
            Builders<Message>.Filter.Eq(message => message.SenderId, acceptedEvent.SenderId),
            Builders<Message>.Filter.Eq(
                message => message.ClientMessageId,
                acceptedEvent.ClientMessageId));

        var deleteResult = await messagesCollection.DeleteOneAsync(
            deleteFilter,
            stoppingToken);

        var mediaReservationService = scope.ServiceProvider
            .GetRequiredService<IChatMediaReservationService>();
        var mediaIds = (acceptedEvent.MediaIds ?? [])
            .Where(mediaId => mediaId != Guid.Empty)
            .Distinct()
            .ToList();
        await mediaReservationService.ReleaseAsync(
            mediaIds,
            acceptedEvent.MessageId,
            stoppingToken);

        var occurredAtUtc = DateTime.UtcNow;
        var failedPayload = new MessagePersistenceFailedDto(
            acceptedEvent.RoomId,
            acceptedEvent.ClientMessageId,
            acceptedEvent.MessageId,
            errorCode,
            occurredAtUtc);
        var retractedPayload = new MessageRetractedDto(
            acceptedEvent.RoomId,
            acceptedEvent.ClientMessageId,
            acceptedEvent.MessageId,
            errorCode,
            occurredAtUtc);

        await _hubContext.Clients.User(acceptedEvent.SenderId.ToString())
            .MessagePersistenceFailed(failedPayload);

        var recipientResolver = scope.ServiceProvider
            .GetRequiredService<IMessageDeliveryRecipientResolver>();
        var recipientResult = await recipientResolver.ResolveAsync(
            acceptedEvent.RoomId,
            acceptedEvent.SenderId,
            stoppingToken);
        var recipientIds = recipientResult.IsSuccess &&
                           recipientResult.RecipientIds.Count > 0
            ? recipientResult.RecipientIds
            : [acceptedEvent.SenderId];

        await _hubContext.Clients.Users(
                recipientIds.Select(userId => userId.ToString()))
            .MessageRetracted(retractedPayload);

        _logger.LogWarning(
            "MessagePersistenceWorkerV2 da compensation loi vinh vien. MessageId={MessageId}; DeletedMongoCount={DeletedMongoCount}; ReservationMediaCount={ReservationMediaCount}; RetractedRecipientCount={RetractedRecipientCount}; Code={Code}",
            acceptedEvent.MessageId,
            deleteResult.DeletedCount,
            mediaIds.Count,
            recipientIds.Count,
            errorCode);
    }

    private static bool TryReadCompensationEvent(
        string payload,
        out MessageAcceptedEventV1 acceptedEvent)
    {
        acceptedEvent = null!;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            var candidate = JsonSerializer.Deserialize<MessageAcceptedEventV1>(payload);
            if (candidate is null ||
                candidate.RoomId == Guid.Empty ||
                candidate.SenderId == Guid.Empty ||
                candidate.ClientMessageId == Guid.Empty ||
                !ObjectId.TryParse(candidate.MessageId, out _))
            {
                return false;
            }

            acceptedEvent = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task ProcessEntryIsolatedAsync(
        IDatabase database,
        ChatBrokerRetryTracker retryTracker,
        StreamEntry entry,
        RedisValue consumerName,
        CancellationToken stoppingToken)
    {
        var retryDecision = await retryTracker.GetDecisionAsync(
            entry.Id,
            stoppingToken);
        var payload = entry.Values
            .FirstOrDefault(value => value.Name == "payload")
            .Value
            .ToString();
        using var logScope = ChatMessageLogScope.BeginForEntry(
            _logger,
            entry.Id.ToString(),
            _keyProvider.PersistenceGroup.ToString(),
            consumerName.ToString(),
            retryDecision.Attempt,
            payload);

        try
        {
            await ProcessEntryAsync(database, entry, stoppingToken);
            await ClearRetryStateBestEffortAsync(
                retryTracker,
                entry.Id,
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ChatBrokerEntryProcessingException ex)
        {
            await RecordEntryFailureBestEffortAsync(
                retryTracker,
                entry.Id,
                ex.FailureKind,
                ex.Code,
                ex,
                stoppingToken);
        }
        catch (Exception ex)
        {
            await RecordEntryFailureBestEffortAsync(
                retryTracker,
                entry.Id,
                ChatBrokerEntryFailureClassifier.Classify(ex),
                "persistence_unhandled_exception",
                ex,
                stoppingToken);
        }
    }

    private async Task RecordEntryFailureBestEffortAsync(
        ChatBrokerRetryTracker retryTracker,
        RedisValue entryId,
        ChatBrokerEntryFailureKind failureKind,
        string code,
        Exception exception,
        CancellationToken stoppingToken)
    {
        try
        {
            var retryState = await retryTracker.RecordFailureAsync(
                entryId,
                failureKind,
                code,
                exception.Message,
                stoppingToken);

            _logger.Log(
                failureKind == ChatBrokerEntryFailureKind.Permanent
                    ? LogLevel.Warning
                    : LogLevel.Error,
                exception,
                "MessagePersistenceWorkerV2 xu ly entry that bai. EntryId={EntryId}; FailureKind={FailureKind}; Code={Code}; Attempt={Attempt}; MaxAttempts={MaxAttempts}; NextRetryAtUtc={NextRetryAtUtc}",
                entryId,
                failureKind,
                code,
                retryState.Attempt,
                _brokerOptions.MaxAttempts,
                retryState.NextRetryAtUtc);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception trackingException)
        {
            _logger.LogError(
                trackingException,
                "MessagePersistenceWorkerV2 khong ghi duoc retry state. EntryId={EntryId}; FailureKind={FailureKind}; Code={Code}; OriginalReason={OriginalReason}",
                entryId,
                failureKind,
                code,
                exception.Message);
        }
    }

    private async Task ClearRetryStateBestEffortAsync(
        ChatBrokerRetryTracker retryTracker,
        RedisValue entryId,
        CancellationToken stoppingToken)
    {
        try
        {
            await retryTracker.ClearAsync(entryId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MessagePersistenceWorkerV2 khong xoa duoc retry state sau khi xu ly thanh cong. EntryId={EntryId}",
                entryId);
        }
    }

    private async Task ProcessEntryAsync(
        IDatabase database,
        StreamEntry entry,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        using var entryCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var entryToken = entryCancellation.Token;

        await Task.Yield();
        entryToken.ThrowIfCancellationRequested();

        var payload = entry.Values.FirstOrDefault(value => value.Name == "payload").Value;
        var parseResult = TryParseAndValidateEvent(entry.Id, payload);
        if (!parseResult.IsValid || parseResult.AcceptedEvent is null)
        {
            throw ChatBrokerEntryProcessingException.Permanent(
                parseResult.Reason ?? "payload_invalid",
                "Persistence event khong hop le.");
        }

        _logger.LogInformation(
            "MessagePersistenceWorkerV2 da parse event hop le. EntryId={EntryId}; MessageId={MessageId}; RoomId={RoomId}; AttachmentCount={AttachmentCount}; Scope={ScopeId}",
            entry.Id,
            parseResult.AcceptedEvent.MessageId,
            parseResult.AcceptedEvent.RoomId,
            parseResult.AcceptedEvent.Attachments.Count,
            scope.ServiceProvider.GetHashCode());

        var upsertResult = await PersistMessageIfAbsentAsync(
            scope.ServiceProvider,
            parseResult.AcceptedEvent,
            entryToken);

        _logger.LogInformation(
            "MessagePersistenceWorkerV2 upsert message hoan tat. EntryId={EntryId}; MessageId={MessageId}; Inserted={Inserted}; Matched={Matched}",
            entry.Id,
            parseResult.AcceptedEvent.MessageId,
            upsertResult.Inserted,
            upsertResult.MatchedExisting);

        var mediaReservationService = scope.ServiceProvider
            .GetRequiredService<IChatMediaReservationService>();
        var completionResult = await mediaReservationService.CompleteWithResultAsync(
            parseResult.AcceptedEvent.MediaIds,
            parseResult.AcceptedEvent.SenderId,
            parseResult.AcceptedEvent.RoomId,
            parseResult.AcceptedEvent.MessageId,
            parseResult.AcceptedEvent.AcceptedAtUtc,
            entryToken);

        if (!completionResult.IsSuccess)
        {
            throw ChatBrokerEntryProcessingException.Permanent(
                completionResult.ConflictCode ?? "media_completion_conflict",
                completionResult.ConflictReason ??
                "Khong the complete media reservation cua message.");
        }

        _logger.LogInformation(
            "MessagePersistenceWorkerV2 complete media thanh cong. EntryId={EntryId}; MessageId={MessageId}; IsNoOp={IsNoOp}",
            entry.Id,
            parseResult.AcceptedEvent.MessageId,
            completionResult.IsNoOp);

        var persistedAtUtc = DateTime.UtcNow;
        await _hubContext.Clients.User(parseResult.AcceptedEvent.SenderId.ToString())
            .MessagePersisted(
                new MessagePersistedDto(
                    parseResult.AcceptedEvent.RoomId,
                    parseResult.AcceptedEvent.ClientMessageId,
                    parseResult.AcceptedEvent.MessageId,
                    persistedAtUtc,
                    "Sent"));

        _logger.LogInformation(
            "MessagePersistenceWorkerV2 da phat MessagePersisted cho nguoi gui. EntryId={EntryId}; MessageId={MessageId}; SenderId={SenderId}",
            entry.Id,
            parseResult.AcceptedEvent.MessageId,
            parseResult.AcceptedEvent.SenderId);

        var acknowledgedCount = await database.StreamAcknowledgeAsync(
            _keyProvider.MessageStream,
            _keyProvider.PersistenceGroup,
            entry.Id);

        if (acknowledgedCount == 0)
        {
            _logger.LogWarning(
                "MessagePersistenceWorkerV2 khong tim thay pending entry de ACK. EntryId={EntryId}; MessageId={MessageId}",
                entry.Id,
                parseResult.AcceptedEvent.MessageId);
            return;
        }

        _logger.LogInformation(
            "MessagePersistenceWorkerV2 da ACK persistence entry. EntryId={EntryId}; MessageId={MessageId}",
            entry.Id,
            parseResult.AcceptedEvent.MessageId);

        // Khong XDEL: delivery group va retention maintenance van can stream entry.
    }

    private static async Task<MessagePersistenceUpsertResult> PersistMessageIfAbsentAsync(
        IServiceProvider serviceProvider,
        MessageAcceptedEventV1 acceptedEvent,
        CancellationToken cancellationToken)
    {
        var mongoDatabase = serviceProvider.GetRequiredService<IMongoDatabase>();
        var messagesCollection = mongoDatabase.GetCollection<Message>("messages");
        var content = acceptedEvent.Content?.Trim() ?? string.Empty;
        var storedAttachments = BuildStoredAttachments(acceptedEvent.Attachments);
        var messageType = DetermineMessageType(content, storedAttachments);

        var filter = Builders<Message>.Filter.Eq(message => message.Id, acceptedEvent.MessageId);
        var update = Builders<Message>.Update
            .SetOnInsert(message => message.Id, acceptedEvent.MessageId)
            .SetOnInsert(message => message.RoomId, acceptedEvent.RoomId)
            .SetOnInsert(message => message.SenderId, acceptedEvent.SenderId)
            .SetOnInsert(message => message.ClientMessageId, acceptedEvent.ClientMessageId)
            .SetOnInsert(message => message.AcceptedAt, acceptedEvent.AcceptedAtUtc)
            .SetOnInsert(message => message.Type, messageType)
            .SetOnInsert(message => message.Content, content)
            .SetOnInsert(message => message.Attachments, storedAttachments.Count == 0 ? null : storedAttachments)
            .SetOnInsert(message => message.Status, MessageStatus.Sent)
            .SetOnInsert(message => message.CreatedAt, acceptedEvent.AcceptedAtUtc);

        var result = await messagesCollection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);

        return new MessagePersistenceUpsertResult(
            Inserted: result.UpsertedId != null,
            MatchedExisting: result.MatchedCount > 0);
    }

    private static List<Attachment> BuildStoredAttachments(
        IReadOnlyList<MessageAcceptedAttachmentV1> attachments)
    {
        if (attachments.Count == 0)
        {
            return [];
        }

        return attachments
            .Select(attachment => new Attachment
            {
                MediaId = attachment.MediaId,
                Kind = attachment.Kind,
                Filename = attachment.Filename,
                Size = attachment.Size,
                MimeType = attachment.MimeType,
                Url = string.Empty,
                ExpiresAt = null
            })
            .ToList();
    }

    private static MessageType DetermineMessageType(
        string content,
        IReadOnlyList<Attachment> attachments)
    {
        if (!string.IsNullOrWhiteSpace(content) || attachments.Count == 0)
        {
            return MessageType.Text;
        }

        if (attachments.Count > 1)
        {
            return MessageType.File;
        }

        return attachments[0].Kind switch
        {
            MediaKind.Image => MessageType.Image,
            MediaKind.Audio => MessageType.Audio,
            MediaKind.Video => MessageType.Video,
            _ => MessageType.File
        };
    }

    private static ParsedMessageEvent TryParseAndValidateEvent(
        RedisValue entryId,
        RedisValue payload)
    {
        if (payload.IsNullOrEmpty)
        {
            return ParsedMessageEvent.Invalid("payload_missing");
        }

        var payloadText = payload.ToString();
        var payloadBytes = Encoding.UTF8.GetByteCount(payloadText);
        if (payloadBytes > MessageAcceptedEventV1.MaxPayloadBytes)
        {
            return ParsedMessageEvent.Invalid("payload_too_large");
        }

        MessageAcceptedEventV1? acceptedEvent;
        try
        {
            acceptedEvent = JsonSerializer.Deserialize<MessageAcceptedEventV1>(payloadText);
        }
        catch (JsonException)
        {
            return ParsedMessageEvent.Invalid("payload_json_invalid");
        }

        if (acceptedEvent is null)
        {
            return ParsedMessageEvent.Invalid("payload_json_empty");
        }

        var validationError = ValidateAcceptedEvent(acceptedEvent);
        return validationError is null
            ? ParsedMessageEvent.Valid(acceptedEvent)
            : ParsedMessageEvent.Invalid(validationError);
    }

    private static string? ValidateAcceptedEvent(MessageAcceptedEventV1 acceptedEvent)
    {
        if (acceptedEvent.SchemaVersion != MessageAcceptedEventV1.CurrentSchemaVersion)
        {
            return "schema_version_unsupported";
        }

        if (acceptedEvent.CorrelationId == Guid.Empty)
        {
            return "correlation_id_empty";
        }

        if (acceptedEvent.ClientMessageId == Guid.Empty)
        {
            return "client_message_id_empty";
        }

        if (!ObjectId.TryParse(acceptedEvent.MessageId, out _))
        {
            return "message_id_invalid";
        }

        if (acceptedEvent.AcceptedAtUtc == default)
        {
            return "accepted_at_empty";
        }

        if (acceptedEvent.RoomId == Guid.Empty)
        {
            return "room_id_empty";
        }

        if (acceptedEvent.SenderId == Guid.Empty)
        {
            return "sender_id_empty";
        }

        var content = acceptedEvent.Content?.Trim() ?? string.Empty;
        if (content.Length > MessageAcceptedEventV1.MaxContentLength)
        {
            return "content_too_long";
        }

        var mediaIds = acceptedEvent.MediaIds ?? [];
        var attachments = acceptedEvent.Attachments ?? [];
        if (string.IsNullOrWhiteSpace(content) && mediaIds.Count == 0)
        {
            return "message_empty";
        }

        if (mediaIds.Count > MessageAcceptedEventV1.MaxAttachmentCount)
        {
            return "attachment_count_too_large";
        }

        if (mediaIds.Any(mediaId => mediaId == Guid.Empty) ||
            mediaIds.Distinct().Count() != mediaIds.Count)
        {
            return "media_ids_invalid";
        }

        return ValidateAttachmentSnapshots(mediaIds, attachments);
    }

    private static string? ValidateAttachmentSnapshots(
        IReadOnlyList<Guid> mediaIds,
        IReadOnlyList<MessageAcceptedAttachmentV1> attachments)
    {
        if (mediaIds.Count != attachments.Count)
        {
            return "attachment_snapshot_count_mismatch";
        }

        for (var index = 0; index < mediaIds.Count; index++)
        {
            var attachment = attachments[index];
            if (attachment.MediaId != mediaIds[index] ||
                attachment.MediaId == Guid.Empty)
            {
                return "attachment_media_id_mismatch";
            }

            if (!Enum.IsDefined(attachment.Kind) ||
                !Enum.IsDefined(attachment.AccessLevel))
            {
                return "attachment_enum_invalid";
            }

            if (string.IsNullOrWhiteSpace(attachment.BucketName) ||
                string.IsNullOrWhiteSpace(attachment.StorageKey) ||
                string.IsNullOrWhiteSpace(attachment.Filename) ||
                string.IsNullOrWhiteSpace(attachment.MimeType) ||
                attachment.Size <= 0)
            {
                return "attachment_metadata_invalid";
            }

            if (attachment.AccessLevel == MediaAccessLevel.PublicRead)
            {
                if (string.IsNullOrWhiteSpace(attachment.PublicUrl))
                {
                    return "attachment_public_url_missing";
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(attachment.PublicUrl))
            {
                return "attachment_private_public_url_present";
            }
        }

        return null;
    }

    private sealed record ParsedMessageEvent(
        bool IsValid,
        MessageAcceptedEventV1? AcceptedEvent,
        string? Reason)
    {
        public static ParsedMessageEvent Valid(MessageAcceptedEventV1 acceptedEvent)
            => new(true, acceptedEvent, null);

        public static ParsedMessageEvent Invalid(string reason)
            => new(false, null, reason);
    }

    private sealed record MessagePersistenceUpsertResult(
        bool Inserted,
        bool MatchedExisting);
}

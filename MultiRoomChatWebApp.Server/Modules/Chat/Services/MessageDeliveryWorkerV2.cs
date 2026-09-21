using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
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
using StackExchange.Redis;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Services;

// Nhánh phát realtime của luồng gửi tin.
// Worker này đọc event tin nhắn đã được chấp nhận từ Redis Stream,
// tìm người nhận đang thuộc phòng, rồi đẩy tin xuống client qua SignalR.
public sealed class MessageDeliveryWorkerV2 : BackgroundService
{
    private const int WorkerId = 0;
    private static readonly RedisValue ReadNewEntries = ">";

    private readonly IChatBrokerConnection _brokerConnection;
    private readonly IChatBrokerKeyProvider _keyProvider;
    private readonly IChatBrokerConsumerIdentityProvider _consumerIdentityProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ChatBrokerOptions _brokerOptions;
    private readonly ILogger<MessageDeliveryWorkerV2> _logger;

    public MessageDeliveryWorkerV2(
        IChatBrokerConnection brokerConnection,
        IChatBrokerKeyProvider keyProvider,
        IChatBrokerConsumerIdentityProvider consumerIdentityProvider,
        IServiceScopeFactory scopeFactory,
        IHubContext<ChatHub, IChatClient> hubContext,
        IOptions<ChatBrokerOptions> brokerOptions,
        ILogger<MessageDeliveryWorkerV2> logger)
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

        // Consumer name định danh instance worker đang đọc nhánh delivery.
        // Nếu sau này scale nhiều instance, Redis dùng tên này để quản lý pending entry.
        var consumerName = _consumerIdentityProvider.GetConsumerName(
            ChatBrokerConsumerKind.Delivery,
            WorkerId);

        // Reclaimer kéo lại các entry delivery bị kẹt, ví dụ worker cũ chết giữa chừng.
        // Nhờ vậy tin chưa phát xong có thể được xử lý lại thay vì nằm pending mãi.
        var pendingEntryReclaimer = new ChatBrokerPendingEntryReclaimer(
            _keyProvider.MessageStream,
            _keyProvider.DeliveryGroup,
            consumerName,
            _brokerOptions.VisibilityTimeout,
            _brokerOptions.ReadBatchSize);

        // Retry tracker ghi nhớ entry nào phát lỗi và khi nào được thử lại.
        var retryTracker = new ChatBrokerRetryTracker(
            database,
            _keyProvider,
            ChatBrokerConsumerKind.Delivery,
            _brokerOptions);

        // Nếu một entry lỗi quá số lần cho phép, đưa vào dead-letter để không chặn luồng chính.
        var deadLetterPublisher = new ChatBrokerDeadLetterPublisher(
            database,
            _keyProvider,
            _brokerOptions);

        _logger.LogInformation(
            "MessageDeliveryWorkerV2 da khoi dong. Stream={Stream}; Group={Group}; Consumer={Consumer}; BatchSize={BatchSize}",
            _keyProvider.MessageStream,
            _keyProvider.DeliveryGroup,
            consumerName,
            _brokerOptions.ReadBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Trước khi đọc tin mới, xử lý lại các tin delivery bị pending quá lâu.
                await ReclaimPendingEntriesAsync(
                    database,
                    pendingEntryReclaimer,
                    retryTracker,
                    deadLetterPublisher,
                    consumerName,
                    stoppingToken);

                // Đọc các event mới từ stream chung, nhưng bằng consumer group của nhánh phát.
                // Dấu ">" nghĩa là chỉ lấy entry mới chưa giao cho consumer nào trong group này.
                var entries = await database.StreamReadGroupAsync(
                    _keyProvider.MessageStream,
                    _keyProvider.DeliveryGroup,
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
                    // Mỗi entry được xử lý tách biệt để một tin lỗi không làm hỏng cả batch.
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
                    "MessageDeliveryWorkerV2 gap loi khi doc broker. Thu lai sau 5s.");
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
                "MessageDeliveryWorkerV2 XAUTOCLAIM da loai {DeletedCount} pending reference khong con stream entry. Consumer={Consumer}",
                result.DeletedIds.Length,
                consumerName);
        }

        if (result.ClaimedEntries.Length == 0)
        {
            return;
        }

        _logger.LogInformation(
            "MessageDeliveryWorkerV2 da reclaim {ClaimedCount} pending entries. Consumer={Consumer}; VisibilityTimeoutMs={VisibilityTimeoutMs}; NextCursor={NextCursor}",
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
                    _keyProvider.DeliveryGroup.ToString(),
                    consumerName.ToString(),
                    retryDecision.Attempt,
                    payload);
                _logger.LogDebug(
                    "MessageDeliveryWorkerV2 bo qua entry chua den han retry. EntryId={EntryId}; Attempt={Attempt}; NextRetryAtUtc={NextRetryAtUtc}",
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
            _keyProvider.DeliveryGroup.ToString(),
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

            var result = await deadLetterPublisher.PublishDeliveryAsync(
                entry.Id,
                consumerName,
                originalPayload,
                originalFields,
                stoppingToken);

            if (!result.IsDeadLettered)
            {
                _logger.LogWarning(
                    "MessageDeliveryWorkerV2 khong dua entry vao DLQ vi attempt chua dat nguong tai thoi diem atomic check. EntryId={EntryId}; Attempt={Attempt}; MaxAttempts={MaxAttempts}",
                    entry.Id,
                    result.Attempt,
                    _brokerOptions.MaxAttempts);
                return;
            }

            if (result.AcknowledgedCount == 0)
            {
                _logger.LogWarning(
                    "MessageDeliveryWorkerV2 da ghi delivery DLQ nhung chua ACK duoc entry goc. EntryId={EntryId}; DlqEntryId={DlqEntryId}; Attempt={Attempt}",
                    entry.Id,
                    result.DeadLetterEntryId,
                    result.Attempt);
                return;
            }

            _logger.LogError(
                "MessageDeliveryWorkerV2 da dua entry vao delivery DLQ va ACK entry goc. EntryId={EntryId}; DlqEntryId={DlqEntryId}; Attempt={Attempt}; Consumer={Consumer}",
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
                "MessageDeliveryWorkerV2 khong ghi duoc delivery DLQ; entry van pending. EntryId={EntryId}; Consumer={Consumer}",
                entry.Id,
                consumerName);
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
            _keyProvider.DeliveryGroup.ToString(),
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
                "delivery_unhandled_exception",
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
                "MessageDeliveryWorkerV2 xu ly entry that bai. EntryId={EntryId}; FailureKind={FailureKind}; Code={Code}; Attempt={Attempt}; MaxAttempts={MaxAttempts}; NextRetryAtUtc={NextRetryAtUtc}",
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
                "MessageDeliveryWorkerV2 khong ghi duoc retry state. EntryId={EntryId}; FailureKind={FailureKind}; Code={Code}; OriginalReason={OriginalReason}",
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
                "MessageDeliveryWorkerV2 khong xoa duoc retry state sau khi xu ly thanh cong. EntryId={EntryId}",
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

        // Bước 1: lấy gói tin mà publisher đã đưa vào Redis Stream.
        var payload = entry.Values
            .FirstOrDefault(value => value.Name == "payload")
            .Value;

        // Bước 2: đọc lại MessageAcceptedEventV1 từ gói tin.
        // Nếu payload hỏng thì đây là lỗi vĩnh viễn, không nên retry mãi.
        var parseResult = TryParseAndValidateEvent(payload);
        if (!parseResult.IsValid || parseResult.AcceptedEvent is null)
        {
            throw ChatBrokerEntryProcessingException.Permanent(
                parseResult.Reason ?? "payload_invalid",
                "Delivery event khong hop le.");
        }

        var acceptedEvent = parseResult.AcceptedEvent;

        // Bước 3: tìm danh sách user cần nhận tin trong room này.
        // Nhánh phát chỉ cần biết ai là người nhận để bắn SignalR.
        var recipientResolver = scope.ServiceProvider
            .GetRequiredService<IMessageDeliveryRecipientResolver>();
        var recipientResult = await recipientResolver.ResolveAsync(
            acceptedEvent.RoomId,
            acceptedEvent.SenderId,
            entryToken);

        if (!recipientResult.IsSuccess || recipientResult.RecipientIds.Count == 0)
        {
            throw ChatBrokerEntryProcessingException.Permanent(
                recipientResult.ErrorCode ?? "delivery_recipient_resolution_failed",
                recipientResult.ErrorReason ??
                "Khong resolve duoc nguoi nhan cua message.");
        }

        var deliveryAtUtc = DateTime.UtcNow;

        // Bước 4: chuẩn bị thông tin file đính kèm để client hiển thị được ngay.
        var attachmentResolver = scope.ServiceProvider
            .GetRequiredService<IMessageDeliveryAttachmentResolver>();
        var attachmentResult = await attachmentResolver.ResolveAsync(
            acceptedEvent,
            deliveryAtUtc,
            entryToken);

        if (!attachmentResult.IsSuccess)
        {
            throw ChatBrokerEntryProcessingException.Permanent(
                attachmentResult.ErrorCode ?? "delivery_attachment_resolution_failed",
                attachmentResult.ErrorReason ??
                "Khong resolve duoc attachment cua message.");
        }

        var content = acceptedEvent.Content.Trim();

        // Bước 5: chuyển event nội bộ thành DTO mà frontend hiểu được.
        var deliveryMessage = new MessageDeliveryDto
        {
            Id = acceptedEvent.MessageId,
            ClientMessageId = acceptedEvent.ClientMessageId,
            RoomId = acceptedEvent.RoomId,
            SenderId = acceptedEvent.SenderId,
            Type = DetermineMessageType(content, attachmentResult.Attachments),
            Content = content,
            Attachments = attachmentResult.Attachments.Count == 0
                ? null
                : attachmentResult.Attachments.ToList(),
            Status = MessageStatus.Accepted,
            AcceptedAtUtc = acceptedEvent.AcceptedAtUtc,
            CreatedAt = acceptedEvent.AcceptedAtUtc
        };

        var recipientUserIds = recipientResult.RecipientIds
            .Select(userId => userId.ToString())
            .ToList();

        // Bước 6: phát tin xuống các client online của những user nhận tin.
        await _hubContext.Clients.Users(recipientUserIds)
            .ReceiveMessage(deliveryMessage);

        _logger.LogInformation(
            "MessageDeliveryWorkerV2 da broadcast typed DTO. EntryId={EntryId}; MessageId={MessageId}; RecipientCount={RecipientCount}; AttachmentCount={AttachmentCount}; PeerDeliverySuppressed={PeerDeliverySuppressed}; Scope={ScopeId}",
            entry.Id,
            acceptedEvent.MessageId,
            recipientUserIds.Count,
            attachmentResult.Attachments.Count,
            recipientResult.IsPeerDeliverySuppressed,
            scope.ServiceProvider.GetHashCode());

        // Bước 7: ACK với Redis rằng nhánh delivery đã xử lý xong entry này.
        // Nếu không ACK, entry sẽ còn pending và có thể bị worker khác xử lý lại.
        var acknowledgedCount = await database.StreamAcknowledgeAsync(
            _keyProvider.MessageStream,
            _keyProvider.DeliveryGroup,
            entry.Id);

        if (acknowledgedCount == 0)
        {
            _logger.LogWarning(
                "MessageDeliveryWorkerV2 khong tim thay pending entry de ACK. EntryId={EntryId}; MessageId={MessageId}",
                entry.Id,
                acceptedEvent.MessageId);
            return;
        }

        _logger.LogInformation(
            "MessageDeliveryWorkerV2 da ACK delivery entry sau khi realtime publish thanh cong. EntryId={EntryId}; MessageId={MessageId}",
            entry.Id,
            acceptedEvent.MessageId);

        // Khong XDEL: persistence group va retention maintenance van can stream entry.
        // ACK nay chi xac nhan realtime layer da chap nhan publish, khong phai device da nhan.
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

    private static ParsedMessageEvent TryParseAndValidateEvent(RedisValue payload)
    {
        if (payload.IsNullOrEmpty)
        {
            return ParsedMessageEvent.Invalid("payload_missing");
        }

        var payloadText = payload.ToString();
        if (Encoding.UTF8.GetByteCount(payloadText) > MessageAcceptedEventV1.MaxPayloadBytes)
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

        if (acceptedEvent.CorrelationId == Guid.Empty ||
            acceptedEvent.ClientMessageId == Guid.Empty ||
            acceptedEvent.RoomId == Guid.Empty ||
            acceptedEvent.SenderId == Guid.Empty)
        {
            return "event_identity_invalid";
        }

        if (!ObjectId.TryParse(acceptedEvent.MessageId, out _))
        {
            return "message_id_invalid";
        }

        if (acceptedEvent.AcceptedAtUtc == default)
        {
            return "acceptance_metadata_invalid";
        }

        var content = acceptedEvent.Content?.Trim() ?? string.Empty;
        var mediaIds = acceptedEvent.MediaIds ?? [];
        var attachments = acceptedEvent.Attachments ?? [];

        if (content.Length > MessageAcceptedEventV1.MaxContentLength)
        {
            return "content_too_long";
        }

        if (string.IsNullOrWhiteSpace(content) && mediaIds.Count == 0)
        {
            return "message_empty";
        }

        if (mediaIds.Count > MessageAcceptedEventV1.MaxAttachmentCount ||
            mediaIds.Count != attachments.Count ||
            mediaIds.Any(mediaId => mediaId == Guid.Empty) ||
            mediaIds.Distinct().Count() != mediaIds.Count)
        {
            return "attachment_identity_invalid";
        }

        for (var index = 0; index < mediaIds.Count; index++)
        {
            if (attachments[index].MediaId != mediaIds[index])
            {
                return "attachment_order_invalid";
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
}

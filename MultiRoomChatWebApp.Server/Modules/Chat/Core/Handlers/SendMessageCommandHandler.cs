using MediatR;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Commands;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Exceptions;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Chat.Core.Logging;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Media.Core.Interfaces;
using System.Text;
using System.Text.Json;

namespace MultiRoomChatWebApp.Server.Modules.Chat.Core.Handlers;

public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, MessageAcceptedResult>
{
    private readonly IMessageAdmissionService _messageAdmissionService;
    private readonly IChatMediaReservationService _mediaReservationService;
    private readonly IMessageIdentityService _messageIdentityService;
    private readonly IChatMessagePublisher _messagePublisher;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    public SendMessageCommandHandler(
        IMessageAdmissionService messageAdmissionService,
        IChatMediaReservationService mediaReservationService,
        IMessageIdentityService messageIdentityService,
        IChatMessagePublisher messagePublisher,
        ILogger<SendMessageCommandHandler> logger)
    {
        _messageAdmissionService = messageAdmissionService;
        _mediaReservationService = mediaReservationService;
        _messageIdentityService = messageIdentityService;
        _messagePublisher = messagePublisher;
        _logger = logger;
    }

    public async Task<MessageAcceptedResult> Handle(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        using var admissionScope = ChatMessageLogScope.Begin(
            _logger,
            messageId: null,
            request.ClientMessageId,
            streamId: null,
            request.RoomId,
            request.SenderId,
            group: "admission",
            consumer: "signalr",
            attempt: 0,
            correlationId: request.ClientMessageId);

        var admission = await _messageAdmissionService.AdmitAsync(request, cancellationToken);
        if (!admission.IsAccepted || admission.Context is null)
        {
            _logger.LogWarning(
                "Send message admission rejected. Code={Code}, Kind={Kind}, Retryable={IsRetryable}",
                admission.Error?.Code,
                admission.Error?.Kind,
                admission.Error?.IsRetryable);
            throw new MessageAdmissionException(
                admission.Error
                ?? MessageAdmissionError.Validation(
                    "message_admission_rejected",
                    "Tin nhan khong duoc he thong tiep nhan."));
        }

        var admitted = admission.Context;
        var identity = await _messageIdentityService.ResolveAsync(
            admitted.SenderId,
            admitted.ClientMessageId);
        using var identityScope = ChatMessageLogScope.Begin(
            _logger,
            identity.MessageId,
            admitted.ClientMessageId,
            streamId: null,
            admitted.RoomId,
            admitted.SenderId,
            group: "admission",
            consumer: "chat-v2",
            attempt: 0,
            correlationId: admitted.ClientMessageId);

        var reservation = await _mediaReservationService.ReserveAsync(
            admitted.MediaIds,
            admitted.SenderId,
            admitted.RoomId,
            identity.MessageId,
            identity.AcceptedAtUtc,
            cancellationToken);

        if (!reservation.IsSuccess)
        {
            _logger.LogWarning(
                "Khong reserve duoc attachment cho User {UserId}, Room {RoomId}, MessageId={MessageId}. Reason={Reason}",
                admitted.SenderId,
                admitted.RoomId,
                identity.MessageId,
                reservation.RejectionReason);
            throw new MessageAdmissionException(
                MessageAdmissionError.Conflict(
                    "message_attachments_unavailable",
                    "Mot hoac nhieu tep dinh kem khong con hop le de gui."));
        }

        var mediaAssets = await _mediaReservationService.LoadForPersistenceAsync(
            admitted.MediaIds,
            admitted.SenderId,
            admitted.RoomId,
            identity.MessageId,
            cancellationToken);

        if (mediaAssets is null)
        {
            _logger.LogWarning(
                "Khong load duoc attachment snapshot cho User {UserId}, Room {RoomId}, MessageId={MessageId}.",
                admitted.SenderId,
                admitted.RoomId,
                identity.MessageId);

            if (reservation.IsNewReservation)
            {
                await ReleaseReservationBestEffortAsync(admitted.MediaIds, identity.MessageId, cancellationToken);
            }

            throw new MessageAdmissionException(
                MessageAdmissionError.Conflict(
                    "message_attachment_snapshot_unavailable",
                    "Khong the xac nhan tep dinh kem cua tin nhan."));
        }

        var acceptedEvent = new MessageAcceptedEventV1
        {
            CorrelationId = admitted.ClientMessageId,
            MessageId = identity.MessageId,
            ClientMessageId = admitted.ClientMessageId,
            AcceptedAtUtc = identity.AcceptedAtUtc,
            RoomId = admitted.RoomId,
            SenderId = admitted.SenderId,
            Content = admitted.Content,
            MediaIds = admitted.MediaIds.ToList(),
            Attachments = BuildAttachmentSnapshots(mediaAssets)
        };
        var messagePayload = JsonSerializer.Serialize(acceptedEvent);
        var payloadBytes = Encoding.UTF8.GetByteCount(messagePayload);
        if (payloadBytes > MessageAcceptedEventV1.MaxPayloadBytes)
        {
            _logger.LogWarning(
                "Message event payload qua lon. MessageId={MessageId}, PayloadBytes={PayloadBytes}, MaxPayloadBytes={MaxPayloadBytes}",
                identity.MessageId,
                payloadBytes,
                MessageAcceptedEventV1.MaxPayloadBytes);

            if (reservation.IsNewReservation)
            {
                await ReleaseReservationBestEffortAsync(admitted.MediaIds, identity.MessageId, cancellationToken);
            }

            throw new MessageAdmissionException(
                MessageAdmissionError.Validation(
                    "message_payload_too_large",
                    "Tin nhan vuot qua gioi han xu ly cua he thong."));
        }

        ChatMessagePublishResult publishResult;

        try
        {
            publishResult = await _messagePublisher.PublishAsync(
                acceptedEvent,
                cancellationToken);
        }
        catch (Exception publishException)
        {
            if (reservation.IsNewReservation)
            {
                await ReleaseReservationBestEffortAsync(admitted.MediaIds, identity.MessageId, cancellationToken);
            }

            _logger.LogError(
                publishException,
                "Khong publish duoc message {MessageId} vao broker.",
                identity.MessageId);
            throw new MessageAdmissionException(
                MessageAdmissionError.BrokerUnavailable(
                    "message_broker_unavailable",
                    "He thong chua the tiep nhan tin nhan luc nay, vui long thu lai."),
                publishException);
        }

        using var publishedScope = ChatMessageLogScope.Begin(
            _logger,
            publishResult.MessageId,
            admitted.ClientMessageId,
            publishResult.StreamId,
            admitted.RoomId,
            admitted.SenderId,
            group: "admission",
            consumer: "chat-v2",
            attempt: 0,
            correlationId: admitted.ClientMessageId);

        _logger.LogInformation(
            "Message admission accepted. IsNewEvent={IsNewEvent}; MessageId={MessageId}; StreamId={StreamId}",
            publishResult.IsNewEvent,
            publishResult.MessageId,
            publishResult.StreamId);

        return new MessageAcceptedResult(
            admitted.ClientMessageId,
            publishResult.MessageId,
            publishResult.StreamId,
            publishResult.AcceptedAtUtc);
    }

    private static List<MessageAcceptedAttachmentV1> BuildAttachmentSnapshots(
        IReadOnlyList<MediaAsset> mediaAssets)
    {
        if (mediaAssets.Count == 0)
        {
            return [];
        }

        return mediaAssets
            .Select(asset => new MessageAcceptedAttachmentV1
            {
                MediaId = asset.Id,
                Kind = asset.Kind,
                AccessLevel = asset.AccessLevel,
                BucketName = asset.BucketName,
                StorageKey = asset.StorageKey,
                Filename = asset.OriginalFileName,
                Size = asset.SizeBytes,
                MimeType = asset.ContentType,
                PublicUrl = asset.AccessLevel == MediaAccessLevel.PublicRead
                    ? asset.PublicUrl
                    : null
            })
            .ToList();
    }

    private async Task ReleaseReservationBestEffortAsync(
        IReadOnlyCollection<Guid> mediaIds,
        string messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mediaReservationService.ReleaseAsync(mediaIds, messageId, cancellationToken);
        }
        catch (Exception releaseException)
        {
            _logger.LogError(
                releaseException,
                "Khong release duoc attachment reservation. MessageId={MessageId}",
                messageId);
        }
    }
}

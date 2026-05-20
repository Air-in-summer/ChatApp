using MediatR;
using MongoDB.Driver;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Room.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.DTOs;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Entities;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Enums;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Events;
using MultiRoomChatWebApp.Server.Modules.Voice.Core.Interfaces;

namespace MultiRoomChatWebApp.Server.Modules.Voice.Services;

/// <summary>
/// Service quản lý lifecycle của VoiceSession cho DM call.
/// Phase đầu chỉ persist DirectCall, không ghi voice room channel vào MongoDB.
/// </summary>
public class VoiceSessionService : IVoiceSessionService
{
    private const string VoiceSessionsCollectionName = "voice_sessions";

    private static readonly VoiceSessionStatus[] ActiveDirectCallStatuses =
    [
        VoiceSessionStatus.Ringing,
        VoiceSessionStatus.Active
    ];

    private readonly IMongoCollection<VoiceSession> _voiceSessions;
    private readonly IRoomMetadataCache _roomMetadataCache;
    private readonly IRoomPermissionsCache _roomPermissionsCache;
    private readonly IVoiceTokenService _voiceTokenService;
    private readonly IMediator _mediator;
    private readonly ILogger<VoiceSessionService> _logger;

    public VoiceSessionService(
        IMongoDatabase mongoDatabase,
        IRoomMetadataCache roomMetadataCache,
        IRoomPermissionsCache roomPermissionsCache,
        IVoiceTokenService voiceTokenService,
        IMediator mediator,
        ILogger<VoiceSessionService> logger)
    {
        _voiceSessions = mongoDatabase.GetCollection<VoiceSession>(VoiceSessionsCollectionName);
        _roomMetadataCache = roomMetadataCache;
        _roomPermissionsCache = roomPermissionsCache;
        _voiceTokenService = voiceTokenService;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<VoiceSessionTokenResponseDto> StartDirectCallAsync(
        Guid dmRoomId,
        Guid callerUserId,
        string callerDisplayName)
    {
        await EnsureDirectMessageRoomAsync(dmRoomId);

        var memberIds = (await _roomPermissionsCache.GetRoomMemberIdsAsync(dmRoomId))
            .Distinct()
            .ToList();

        if (memberIds.Count != 2)
        {
            throw new ArgumentException("DirectMessage room phải có đúng 2 thành viên.");
        }

        if (!memberIds.Contains(callerUserId))
        {
            throw new UnauthorizedAccessException("Bạn không phải thành viên của DM room này.");
        }

        await EnsureNoActiveDirectCallAsync(dmRoomId);

        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var calleeUserId = memberIds.First(id => id != callerUserId);

        var session = new VoiceSession
        {
            Id = sessionId,
            Kind = VoiceSessionKind.DirectCall,
            Status = VoiceSessionStatus.Ringing,
            SourceRoomId = dmRoomId,
            CreatedByUserId = callerUserId,
            LiveKitRoomName = $"call:{sessionId}",
            CreatedAt = now,
            Participants =
            [
                new VoiceSessionParticipant
                {
                    UserId = callerUserId,
                    Status = VoiceParticipantStatus.Joined,
                    InvitedAt = now,
                    JoinedAt = now
                },
                new VoiceSessionParticipant
                {
                    UserId = calleeUserId,
                    Status = VoiceParticipantStatus.Invited,
                    InvitedAt = now
                }
            ]
        };

        await _voiceSessions.InsertOneAsync(session);

        _logger.LogInformation(
            "Đã tạo DirectCall VoiceSession {SessionId} cho DM room {RoomId}, caller {CallerUserId}, callee {CalleeUserId}",
            session.Id, dmRoomId, callerUserId, calleeUserId);

        await _mediator.Publish(new VoiceCallIncomingEvent(
            calleeUserId,
            new VoiceCallIncomingDto
            {
                Session = MapSession(session),
                CallerId = callerUserId,
                CallerDisplayName = callerDisplayName
            }));

        return CreateTokenResponse(session, callerUserId, callerDisplayName);
    }

    public async Task<VoiceSessionTokenResponseDto> AcceptAsync(
        Guid sessionId,
        Guid userId,
        string displayName)
    {
        var session = await GetDirectCallSessionOrThrowAsync(sessionId);

        if (session.Status != VoiceSessionStatus.Ringing)
        {
            throw new InvalidOperationException("Chỉ DM call đang Ringing mới có thể accept.");
        }

        var participant = GetParticipantOrThrow(session, userId);
        if (participant.Status != VoiceParticipantStatus.Invited)
        {
            throw new UnauthorizedAccessException("Chỉ participant đang được mời mới có thể accept call.");
        }

        var now = DateTime.UtcNow;
        participant.Status = VoiceParticipantStatus.Joined;
        participant.JoinedAt = now;
        session.Status = VoiceSessionStatus.Active;
        session.StartedAt ??= now;

        await ReplaceSessionAsync(session);

        _logger.LogInformation(
            "User {UserId} đã accept DirectCall VoiceSession {SessionId}",
            userId, sessionId);

        await _mediator.Publish(new VoiceCallAcceptedEvent(
            session.CreatedByUserId,
            new VoiceCallStatusChangedDto
            {
                Session = MapSession(session),
                ActorUserId = userId
            }));

        return CreateTokenResponse(session, userId, displayName);
    }

    public async Task<VoiceSessionResponseDto> DeclineAsync(Guid sessionId, Guid userId)
    {
        var session = await GetDirectCallSessionOrThrowAsync(sessionId);

        if (session.Status != VoiceSessionStatus.Ringing)
        {
            throw new InvalidOperationException("Chỉ DM call đang Ringing mới có thể decline.");
        }

        var participant = GetParticipantOrThrow(session, userId);
        if (participant.Status != VoiceParticipantStatus.Invited)
        {
            throw new UnauthorizedAccessException("Chỉ participant đang được mời mới có thể decline call.");
        }

        participant.Status = VoiceParticipantStatus.Declined;
        session.Status = VoiceSessionStatus.Declined;
        session.EndedAt = DateTime.UtcNow;

        await ReplaceSessionAsync(session);

        _logger.LogInformation(
            "User {UserId} đã decline DirectCall VoiceSession {SessionId}",
            userId, sessionId);

        await _mediator.Publish(new VoiceCallDeclinedEvent(
            session.CreatedByUserId,
            new VoiceCallStatusChangedDto
            {
                Session = MapSession(session),
                ActorUserId = userId
            }));

        return MapSession(session);
    }

    public async Task<VoiceSessionTokenResponseDto> GetTokenAsync(
        Guid sessionId,
        Guid userId,
        string displayName)
    {
        var session = await GetDirectCallSessionOrThrowAsync(sessionId);

        if (IsTerminalStatus(session.Status))
        {
            throw new InvalidOperationException("VoiceSession đã kết thúc, không thể cấp token.");
        }

        var participant = GetParticipantOrThrow(session, userId);
        if (participant.Status != VoiceParticipantStatus.Joined)
        {
            throw new UnauthorizedAccessException("Participant chưa joined nên không được cấp token.");
        }

        return CreateTokenResponse(session, userId, displayName);
    }

    public async Task<VoiceSessionResponseDto> LeaveAsync(Guid sessionId, Guid userId)
    {
        var session = await GetDirectCallSessionOrThrowAsync(sessionId);
        var participant = GetParticipantOrThrow(session, userId);

        if (IsTerminalStatus(session.Status))
        {
            return MapSession(session);
        }

        if (participant.Status != VoiceParticipantStatus.Joined)
        {
            throw new InvalidOperationException("Chỉ participant đã joined mới có thể leave call.");
        }

        var now = DateTime.UtcNow;
        participant.Status = VoiceParticipantStatus.Left;
        participant.LeftAt = now;

        // Neu callee chua tung accept/join, caller cancel khi con Ringing la missed call.
        session.Status = ShouldMarkAsMissedOnLeave(session)
            ? VoiceSessionStatus.Missed
            : VoiceSessionStatus.Ended;
        session.EndedAt = now;

        await ReplaceSessionAsync(session);

        _logger.LogInformation(
            "User {UserId} đã leave DirectCall VoiceSession {SessionId}",
            userId, sessionId);

        await _mediator.Publish(new VoiceCallEndedEvent(
            session.Participants
                .Where(p => p.UserId != userId)
                .Select(p => p.UserId),
            new VoiceCallStatusChangedDto
            {
                Session = MapSession(session),
                ActorUserId = userId
            }));

        return MapSession(session);
    }

    public async Task<int> MarkExpiredRingingCallsAsMissedAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<VoiceSession>.Filter.And(
            Builders<VoiceSession>.Filter.Eq(s => s.Kind, VoiceSessionKind.DirectCall),
            Builders<VoiceSession>.Filter.Eq(s => s.Status, VoiceSessionStatus.Ringing),
            Builders<VoiceSession>.Filter.Lte(s => s.CreatedAt, cutoffUtc));

        var expiredSessions = await _voiceSessions
            .Find(filter)
            .ToListAsync(cancellationToken);

        if (expiredSessions.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var missedCount = 0;

        foreach (var session in expiredSessions)
        {
            session.Status = VoiceSessionStatus.Missed;
            session.EndedAt = now;

            var replaceFilter = Builders<VoiceSession>.Filter.And(
                Builders<VoiceSession>.Filter.Eq(s => s.Id, session.Id),
                Builders<VoiceSession>.Filter.Eq(s => s.Status, VoiceSessionStatus.Ringing));

            var result = await _voiceSessions.ReplaceOneAsync(
                replaceFilter,
                session,
                cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                continue;
            }

            missedCount++;

            await _mediator.Publish(new VoiceCallEndedEvent(
                session.Participants.Select(p => p.UserId),
                new VoiceCallStatusChangedDto
                {
                    Session = MapSession(session),
                    ActorUserId = session.CreatedByUserId
                }), cancellationToken);
        }

        if (missedCount > 0)
        {
            _logger.LogInformation(
                "Da mark {MissedCount} DirectCall VoiceSession qua han thanh Missed",
                missedCount);
        }

        return missedCount;
    }

    public async Task HandleLiveKitParticipantLeftAsync(
        string liveKitRoomName,
        string participantIdentity,
        CancellationToken cancellationToken)
    {
        if (!liveKitRoomName.StartsWith("call:", StringComparison.Ordinal))
        {
            return;
        }

        if (!Guid.TryParse(participantIdentity, out var userId))
        {
            _logger.LogWarning(
                "Bo qua LiveKit participant_left vi identity khong phai Guid: {ParticipantIdentity}",
                participantIdentity);
            return;
        }

        var session = await _voiceSessions
            .Find(s => s.Kind == VoiceSessionKind.DirectCall && s.LiveKitRoomName == liveKitRoomName)
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null || IsTerminalStatus(session.Status))
        {
            return;
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant?.Status != VoiceParticipantStatus.Joined)
        {
            return;
        }

        // Reuse LeaveAsync de giu dung business rule hien co:
        // - Ringing + caller roi -> Missed.
        // - Active + mot ben roi -> Ended.
        await LeaveAsync(session.Id, userId);
    }

    private async Task EnsureDirectMessageRoomAsync(Guid dmRoomId)
    {
        var metadata = await _roomMetadataCache.GetRoomMetadataAsync(dmRoomId);
        if (metadata == null || metadata.Value.Type != RoomType.DirectMessage)
        {
            throw new KeyNotFoundException("DirectMessage room không tồn tại.");
        }
    }

    private async Task EnsureNoActiveDirectCallAsync(Guid dmRoomId)
    {
        var filter = Builders<VoiceSession>.Filter.And(
            Builders<VoiceSession>.Filter.Eq(s => s.Kind, VoiceSessionKind.DirectCall),
            Builders<VoiceSession>.Filter.Eq(s => s.SourceRoomId, dmRoomId),
            Builders<VoiceSession>.Filter.In(s => s.Status, ActiveDirectCallStatuses));

        var exists = await _voiceSessions.Find(filter).AnyAsync();
        if (exists)
        {
            throw new InvalidOperationException("DM room này đang có call chưa kết thúc.");
        }
    }

    private async Task<VoiceSession> GetDirectCallSessionOrThrowAsync(Guid sessionId)
    {
        var session = await _voiceSessions
            .Find(s => s.Id == sessionId && s.Kind == VoiceSessionKind.DirectCall)
            .FirstOrDefaultAsync();

        return session ?? throw new KeyNotFoundException("VoiceSession không tồn tại.");
    }

    private async Task ReplaceSessionAsync(VoiceSession session)
    {
        var result = await _voiceSessions.ReplaceOneAsync(s => s.Id == session.Id, session);
        if (result.MatchedCount == 0)
        {
            throw new KeyNotFoundException("VoiceSession không tồn tại.");
        }
    }

    private VoiceSessionTokenResponseDto CreateTokenResponse(
        VoiceSession session,
        Guid userId,
        string displayName)
    {
        var tokenTtl = _voiceTokenService.GetTokenTtl();

        return new VoiceSessionTokenResponseDto
        {
            Session = MapSession(session),
            Token = _voiceTokenService.GenerateTokenForLiveKitRoom(session.LiveKitRoomName, userId, displayName),
            LiveKitHost = _voiceTokenService.GetLiveKitHost(),
            ExpiresAtUtc = DateTime.UtcNow.Add(tokenTtl),
            ExpiresInSeconds = (int)tokenTtl.TotalSeconds
        };
    }

    private static VoiceSessionParticipant GetParticipantOrThrow(VoiceSession session, Guid userId)
    {
        return session.Participants.FirstOrDefault(p => p.UserId == userId)
            ?? throw new UnauthorizedAccessException("Bạn không phải participant của VoiceSession này.");
    }

    private static bool IsTerminalStatus(VoiceSessionStatus status)
    {
        return status is VoiceSessionStatus.Ended
            or VoiceSessionStatus.Declined
            or VoiceSessionStatus.Missed;
    }

    private static bool ShouldMarkAsMissedOnLeave(VoiceSession session)
    {
        return session.Status == VoiceSessionStatus.Ringing
            && session.Participants.Any(p => p.Status == VoiceParticipantStatus.Invited);
    }

    private static VoiceSessionResponseDto MapSession(VoiceSession session)
    {
        return new VoiceSessionResponseDto
        {
            SessionId = session.Id,
            Kind = session.Kind,
            Status = session.Status,
            SourceRoomId = session.SourceRoomId,
            CreatedByUserId = session.CreatedByUserId,
            LiveKitRoomName = session.LiveKitRoomName,
            CreatedAt = session.CreatedAt,
            StartedAt = session.StartedAt,
            EndedAt = session.EndedAt,
            Participants = session.Participants
                .Select(p => new VoiceSessionParticipantDto
                {
                    UserId = p.UserId,
                    Status = p.Status,
                    InvitedAt = p.InvitedAt,
                    JoinedAt = p.JoinedAt,
                    LeftAt = p.LeftAt
                })
                .ToList()
        };
    }
}

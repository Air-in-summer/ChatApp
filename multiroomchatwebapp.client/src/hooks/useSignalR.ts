import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import { useChatStore } from '../store/useChatStore';
import { useNotificationStore } from '../store/useNotificationStore';
import { useUserRelationshipsStore } from '../store/useUserRelationshipsStore';
import { useVoiceStore } from '../store/useVoiceStore';
import type { VoiceCallIncomingDto, VoiceCallStatusChangedDto } from '../api/voiceApi';
import type {
  MessageAcceptedResult,
  MessageDeletedDto,
  MessageDto,
  MessageEditedDto,
  MessagePinnedDto,
  MessagePersistedDto,
  MessagePersistenceFailedDto,
  MessageReactionUpdatedDto,
  MessageRetractedDto,
  MessageUnpinnedDto,
} from '../types/chat';
import { buildMessagePreview } from '../utils/chatMessagePreview';

const PRESENCE_HEARTBEAT_INTERVAL_MS = 30_000;

const cleanupActiveDirectCallIfMatches = (
  payload: VoiceCallStatusChangedDto,
  reason: string
) => {
  const activeSession = useVoiceStore.getState().activeSession;

  if (
    activeSession?.kind === 'direct-call' &&
    activeSession.sessionId === payload.session.sessionId
  ) {
    void import('../services/voiceConnectionService')
      .then(({ leaveVoiceRoom }) => leaveVoiceRoom())
      .catch((error) => {
        console.error(`Khong the cleanup DM call sau ${reason}:`, error);
      });
  }
};
/**
 * Hook quản lý kết nối SignalR (Singleton lifecycle gắn với MainLayout).
 *
 * @remarks
 * Luồng xử lý:
 * 1. Khởi tạo kết nối khi có accessToken.
 * 2. Đăng ký các sự kiện lắng nghe: ReceiveMessage, ReceiveTyping, ReceiveReadReceipt và typed message events.
 * 3. Expose các method gửi lệnh lên Backend.
 * 4. Cleanup khi unmount.
 *
 * Lưu ý quan trọng:
 * - Backend (C#) serialize Entity dùng camelCase convention, nên field name
 *   có thể là `roomId` hoặc `room_id` tùy cấu hình. Hook này map lại cho đúng.
 * - Khi ReceiveMessage, cần kiểm tra xem tin nhắn có phải do chính user này gửi không.
 *   Nếu đúng → thay thế tin tạm (Optimistic) bằng tin thật. Nếu không → thêm mới.
 * - MessagePersisted/MessagePersistenceFailed/MessageRetracted là nguồn reconcile trạng thái V2.
 */
export const useSignalR = () => {
  const { accessToken, user } = useAuth();
  const currentUserId = user?.userId;
  const [isConnected, setIsConnected] = useState(false);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  // Lấy actions từ Zustand Store (chỉ lấy function, không gây re-render)
  const addMessage = useChatStore((state) => state.addMessage);
  const updateMessageStatus = useChatStore((state) => state.updateMessageStatus);
  const retractMessage = useChatStore((state) => state.retractMessage);
  const editStoredMessage = useChatStore((state) => state.editMessage);
  const markMessageDeleted = useChatStore((state) => state.markMessageDeleted);
  const applyReactionUpdate = useChatStore((state) => state.applyReactionUpdate);
  const applyMessagePinned = useChatStore((state) => state.applyMessagePinned);
  const applyMessageUnpinned = useChatStore((state) => state.applyMessageUnpinned);
  const setTyping = useChatStore((state) => state.setTyping);
  const setReadReceipt = useChatStore((state) => state.setReadReceipt);
  const updateRoomMetadata = useChatStore((state) => state.updateRoomMetadata);

  useEffect(() => {
    if (!accessToken) return;

    let heartbeatTimer: ReturnType<typeof window.setInterval> | undefined;

    // 1. Khởi tạo kết nối
    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl('https://localhost:7222/hub/chat', {
        accessTokenFactory: () => accessToken,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    connectionRef.current = newConnection;

    const startPresenceHeartbeat = () => {
      if (heartbeatTimer !== undefined) return;

      heartbeatTimer = window.setInterval(() => {
        if (newConnection.state === signalR.HubConnectionState.Connected) {
          newConnection.invoke('Heartbeat').catch((error) => {
            console.error('Lỗi Heartbeat SignalR:', error);
          });
        }
      }, PRESENCE_HEARTBEAT_INTERVAL_MS);
    };

    const stopPresenceHeartbeat = () => {
      if (heartbeatTimer === undefined) return;

      window.clearInterval(heartbeatTimer);
      heartbeatTimer = undefined;
    };

    newConnection.onreconnecting(() => {
      setIsConnected(false);
      stopPresenceHeartbeat();
    });

    newConnection.onreconnected(() => {
      setIsConnected(true);
      startPresenceHeartbeat();
      newConnection.invoke('Heartbeat').catch((error) => {
        console.error('Lỗi Heartbeat sau reconnect SignalR:', error);
      });
      useNotificationStore.getState().triggerRealtimeSync();
    });

    newConnection.onclose(() => {
      setIsConnected(false);
      stopPresenceHeartbeat();
    });

    // 2. Đăng ký các sự kiện lắng nghe từ Backend

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    newConnection.on('ReceiveMessage', (raw: any) => {
      console.log('📬 Raw ReceiveMessage từ Backend:', raw);

      // Map field name từ C# Entity (camelCase convention) sang MessageDto Frontend
      const message: MessageDto = {
        id: raw.id ?? raw.Id ?? '',
        clientMessageId:
          raw.clientMessageId ??
          raw.ClientMessageId ??
          raw.client_message_id ??
          null,
        roomId: raw.roomId ?? raw.RoomId ?? raw.room_id ?? '',
        senderId: raw.senderId ?? raw.SenderId ?? raw.sender_id ?? '',
        type: raw.type ?? raw.Type ?? 'Text',
        content: raw.content ?? raw.Content ?? '',
        status: raw.status ?? raw.Status ?? 'Sent',
        createdAt: raw.createdAt ?? raw.CreatedAt ?? raw.created_at ?? new Date().toISOString(),
        acceptedAtUtc:
          raw.acceptedAtUtc ??
          raw.AcceptedAtUtc ??
          raw.accepted_at ??
          null,
        attachments: Array.isArray(raw.attachments ?? raw.Attachments)
          ? (raw.attachments ?? raw.Attachments).map((attachment: any) => ({
              mediaId: attachment.mediaId ?? attachment.MediaId ?? attachment.media_id ?? null,
              kind: attachment.kind ?? attachment.Kind ?? 'File',
              filename: attachment.filename ?? attachment.Filename ?? '',
              size: attachment.size ?? attachment.Size ?? 0,
              mimeType: attachment.mimeType ?? attachment.MimeType ?? attachment.mime_type ?? '',
              url: attachment.url ?? attachment.Url ?? '',
              thumbnailUrl: attachment.thumbnailUrl ?? attachment.ThumbnailUrl ?? attachment.thumbnail_url ?? null,
              expiresAt: attachment.expiresAt ?? attachment.ExpiresAt ?? attachment.expires_at ?? null,
            }))
          : null,
      };

      if (!message.roomId) {
        console.warn('⚠️ ReceiveMessage: thiếu roomId, bỏ qua tin nhắn', raw);
        return;
      }

      // Tin do chính user gửi được đối chiếu chính xác bằng clientMessageId.
      if (message.senderId === currentUserId && message.clientMessageId) {
        updateMessageStatus(
          message.roomId,
          message.clientMessageId,
          message,
          'accepted'
        );
      } else {
        // Tin của người khác: kiểm tra xem có đang mở phòng này không
        const { activeRoomId, incrementUnread } = useChatStore.getState();
        if (message.roomId !== activeRoomId) {
          incrementUnread(message.roomId);
        }
      }

      // Fix #1+#2+#4: Luôn update metadata để phòng lên đầu danh sách (cả khi A gửi lẫn khi nhận)
      updateRoomMetadata(message.roomId, buildMessagePreview(message), message.createdAt);

      // addMessage chống trùng theo cả messageId và clientMessageId.
      addMessage(message.roomId, message);
    });

    newConnection.on('MessagePersisted', (payload: MessagePersistedDto) => {
      const messages = useChatStore.getState().messages[payload.roomId] ?? [];
      const optimisticMessage = messages.find(
        (message) =>
          message.clientMessageId === payload.clientMessageId ||
          message.id === payload.messageId
      );

      if (!optimisticMessage) {
        return;
      }

      updateMessageStatus(payload.roomId, payload.clientMessageId, {
        ...optimisticMessage,
        id: payload.messageId,
        clientMessageId: payload.clientMessageId,
        status: payload.status,
      }, 'persisted');
    });

    newConnection.on('MessagePersistenceFailed', (payload: MessagePersistenceFailedDto) => {
      const messages = useChatStore.getState().messages[payload.roomId] ?? [];
      const optimisticMessage = messages.find(
        (message) =>
          message.clientMessageId === payload.clientMessageId ||
          message.id === payload.messageId
      );

      if (!optimisticMessage) {
        return;
      }

      updateMessageStatus(payload.roomId, payload.clientMessageId, {
        ...optimisticMessage,
        id: payload.messageId,
        clientMessageId: payload.clientMessageId,
        status: 'Failed',
      }, 'permanent-failed');
    });

    newConnection.on('MessageRetracted', (payload: MessageRetractedDto) => {
      const messages = useChatStore.getState().messages[payload.roomId] ?? [];
      const existingMessage = messages.find(
        (message) =>
          message.clientMessageId === payload.clientMessageId ||
          message.id === payload.messageId
      );

      if (existingMessage?.senderId === currentUserId) {
        return;
      }

      retractMessage(
        payload.roomId,
        payload.messageId,
        payload.clientMessageId
      );
    });

    newConnection.on('MessageEdited', (payload: MessageEditedDto) => {
      editStoredMessage(payload);
    });

    newConnection.on('MessageDeleted', (payload: MessageDeletedDto) => {
      markMessageDeleted(payload);
    });

    newConnection.on('MessageReactionUpdated', (payload: MessageReactionUpdatedDto) => {
      applyReactionUpdate(payload);
    });

    newConnection.on('MessagePinned', (payload: MessagePinnedDto) => {
      applyMessagePinned(payload);
    });

    newConnection.on('MessageUnpinned', (payload: MessageUnpinnedDto) => {
      applyMessageUnpinned(payload);
    });

    newConnection.on('ReceiveTyping', (userId: string, roomId: string) => {
      if (roomId) setTyping(roomId, userId, true);
    });

    newConnection.on('ReceiveTypingStopped', (userId: string, roomId: string) => {
      if (roomId) setTyping(roomId, userId, false);
    });

    newConnection.on('UserIsOnline', (userId: string) => {
      useUserRelationshipsStore.getState().setUserOnline(userId);
    });

    newConnection.on('UserIsOffline', (payload: string | { userId?: string; lastSeenAt?: string | null }) => {
      if (typeof payload === 'string') {
        useUserRelationshipsStore.getState().setUserOffline(payload);
        return;
      }

      if (payload?.userId) {
        useUserRelationshipsStore.getState().setUserOffline(payload.userId, payload.lastSeenAt);
      }
    });

    /**
     * Nhận thông báo người khác đã đọc tin nhắn đến messageId nào trong roomId nào.
     * Cập nhật vào readReceipts store để UI có thể hiển thị avatar "Đã đọc".
     */
    newConnection.on('ReceiveReadReceipt', (userId: string, roomId: string, lastReadMessageId: string) => {
      console.log(`👀 User ${userId} đã đọc đến ${lastReadMessageId} trong phòng ${roomId}`);
      setReadReceipt(roomId, userId, lastReadMessageId);
    });

    // === NOTIFICATION LISTENERS ===

    newConnection.on('GroupRoomsUpdated', (groupId: string) => {
      console.log('📢 Tín hiệu: Danh sách phòng trong Group thay đổi:', groupId);
      useNotificationStore.getState().triggerRoomRefetch(groupId);
    });

    newConnection.on('YouWereKicked', (groupId: string, groupName: string) => {
      console.log('⛔ Tín hiệu: Bạn đã bị kick khỏi:', groupName);
      useNotificationStore.getState().handleKicked(groupId, groupName);
    });

    newConnection.on('GroupDeleted', (groupId: string, groupName: string) => {
      console.log('⚠️ Tín hiệu: Server đã bị giải tán:', groupName);
      useNotificationStore.getState().handleGroupDeleted(groupId, groupName);
    });

    newConnection.on('MemberRoleChanged', (_groupId: string, _userId: string, newRole: string) => {
      console.log('🎖️ Tín hiệu: Vai trò của bạn đã thay đổi thành:', newRole);
      // Có thể dùng để trigger refetch quyền hạn nếu cần
    });

    // === VOICE CALL LISTENERS ===

    newConnection.on('VoiceCallIncoming', (payload: VoiceCallIncomingDto) => {
      console.log('📞 VoiceCallIncoming:', payload);
      useVoiceStore.getState().setIncomingCall(payload);
    });

    newConnection.on('VoiceCallAccepted', (payload: VoiceCallStatusChangedDto) => {
      console.log('📞 VoiceCallAccepted:', payload);
      useVoiceStore.getState().setCallAccepted(payload);
    });

    newConnection.on('VoiceCallDeclined', (payload: VoiceCallStatusChangedDto) => {
      console.log('📞 VoiceCallDeclined:', payload);
      useVoiceStore.getState().setCallDeclined(payload);
      cleanupActiveDirectCallIfMatches(payload, 'VoiceCallDeclined');
    });

    newConnection.on('VoiceCallEnded', (payload: VoiceCallStatusChangedDto) => {
      console.log('📞 VoiceCallEnded:', payload);
      useVoiceStore.getState().setCallEnded(payload);

      cleanupActiveDirectCallIfMatches(payload, 'VoiceCallEnded');
    });

    // 3. Khởi động kết nối
    const startConnection = async () => {
      try {
        await newConnection.start();
        setIsConnected(true);
        await newConnection.invoke('Heartbeat');
        startPresenceHeartbeat();
        console.log('🔌 Đã kết nối SignalR thành công!');

        // [User-based Routing] Auto-join loop đã được vô hiệu hóa.
        // Lý do: MessagePersistenceWorker đã dùng Clients.Users(memberIds) thay vì Clients.Group().
        // Server tự route tin nhắn đến đúng user qua NameIdentifier claim → không cần client join group.
        // Lợi ích: Loại bỏ N sequential round-trips (N = số phòng) mỗi lần kết nối.
        // Typing (OthersInGroup) vẫn hoạt động vì joinRoom() được gọi explicit khi user mở từng phòng.
        //
        // try {
        //   const authClient = createAuthClient(accessToken);
        //   const res = await authClient.get<{ id: string }[]>('/api/v1/rooms/my-rooms');
        //   for (const room of res.data) {
        //     await newConnection.invoke('JoinRoom', room.id);
        //   }
        //   console.log(`📌 Auto-joined ${res.data.length} rooms`);
        // } catch (joinErr) {
        //   console.warn('⚠️ Không thể auto-join rooms:', joinErr);
        // }
      } catch (err: any) {
        if (err.message && err.message.includes('stopped during negotiation')) {
          console.warn('⚠️ SignalR: Kết nối bị hủy do component unmount (thường gặp trong React StrictMode).');
        } else {
          console.error('❌ Lỗi kết nối SignalR:', err);
        }
      }
    };

    startConnection();

    // 4. Cleanup khi unmount
    return () => {
      stopPresenceHeartbeat();
      newConnection.stop();
      setIsConnected(false);
    };
  }, [accessToken]); // Chỉ phụ thuộc accessToken, các store actions là stable reference

  // Expose các method gửi lệnh lên Backend (dùng useCallback để tránh re-render con)
  const joinRoom = useCallback(async (roomId: string) => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      await connectionRef.current.invoke('JoinRoom', roomId);
      console.log(`📌 Đã JoinRoom SignalR Group: ${roomId}`);
    }
  }, []);

  /**
   * Gửi tin nhắn lên Hub.
   * @param roomId - ID phòng
   * @param content - Nội dung tin nhắn
   * @param clientMessageId - UUID ổn định cho một lần gửi logic
   */
  const sendMessage = useCallback(async (
    roomId: string,
    content: string,
    clientMessageId: string,
    mediaIds: string[] = []
  ): Promise<MessageAcceptedResult> => {
    if (connectionRef.current?.state !== signalR.HubConnectionState.Connected) {
      throw new Error('Kết nối thời gian thực chưa sẵn sàng.');
    }

    return connectionRef.current.invoke<MessageAcceptedResult>('SendMessage', {
      roomId,
      content,
      clientMessageId,
      mediaIds,
    });
  }, []);

  const sendTyping = useCallback(async (roomId: string) => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      await connectionRef.current.invoke('TypingStarted', roomId);
    }
  }, []);

  const stopTyping = useCallback(async (roomId: string) => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      await connectionRef.current.invoke('TypingStopped', roomId);
    }
  }, []);

  const markAsRead = useCallback(async (
    roomId: string,
    messageId: string
  ) => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      try {
        await connectionRef.current.invoke(
          'MarkAsRead',
          roomId,
          messageId
        );
        // Đồng bộ mốc đọc của chính mình vào store ngay lập tức
        useChatStore.getState().setInitialLastReadIds({
          ...useChatStore.getState().myLastReadMessageIds,
          [roomId]: messageId
        });
      } catch (err) {
        console.error('Lỗi MarkAsRead SignalR:', err);
      }
    }
  }, []);

  return {
    isConnected,
    joinRoom,
    sendMessage,
    sendTyping,
    stopTyping,
    markAsRead
  };
};

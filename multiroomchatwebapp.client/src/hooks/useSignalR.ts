import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import { createAuthClient } from '../api/apiClient';
import { useChatStore } from '../store/useChatStore';
import type { MessageDto } from '../types/chat';

/**
 * Hook quản lý kết nối SignalR (Singleton lifecycle gắn với MainLayout).
 *
 * @remarks
 * Luồng xử lý:
 * 1. Khởi tạo kết nối khi có accessToken.
 * 2. Đăng ký các sự kiện lắng nghe: ReceiveMessage, ReceiveTyping, ReceiveReadReceipt, MessageStatusUpdated.
 * 3. Expose các method gửi lệnh lên Backend.
 * 4. Cleanup khi unmount.
 *
 * Lưu ý quan trọng:
 * - Backend (C#) serialize Entity dùng camelCase convention, nên field name
 *   có thể là `roomId` hoặc `room_id` tùy cấu hình. Hook này map lại cho đúng.
 * - Khi ReceiveMessage, cần kiểm tra xem tin nhắn có phải do chính user này gửi không.
 *   Nếu đúng → thay thế tin tạm (Optimistic) bằng tin thật. Nếu không → thêm mới.
 * - MessageStatusUpdated: Chỉ người gửi nhận được event này từ Worker để update Sent status.
 */
export const useSignalR = () => {
  const { accessToken, user } = useAuth();
  const currentUserId = user?.userId;
  const [isConnected, setIsConnected] = useState(false);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  // Lấy actions từ Zustand Store (chỉ lấy function, không gây re-render)
  const addMessage = useChatStore((state) => state.addMessage);
  const updateMessageStatus = useChatStore((state) => state.updateMessageStatus);
  const setTyping = useChatStore((state) => state.setTyping);
  const setReadReceipt = useChatStore((state) => state.setReadReceipt);
  const updateRoomMetadata = useChatStore((state) => state.updateRoomMetadata);

  useEffect(() => {
    if (!accessToken) return;

    // 1. Khởi tạo kết nối
    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl('https://localhost:7222/hub/chat', {
        accessTokenFactory: () => accessToken,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    connectionRef.current = newConnection;

    // 2. Đăng ký các sự kiện lắng nghe từ Backend

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    newConnection.on('ReceiveMessage', (raw: any) => {
      console.log('📬 Raw ReceiveMessage từ Backend:', raw);

      // Map field name từ C# Entity (camelCase convention) sang MessageDto Frontend
      const message: MessageDto = {
        id: raw.id ?? raw.Id ?? '',
        roomId: raw.roomId ?? raw.RoomId ?? raw.room_id ?? '',
        senderId: raw.senderId ?? raw.SenderId ?? raw.sender_id ?? '',
        type: raw.type ?? raw.Type ?? 'Text',
        content: raw.content ?? raw.Content ?? '',
        status: raw.status ?? raw.Status ?? 'Sent',
        createdAt: raw.createdAt ?? raw.CreatedAt ?? raw.created_at ?? new Date().toISOString(),
      };

      if (!message.roomId) {
        console.warn('⚠️ ReceiveMessage: thiếu roomId, bỏ qua tin nhắn', raw);
        return;
      }

      // Nếu chính user này gửi → thay thế tin tạm (Optimistic) bằng tin thật
      // Lưu ý: MessageStatusUpdated sẽ làm việc này chính xác hơn (dùng tempId).
      // ReceiveMessage vẫn giữ logic dự phòng này cho trường hợp không có TempId (API test).
      if (message.senderId === currentUserId) {
        const roomMsgs = useChatStore.getState().messages[message.roomId] || [];
        const tempMsg = roomMsgs.find(
          (m) => m.id.startsWith('temp-') && m.content === message.content
        );

        if (tempMsg) {
          // Dự phòng: thay thế nếu chưa được MessageStatusUpdated xử lý
          updateMessageStatus(message.roomId, tempMsg.id, message);
          console.log(`✅ [ReceiveMessage fallback] Đã thay thế tin tạm ${tempMsg.id} → ${message.id}`);
          // Không return sớm: vẫn cần chạy updateRoomMetadata bên dưới
        }
      } else {
        // Tin của người khác: kiểm tra xem có đang mở phòng này không
        const { activeRoomId, incrementUnread } = useChatStore.getState();
        if (message.roomId !== activeRoomId) {
          incrementUnread(message.roomId);
        }
      }

      // Fix #1+#2+#4: Luôn update metadata để phòng lên đầu danh sách (cả khi A gửi lẫn khi nhận)
      updateRoomMetadata(message.roomId, message.content, message.createdAt);

      // Thêm mới tin nhắn vào store (addMessage tự check duplicate nếu tempId đã được thế)
      addMessage(message.roomId, message);
    });

    /**
     * Worker callback: Tin nhắn đã được lưu MongoDB thành công.
     * Chỉ người GỬI nhận được event này.
     * Dùng tempId để tìm đúng tin tạm trong store và thay bằng finalMessage (với status Sent).
     */
    newConnection.on('MessageStatusUpdated', (tempId: string, finalMessageId: string, status: string) => {
      console.log(`🔔 MessageStatusUpdated: tempId=${tempId} → finalId=${finalMessageId}, status=${status}`);

      // Tìm tin tạm trong toàn bộ store (không biết roomId ở đây, nên phải scan)
      const allMessages = useChatStore.getState().messages;
      for (const [roomId, msgs] of Object.entries(allMessages)) {
        const tempMsg = msgs.find((m) => m.id === tempId);
        if (tempMsg) {
          // Tạo finalMessage từ tin tạm, chỉ thay id và status
          const finalMessage: MessageDto = {
            ...tempMsg,
            id: finalMessageId,
            status: status as MessageDto['status'],
          };
          updateMessageStatus(roomId, tempId, finalMessage);
          console.log(`✅ MessageStatusUpdated: Room=${roomId}, ${tempId} → ${finalMessageId} (${status})`);
          break;
        }
      }
    });

    newConnection.on('ReceiveTyping', (userId: string, roomId: string) => {
      if (roomId) setTyping(roomId, userId, true);
    });

    newConnection.on('ReceiveTypingStopped', (userId: string, roomId: string) => {
      if (roomId) setTyping(roomId, userId, false);
    });

    newConnection.on('UserIsOnline', (userId: string) => {
      console.log('🟢 User online:', userId);
    });

    newConnection.on('UserIsOffline', (userId: string) => {
      console.log('⚪ User offline:', userId);
    });

    /**
     * Nhận thông báo người khác đã đọc tin nhắn đến messageId nào trong roomId nào.
     * Cập nhật vào readReceipts store để UI có thể hiển thị avatar "Đã đọc".
     */
    newConnection.on('ReceiveReadReceipt', (userId: string, roomId: string, lastReadMessageId: string) => {
      console.log(`👀 User ${userId} đã đọc đến ${lastReadMessageId} trong phòng ${roomId}`);
      setReadReceipt(roomId, userId, lastReadMessageId);
    });

    // 3. Khởi động kết nối
    const startConnection = async () => {
      try {
        await newConnection.start();
        setIsConnected(true);
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
   * @param tempId - ID tạm (temp-xxx) để Worker callback đúng tin tạm sau khi lưu xong
   */
  const sendMessage = useCallback(async (roomId: string, content: string, tempId: string) => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      await connectionRef.current.invoke('SendMessage', roomId, content, tempId);
    }
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

  const markAsRead = useCallback(async (roomId: string, messageId: string) => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      try {
        await connectionRef.current.invoke('MarkAsRead', roomId, messageId);
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

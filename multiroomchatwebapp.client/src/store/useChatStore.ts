import { create } from 'zustand';
import type { MessageDto } from '../types/chat';

interface ChatState {
  // Map lưu trữ tin nhắn theo từng RoomId: Record<RoomId, MessageDto[]>
  messages: Record<string, MessageDto[]>;
  
  // Trạng thái typing của các room: Record<RoomId, Set<UserId>>
  typingUsers: Record<string, Set<string>>;

  // Cờ báo hiệu phòng nào còn tin nhắn cũ chưa load: Record<RoomId, boolean>
  hasMore: Record<string, boolean>;

  // Quản lý tin nhắn chưa đọc: Record<RoomId, number>
  unreadCount: Record<string, number>;
  activeRoomId: string | null;

  /**
   * Mốc tin nhắn cuối cùng CHÍNH USER NÀY đã đọc trong mỗi phòng.
   * Dùng để xác định vị trí vạch "Tin nhắn mới" (Unread Divider).
   * Cấu trúc: Record<roomId, messageId>
   */
  myLastReadMessageIds: Record<string, string>;

  /**
   * Lưu trạng thái "Đã xem" của người khác trong mỗi phòng.
   * Cấu trúc: Record<roomId, Record<userId, lastReadMessageId>>
   * Ví dụ: { "room-abc": { "user-xyz": "mongo-msg-id-999" } }
   * Dùng để hiển thị avatar nhỏ dưới tin nhắn cuối mà người kia đã đọc đến.
   */
  readReceipts: Record<string, Record<string, string>>;

  /**
   * Metadata sort phòng: lưu nội dung và timestamp tin nhắn mới nhất theo roomId.
   * Dùng để sort danh sách phòng real-time mà không cần re-fetch API mỗi khi có tin mới.
   * Cấu trúc: Record<roomId, { lastMessageContent, lastMessageTimestamp }>
   */
  roomMetadata: Record<string, { lastMessageContent: string; lastMessageTimestamp: string }>;

  // Actions
  addMessage: (roomId: string, message: MessageDto) => void;
  setMessages: (roomId: string, messages: MessageDto[]) => void;
  prependMessages: (roomId: string, messages: MessageDto[]) => void;
  /**
   * Cập nhật 1 tin nhắn tạm (tempId → finalMessage) sau khi Worker xác nhận lưu thành công.
   * Cũng dùng để update trạng thái Failed nếu gửi lỗi.
   */
  updateMessageStatus: (roomId: string, tempId: string, finalMessage: MessageDto) => void;
  
  setTyping: (roomId: string, userId: string, isTyping: boolean) => void;
  setHasMore: (roomId: string, hasMore: boolean) => void;
  
  // Actions unread
  setActiveRoomId: (roomId: string | null) => void;
  setInitialUnreadCounts: (counts: Record<string, number>) => void;
  setInitialLastReadIds: (ids: Record<string, string>) => void;
  incrementUnread: (roomId: string) => void;
  clearUnread: (roomId: string) => void;

  /**
   * Cập nhật ReadReceipt khi nhận ReceiveReadReceipt từ SignalR.
   * Ghi đè lastReadMessageId của userId trong phòng roomId.
   */
  setReadReceipt: (roomId: string, userId: string, lastReadMessageId: string) => void;

  /**
   * Cập nhật metadata lastMessage của phòng khi có tin nhắn mới (nhận hoặc gửi).
   * Trigger sort danh sách phòng trong RoomListColumn.
   */
  updateRoomMetadata: (roomId: string, content: string, timestamp: string) => void;

  /**
   * Cắt bớt tin nhắn của phòng xuống còn `keepCount` tin MỚI NHẤT.
   * Gọi khi user thoát phòng để giải phóng bộ nhớ (Discord pattern).
   * Reset `hasMore = true` để cho phép lazy load lại lịch sử từ điểm mới.
   */
  trimRoom: (roomId: string, keepCount?: number) => void;
}

export const useChatStore = create<ChatState>((set) => ({
  messages: {},
  typingUsers: {},
  hasMore: {},
  unreadCount: {},
  activeRoomId: null,
  readReceipts: {},
  myLastReadMessageIds: {},
  roomMetadata: {},

  addMessage: (roomId, message) =>
    set((state) => {
      const roomMsgs = state.messages[roomId] || [];
      // Tránh duplicate nếu nhận lại chính tin nhắn mình vừa gửi
      if (roomMsgs.some((m) => m.id === message.id)) return state;

      return {
        messages: {
          ...state.messages,
          [roomId]: [...roomMsgs, message],
        },
      };
    }),

  setMessages: (roomId, messages) =>
    set((state) => ({
      messages: {
        ...state.messages,
        [roomId]: messages,
      },
    })),

  prependMessages: (roomId, newOldMessages) =>
    set((state) => {
      const currentMsgs = state.messages[roomId] || [];
      // Tránh prepend trùng tin nhắn nếu lỡ bấm 2 lần
      const existingIds = new Set(currentMsgs.map(m => m.id));
      const filteredOldMsgs = newOldMessages.filter(m => !existingIds.has(m.id));
      
      return {
        messages: {
          ...state.messages,
          [roomId]: [...filteredOldMsgs, ...currentMsgs],
        },
      };
    }),

  updateMessageStatus: (roomId, tempId, finalMessage) =>
    set((state) => {
      const roomMsgs = state.messages[roomId] || [];
      // Cập nhật mốc đọc của chính mình khi tin được confirm với ID thật
      // Giúp tránh vạch Divider sai khi chuyển phòng rồi quay lại
      const currentMoc = state.myLastReadMessageIds[roomId];
      const shouldUpdateMoc = finalMessage.id && !finalMessage.id.startsWith('temp-') &&
        (!currentMoc || finalMessage.id > currentMoc);
      return {
        messages: {
          ...state.messages,
          [roomId]: roomMsgs.map((m) => (m.id === tempId ? finalMessage : m)),
        },
        ...(shouldUpdateMoc && {
          myLastReadMessageIds: {
            ...state.myLastReadMessageIds,
            [roomId]: finalMessage.id,
          }
        }),
      };
    }),

  setTyping: (roomId, userId, isTyping) =>
    set((state) => {
      const roomTyping = new Set(state.typingUsers[roomId] || new Set());
      if (isTyping) {
        roomTyping.add(userId);
      } else {
        roomTyping.delete(userId);
      }

      return {
        typingUsers: {
          ...state.typingUsers,
          [roomId]: roomTyping,
        },
      };
    }),

  setHasMore: (roomId, hasMore) =>
    set((state) => ({
      hasMore: {
        ...state.hasMore,
        [roomId]: hasMore,
      },
    })),

  setActiveRoomId: (roomId) => set({ activeRoomId: roomId }),

  setInitialUnreadCounts: (counts) =>
    set((state) => {
      // Chỉ gán giá trị khởi tạo nếu trong store chưa có (để không ghi đè số đếm real-time)
      const merged = { ...counts, ...state.unreadCount };

      // [FIX] So sánh shallow: nếu data merge giống hệt state cũ → trả về state cũ
      // để Zustand KHÔNG trigger re-render thừa (tránh vòng lặp vô hạn)
      const oldKeys = Object.keys(state.unreadCount);
      const newKeys = Object.keys(merged);
      if (oldKeys.length === newKeys.length && oldKeys.every(k => state.unreadCount[k] === merged[k])) {
        return state; // Không thay đổi gì → Zustand bỏ qua
      }

      return { unreadCount: merged };
    }),

  setInitialLastReadIds: (ids) => set({ myLastReadMessageIds: ids }),

  incrementUnread: (roomId) =>
    set((state) => ({
      unreadCount: {
        ...state.unreadCount,
        [roomId]: (state.unreadCount[roomId] || 0) + 1,
      },
    })),

  clearUnread: (roomId) =>
    set((state) => {
      const newUnread = { ...state.unreadCount };
      delete newUnread[roomId];
      return { unreadCount: newUnread };
    }),

  setReadReceipt: (roomId, userId, lastReadMessageId) =>
    set((state) => ({
      readReceipts: {
        ...state.readReceipts,
        [roomId]: {
          ...(state.readReceipts[roomId] || {}),
          [userId]: lastReadMessageId,
        },
      },
    })),

  // Cập nhật metadata lastMessage để sort danh sách phòng real-time (không cần re-fetch API)
  updateRoomMetadata: (roomId, content, timestamp) =>
    set((state) => ({
      roomMetadata: {
        ...state.roomMetadata,
        [roomId]: { lastMessageContent: content, lastMessageTimestamp: timestamp },
      },
    })),

  // Giữ bộ nhớ gọn: cắt xuống keepCount tin mới nhất, reset hasMore (Discord pattern)
  trimRoom: (roomId, keepCount = 50) =>
    set((state) => {
      const roomMsgs = state.messages[roomId];
      // Không làm gì nếu không có data hoặc số tin ít hơn ngưỡng
      if (!roomMsgs || roomMsgs.length <= keepCount) return state;
      return {
        messages: {
          ...state.messages,
          // slice(-50) = giữ 50 phần tử cuối (mới nhất)
          [roomId]: roomMsgs.slice(-keepCount),
        },
        hasMore: {
          ...state.hasMore,
          // Reset = true vì đã xóa tin cũ, có thể lazy load lại
          [roomId]: true,
        },
      };
    }),
}));

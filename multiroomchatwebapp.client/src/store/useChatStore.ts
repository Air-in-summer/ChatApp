import { create } from 'zustand';
import type { MessageDto, MessageStatus } from '../types/chat';
import { buildMessagePreview } from '../utils/chatMessagePreview';

type MessageStatusTransition =
  | 'merge'
  | 'accepted'
  | 'persisted'
  | 'retrying'
  | 'rejected'
  | 'permanent-failed';

const MESSAGE_STATUS_PRIORITY: Record<MessageStatus, number> = {
  Sending: 1,
  Accepted: 2,
  Failed: 3,
  Sent: 4,
  Delivered: 5,
  Read: 6,
};

const resolveMessageStatus = (
  currentStatus: MessageStatus | undefined,
  nextStatus: MessageStatus,
  transition: MessageStatusTransition
): MessageStatus => {
  if (!currentStatus) {
    return nextStatus;
  }

  switch (transition) {
    case 'accepted':
      return ['Sent', 'Delivered', 'Read'].includes(currentStatus)
        ? currentStatus
        : 'Accepted';
    case 'persisted':
      return ['Delivered', 'Read'].includes(currentStatus)
        ? currentStatus
        : 'Sent';
    case 'retrying':
      return currentStatus === 'Failed' ? 'Sending' : currentStatus;
    case 'rejected':
      return currentStatus === 'Sending' ? 'Failed' : currentStatus;
    case 'permanent-failed':
      return 'Failed';
    case 'merge':
    default:
      if (nextStatus === 'Accepted') {
        return ['Sent', 'Delivered', 'Read'].includes(currentStatus)
          ? currentStatus
          : 'Accepted';
      }

      return MESSAGE_STATUS_PRIORITY[currentStatus] >
        MESSAGE_STATUS_PRIORITY[nextStatus]
        ? currentStatus
        : nextStatus;
  }
};

const getMessageTime = (message: MessageDto): number | null => {
  const timestamp = Date.parse(message.createdAt);
  return Number.isNaN(timestamp) ? null : timestamp;
};

const sortMessagesChronologically = (messages: MessageDto[]): MessageDto[] =>
  messages
    .map((message, index) => ({ message, index }))
    .sort((left, right) => {
      const leftTime = getMessageTime(left.message);
      const rightTime = getMessageTime(right.message);
      const leftHasTime = leftTime !== null;
      const rightHasTime = rightTime !== null;

      if (leftHasTime && rightHasTime && leftTime !== rightTime) {
        return leftTime - rightTime;
      }

      if (leftHasTime !== rightHasTime) {
        return leftHasTime ? -1 : 1;
      }

      if (
        left.message.id &&
        right.message.id &&
        left.message.id !== right.message.id
      ) {
        return left.message.id < right.message.id ? -1 : 1;
      }

      return left.index - right.index;
    })
    .map(({ message }) => message);

const isSameMessage = (left: MessageDto, right: MessageDto): boolean => {
  if (left.id && right.id && left.id === right.id) {
    return true;
  }

  const leftClientMessageId = left.clientMessageId?.trim();
  const rightClientMessageId = right.clientMessageId?.trim();
  return Boolean(leftClientMessageId) &&
    Boolean(rightClientMessageId) &&
    leftClientMessageId === rightClientMessageId;
};

const mergeAttachments = (
  currentMessage: MessageDto,
  nextMessage: MessageDto
) =>
  nextMessage.attachments?.map((attachment, attachmentIndex) => ({
    ...attachment,
    localPreviewUrl:
      attachment.localPreviewUrl ??
      currentMessage.attachments?.[attachmentIndex]?.localPreviewUrl,
  })) ?? currentMessage.attachments;

const mergeMessage = (
  currentMessage: MessageDto,
  nextMessage: MessageDto,
  transition: MessageStatusTransition = 'merge'
): MessageDto => ({
  ...currentMessage,
  ...nextMessage,
  status: resolveMessageStatus(
    currentMessage.status,
    nextMessage.status,
    transition
  ),
  attachments: mergeAttachments(currentMessage, nextMessage),
});

const upsertMessageList = (
  currentMessages: MessageDto[],
  nextMessage: MessageDto,
  transition: MessageStatusTransition = 'merge'
): MessageDto[] => {
  const existingIndex = currentMessages.findIndex(
    (currentMessage) => isSameMessage(currentMessage, nextMessage)
  );

  if (existingIndex < 0) {
    return sortMessagesChronologically([...currentMessages, nextMessage]);
  }

  return sortMessagesChronologically(
    currentMessages.map((currentMessage, index) =>
      index === existingIndex
        ? mergeMessage(currentMessage, nextMessage, transition)
        : currentMessage
    )
  );
};

const mergeMessageLists = (
  currentMessages: MessageDto[],
  incomingMessages: MessageDto[]
): MessageDto[] =>
  incomingMessages.reduce(
    (mergedMessages, message) =>
      upsertMessageList(mergedMessages, message, 'merge'),
    [...currentMessages]
  );

interface ChatState {
  // Map lưu trữ tin nhắn theo từng RoomId: Record<RoomId, MessageDto[]>
  messages: Record<string, MessageDto[]>;
  
  // Trạng thái typing của các room: Record<RoomId, Set<UserId>>
  typingUsers: Record<string, Set<string>>;

  // Cờ báo hiệu phòng nào còn tin nhắn cũ chưa load: Record<RoomId, boolean>
  hasMore: Record<string, boolean>;
  historyCursor: Record<string, string | null>;

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
   * Đối chiếu optimistic message bằng clientMessageId và cập nhật dữ liệu/trạng thái mới nhất.
   * Cũng dùng để update trạng thái Failed nếu gửi lỗi.
   */
  updateMessageStatus: (
    roomId: string,
    clientMessageId: string,
    finalMessage: MessageDto,
    transition?: MessageStatusTransition
  ) => void;
  retractMessage: (
    roomId: string,
    messageId: string,
    clientMessageId?: string | null
  ) => void;
  
  setTyping: (roomId: string, userId: string, isTyping: boolean) => void;
  setHasMore: (roomId: string, hasMore: boolean) => void;
  setHistoryCursor: (roomId: string, cursor: string | null) => void;
  
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
  historyCursor: {},
  unreadCount: {},
  activeRoomId: null,
  readReceipts: {},
  myLastReadMessageIds: {},
  roomMetadata: {},

  addMessage: (roomId, message) =>
    set((state) => {
      const roomMsgs = state.messages[roomId] || [];
      // Tránh duplicate nếu nhận lại chính tin nhắn mình vừa gửi
      return {
        messages: {
          ...state.messages,
          [roomId]: upsertMessageList(roomMsgs, message),
        },
      };
    }),

  setMessages: (roomId, messages) =>
    set((state) => {
      const currentMsgs = state.messages[roomId] || [];

      return {
        messages: {
          ...state.messages,
          [roomId]: mergeMessageLists(currentMsgs, messages),
        },
      };
    }),

  prependMessages: (roomId, newOldMessages) =>
    set((state) => {
      const currentMsgs = state.messages[roomId] || [];
      // Tránh prepend trùng tin nhắn nếu lỡ bấm 2 lần
      
      return {
        messages: {
          ...state.messages,
          [roomId]: mergeMessageLists(newOldMessages, currentMsgs),
        },
      };
    }),

  updateMessageStatus: (
    roomId,
    clientMessageId,
    finalMessage,
    transition = 'merge'
  ) =>
    set((state) => {
      const roomMsgs = state.messages[roomId] || [];
      const targetMessage = {
        ...finalMessage,
        clientMessageId: finalMessage.clientMessageId ?? clientMessageId,
      };
      const existingMessage = roomMsgs.find((message) =>
        isSameMessage(message, targetMessage)
      );
      const resolvedStatus = resolveMessageStatus(
        existingMessage?.status,
        targetMessage.status,
        transition
      );
      const resolvedMessage = {
        ...targetMessage,
        status: resolvedStatus,
      };

      // Cập nhật mốc đọc của chính mình khi tin được confirm với ID thật
      // Giúp tránh vạch Divider sai khi chuyển phòng rồi quay lại
      const currentMoc = state.myLastReadMessageIds[roomId];
      const shouldUpdateMoc =
        ['Sent', 'Delivered', 'Read'].includes(resolvedMessage.status) &&
        Boolean(resolvedMessage.id) &&
        (!currentMoc || resolvedMessage.id > currentMoc);
      return {
        messages: {
          ...state.messages,
          [roomId]: upsertMessageList(
            roomMsgs,
            resolvedMessage,
            transition
          ),
        },
        ...(shouldUpdateMoc && {
          myLastReadMessageIds: {
            ...state.myLastReadMessageIds,
            [roomId]: resolvedMessage.id,
          }
        }),
      };
    }),

  retractMessage: (roomId, messageId, clientMessageId) =>
    set((state) => {
      const roomMsgs = state.messages[roomId] || [];
      const nextMsgs = sortMessagesChronologically(
        roomMsgs.filter(
          (message) =>
            message.id !== messageId &&
            (
              !clientMessageId ||
              message.clientMessageId !== clientMessageId
            )
        )
      );

      if (nextMsgs.length === roomMsgs.length) {
        return state;
      }

      const nextRoomMetadata = { ...state.roomMetadata };
      const latestMessage = nextMsgs.at(-1);
      if (latestMessage) {
        nextRoomMetadata[roomId] = {
          lastMessageContent: buildMessagePreview(latestMessage),
          lastMessageTimestamp: latestMessage.createdAt,
        };
      } else {
        delete nextRoomMetadata[roomId];
      }

      return {
        messages: {
          ...state.messages,
          [roomId]: nextMsgs,
        },
        roomMetadata: nextRoomMetadata,
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

  setHistoryCursor: (roomId, cursor) =>
    set((state) => ({
      historyCursor: {
        ...state.historyCursor,
        [roomId]: cursor,
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

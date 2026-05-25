import { lazy, Suspense, useState, useRef, useEffect, useLayoutEffect } from 'react';
import toast from 'react-hot-toast';
import { useAuth } from '../../context/AuthContext';
import { createAuthClient } from '../../api/apiClient';
import { getGroupMembers, leaveGroup } from '../../api/groupApi';
import { useChatStore } from '../../store/useChatStore';
import { useUserRelationshipsStore } from '../../store/useUserRelationshipsStore';
import { useVoiceStore } from '../../store/useVoiceStore';
import type { ActiveChat, RoomDto, MessageDto, GetMessagesResponse } from '../../types/chat';
import type { GroupMemberDto, GroupRole } from '../../types/group';
import { DirectCallButton } from '../call/DirectCallButton';
import { AddMemberToRoomModal } from '../group/AddMemberToRoomModal';
import { UserActionMenu } from '../user/UserActionMenu';
import styles from './ChatColumn.module.css';

const VoiceRoomPanel = lazy(() =>
  import('./VoiceRoomPanel').then((module) => ({ default: module.VoiceRoomPanel }))
);

interface ChatColumnProps {
  activeChat: ActiveChat | null;
  /** Callback trả ngược về Layout để update Cột 2 (VD: đang ảo gõ enter -> thành room thật) */
  onChatEvolvedToReal: (realRoom: RoomDto) => void;
  // Các hàm từ useSignalR truyền xuống
  sendMessage: (roomId: string, content: string, tempId: string) => Promise<void>;
  sendTyping: (roomId: string) => Promise<void>;
  stopTyping: (roomId: string) => Promise<void>;
  markAsRead: (roomId: string, messageId: string) => Promise<void>;
  joinRoom: (roomId: string) => Promise<void>;
  /** Class CSS từ cha (Layout) để định hình cột */
  className?: string;
  onGroupLeft?: () => void;
}

export const ChatColumn = ({
  activeChat,
  onChatEvolvedToReal,
  sendMessage,
  sendTyping,
  stopTyping,
  markAsRead,
  joinRoom,
  className,
  onGroupLeft,
}: ChatColumnProps) => {
  const { accessToken, user } = useAuth();
  const userId = user?.userId;
  const activeSession = useVoiceStore((state) => state.activeSession);
  const voiceConnectionStatus = useVoiceStore((state) => state.connectionStatus);
  const blockedUsers = useUserRelationshipsStore(state => state.blockedUsers);

  const roomId = activeChat?.type === 'real' ? activeChat.room.id : null;
  const storeMessages = useChatStore(state => roomId ? state.messages[roomId] : undefined);
  const messages = storeMessages || [];

  const storeTypingUsers = useChatStore(state => roomId ? state.typingUsers[roomId] : undefined);
  const typingUsers = storeTypingUsers || new Set();

  const storeHasMore = useChatStore(state => roomId ? state.hasMore[roomId] : undefined);
  const hasMore = storeHasMore ?? true;
  const addMessage = useChatStore(state => state.addMessage);
  const setMessages = useChatStore(state => state.setMessages);
  const prependMessages = useChatStore(state => state.prependMessages);
  const setHasMore = useChatStore(state => state.setHasMore);
  const updateMessageStatus = useChatStore(state => state.updateMessageStatus);
  const trimRoom = useChatStore(state => state.trimRoom);

  // Lấy readReceipts của phòng hiện tại để biết người kia đã đọc đến tin nào
  const roomReadReceipts = useChatStore(state => roomId ? state.readReceipts[roomId] : undefined) || {};

  const [inputText, setInputText] = useState('');
  const [isSendingFirstMessage, setIsSendingFirstMessage] = useState(false);
  const [isLoadingInitial, setIsLoadingInitial] = useState(false);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const endOfMessagesRef = useRef<HTMLDivElement>(null);
  const messageListRef = useRef<HTMLDivElement>(null);
  const typingTimeoutRef = useRef<number | undefined>(undefined);

  // States cho tính năng Add Member
  const [currentUserRole, setCurrentUserRole] = useState<GroupRole | null>(null);
  const [groupMembers, setGroupMembers] = useState<GroupMemberDto[]>([]);
  const [isAddMemberModalOpen, setIsAddMemberModalOpen] = useState(false);
  const [dismissedBlockedGroupWarningByRoom, setDismissedBlockedGroupWarningByRoom] = useState<Record<string, boolean>>({});

  // [CHỐT MỐC UNREAD] Dùng Ref để "chụp ảnh" mốc đọc ngay khi click vào phòng.
  // Ref này sẽ KHÔNG thay đổi trong suốt lần ghé thăm này, giúp vạch Divider không bị mất khi markAsRead chạy.
  const initialLastReadIdRef = useRef<string | undefined>(undefined);
  const lastScrolledRoomIdRef = useRef<string | null>(null);
  const prevMsgCountRef = useRef<number>(0);

  // Logic "Capture" mốc ngay trong chu kỳ render khi roomId thay đổi
  if (lastScrolledRoomIdRef.current !== roomId) {
    initialLastReadIdRef.current = useChatStore.getState().myLastReadMessageIds[roomId || ''];
    prevMsgCountRef.current = 0; // Reset số lượng tin để không tự cuộn đáy khi vào phòng mới
    console.log(`[Capture Mốc] Room: ${roomId}, Mốc: ${initialLastReadIdRef.current}`);
  }

  // Hook: Xoá màn hình khi chuyển sang Chat của người khác
  useEffect(() => {
    setInputText('');
  }, [activeChat]);

  // Hook: JoinRoom SignalR Group khi chọn phòng — BẮT BUỘC để nhận broadcast
  useEffect(() => {
    if (roomId) {
      joinRoom(roomId).catch(e => console.error('Lỗi JoinRoom:', e));
    }
  }, [roomId, joinRoom]);

  // Hook: Lấy quyền GroupRole nếu phòng này là Private trong Group
  useEffect(() => {
    if (activeChat?.type === 'real' && activeChat.room.groupId && accessToken) {
      const fetchMembers = async () => {
        try {
          const members = await getGroupMembers(accessToken, activeChat.room.groupId!);
          setGroupMembers(members);

          if (activeChat.room.isPrivate && userId) {
            const me = members.find(m => m.profile.id === userId);
            if (me) setCurrentUserRole(me.role);
          } else {
            setCurrentUserRole(null);
          }
        } catch (error) {
          console.error('Failed to fetch group members:', error);
          setGroupMembers([]);
        }
      };
      fetchMembers();
    } else {
      setCurrentUserRole(null);
      setGroupMembers([]);
    }
  }, [activeChat, accessToken, userId]);

  // Ref giữ roomId trước đó để gọi trimRoom khi user chuyển phòng
  const prevRoomIdRef = useRef<string | null>(null);

  // Hook: Trim phòng cũ khi chuyển sang phòng mới (Discord pattern)
  // Giữ lại 50 tin mới nhất để dùng lại làm cache khi quay lại, xóa tin cũ để tiết kiệm RAM
  useEffect(() => {
    const prevRoomId = prevRoomIdRef.current;
    if (prevRoomId && prevRoomId !== roomId) {
      console.log(`[Trim] Dọn dẹp phòng cũ: ${prevRoomId}`);
      trimRoom(prevRoomId);
    }
    prevRoomIdRef.current = roomId;
  }, [roomId, trimRoom]);

  // [DEBUG LOG] Giám sát mốc đọc
  useEffect(() => {
    if (roomId) {
      console.log(`[Visit Info] Room: ${roomId}, UI-Locked Mốc: ${initialLastReadIdRef.current}`);
    }
  }, [roomId]);

  // Hook: Lấy lịch sử tin nhắn khi mở phòng
  useEffect(() => {
    if (!roomId || !accessToken) return;

    const controller = new AbortController();

    // Kiểm tra cache
    const existingMsgs = useChatStore.getState().messages[roomId] || [];
    if (existingMsgs.length > 0) {
      console.log(`[Cache Hit] Phòng ${roomId} đã có ${existingMsgs.length} tin nhắn. KHÔNG gọi API.`);
      setIsLoadingInitial(false);
      return;
    }

    const fetchInitialMessages = async () => {
      console.log(`[API Fetch] Bắt đầu tải tin nhắn cho phòng ${roomId}...`);
      setIsLoadingInitial(true);
      try {
        const authClient = createAuthClient(accessToken);
        const res = await authClient.get<GetMessagesResponse>(
          `/api/v1/chat/rooms/${roomId}/messages`,
          { signal: controller.signal }
        );

        const dbMessages = res.data.data;
        const currentMsgs = useChatStore.getState().messages[roomId] || [];
        const dbIds = new Set(dbMessages.map(m => m.id));
        const notInDb = currentMsgs.filter(m => !dbIds.has(m.id));

        setMessages(roomId, [...dbMessages, ...notInDb]);
        setHasMore(roomId, res.data.hasMore);
        console.log(`[API Success] Đã nạp ${dbMessages.length} tin nhắn cho phòng ${roomId}`);
      } catch (err: any) {
        if (err.name !== 'CanceledError' && err.name !== 'AbortError') {
          console.error("Lỗi khi lấy tin nhắn:", err);
        }
      } finally {
        setIsLoadingInitial(false);
      }
    };

    fetchInitialMessages();

    return () => {
      controller.abort();
    };
  }, [roomId, accessToken]); // Cố tình không đưa messages vào đây để tránh re-fetch

  /**
   * [CORE LOGIC] Xử lý Cuộn (Scroll Management)
   * Phải chạy đồng bộ TRƯỚC KHI paint để tránh giật hình.
   */
  useLayoutEffect(() => {
    // Không cuộn nếu đang load tin đầu tiên hoặc chưa có tin nhắn
    if (isLoadingInitial || !roomId || messages.length === 0) return;

    // Chỉ thực hiện cuộn "nhảy" (jump) trong lần đầu tiên Render phòng này thành công
    if (lastScrolledRoomIdRef.current !== roomId) {
      const listElem = messageListRef.current;
      if (!listElem) return;

      const divider = document.getElementById('unread-divider');
      if (divider) {
        console.log(`[Scroll] Tìm thấy vạch Unread -> Cuộn đến Divider`);
        divider.scrollIntoView({ behavior: 'auto', block: 'center' });
      } else {
        console.log(`[Scroll] Không có tin mới -> Cuộn xuống Đáy (Forced)`);
        // Cưỡng bức cuộn xuống đáy bằng cách gán thẳng giá trị (ổn định hơn scrollIntoView)
        listElem.scrollTop = listElem.scrollHeight;
      }

      // Đánh dấu đã cuộn cho lần ghé thăm này
      lastScrolledRoomIdRef.current = roomId;
    }
  }, [roomId, messages.length, isLoadingInitial]);

  // Hook: Tự động cuộn xuống khi có tin nhắn mới (chỉ smooth khi đang ở đáy)
  useEffect(() => {
    // Điều kiện cuộn đáy tự động:
    // 1. Không phải đang lazy load (isLoadingMore)
    // 2. Không phải lần đầu tiên nạp tin nhắn (số lượng tin nhắn cũ phải > 0)
    // 3. Số lượng tin nhắn hiện tại phải lớn hơn số lượng trước đó (có tin mới)
    if (!isLoadingMore && prevMsgCountRef.current > 0 && messages.length > prevMsgCountRef.current) {
      console.log(`[Scroll] Có tin nhắn mới (${prevMsgCountRef.current} -> ${messages.length}) -> Cuộn xuống đáy.`);
      endOfMessagesRef.current?.scrollIntoView({ behavior: 'smooth' });
    }

    // Luôn cập nhật số lượng tin hiện tại vào Ref
    prevMsgCountRef.current = messages.length;
  }, [messages.length, typingUsers.size, isLoadingMore]);

  // Hook: Mark as Read khi mở phòng hoặc khi có tin nhắn mới VÀ trình duyệt đang active
  useEffect(() => {
    if (roomId && messages.length > 0) {
      const lastMsg = messages[messages.length - 1];

      // Chỉ đánh dấu đã đọc nếu tin nhắn cuối không phải của mình VÀ tab đang được focus
      if (lastMsg.senderId !== userId && document.visibilityState === 'visible') {
        markAsRead(roomId, lastMsg.id).catch(e => console.error(e));
      }
    }
  }, [roomId, messages.length, markAsRead, userId]);

  // Hook: Mark as read ngay khi user quay lại tab (nếu có tin nhắn chưa đọc)
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible' && roomId && messages.length > 0) {
        const lastMsg = messages[messages.length - 1];
        if (lastMsg.senderId !== userId) {
          markAsRead(roomId, lastMsg.id).catch(e => console.error(e));
        }
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange);
    };
  }, [roomId, messages, markAsRead, userId]);

  // Hàm: Lazy Load khi cuộn lên đỉnh
  const handleScroll = async (e: React.UIEvent<HTMLDivElement>) => {
    if (!roomId || !accessToken || isLoadingMore || !hasMore) return;

    const target = e.currentTarget;
    // Khi cuộn lên sát đỉnh (sai số 5px cho mượt)
    if (target.scrollTop <= 5) {
      const firstMessageId = messages[0]?.id;
      if (!firstMessageId) return;

      setIsLoadingMore(true);
      // Ghi nhớ vị trí cuộn hiện tại để giữ nguyên khung nhìn
      const previousScrollHeight = target.scrollHeight;

      try {
        const authClient = createAuthClient(accessToken);
        const res = await authClient.get<GetMessagesResponse>(`/api/v1/chat/rooms/${roomId}/messages?cursor=${firstMessageId}`);

        prependMessages(roomId, res.data.data);
        setHasMore(roomId, res.data.hasMore);

        // Khôi phục thanh cuộn
        requestAnimationFrame(() => {
          if (messageListRef.current) {
            const newScrollHeight = messageListRef.current.scrollHeight;
            messageListRef.current.scrollTop = newScrollHeight - previousScrollHeight;
          }
        });
      } catch (err) {
        console.error("Lỗi Lazy Load:", err);
      } finally {
        setIsLoadingMore(false);
      }
    }
  };

  // HÀM XỬ LÝ SỰ KIỆN: BẤM ENTER GỬI TIN
  const handleSendMessage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputText.trim() || !activeChat || !accessToken) return;

    const contentToSend = inputText.trim();
    setInputText(''); // Reset giao diện ngay lập tức

    // Xoá timeout gõ phím
    if (typingTimeoutRef.current) clearTimeout(typingTimeoutRef.current);
    if (roomId) stopTyping(roomId).catch(e => console.error(e));

    // Tạo tempId duy nhất để Worker có thể callback đúng tin tạm này
    const tempId = `temp-${Date.now()}`;

    // Xóa vạch Unread ngay khi A nhắn tin (chỉ UI, không gọi API)
    initialLastReadIdRef.current = undefined;

    try {
      if (activeChat.type === 'virtual') {
        // [MAGICAL FLOW] - Giờ mới bắt đầu tạo phòng
        setIsSendingFirstMessage(true);
        const targetUserId = activeChat.targetUser.id;

        const roomRes = await createAuthClient(accessToken).post<RoomDto>(`/api/v1/rooms/direct/${targetUserId}`);
        const realRoom = roomRes.data;
        realRoom.otherUserDisplayName = activeChat.targetUser.displayName;

        // Báo cho cha chuyển sang RealRoom
        onChatEvolvedToReal(realRoom);

        // Hiển thị ngay lập tức (Optimistic UI) với status Sending
        const tempMsg: MessageDto = {
          id: tempId,
          roomId: realRoom.id,
          senderId: userId || '',
          content: contentToSend,
          status: 'Sending',
          type: 'Text',
          createdAt: new Date().toISOString()
        };
        addMessage(realRoom.id, tempMsg);

        // Phát sóng bằng SignalR kèm tempId để Worker callback đúng
        await sendMessage(realRoom.id, contentToSend, tempId);
      } else {
        // Hiển thị Optimistic UI với status Sending
        const tempMsg: MessageDto = {
          id: tempId,
          roomId: roomId!,
          senderId: userId || '',
          content: contentToSend,
          status: 'Sending',
          type: 'Text',
          createdAt: new Date().toISOString()
        };
        addMessage(roomId!, tempMsg);

        // Luồng chat bình thường, phòng đã tồn tại
        await sendMessage(roomId!, contentToSend, tempId);
      }
    } catch (error) {
      console.error("Gửi tin thất bại", error);
      // Đánh dấu tin tạm là Failed nếu Hub invoke thất bại
      if (roomId) {
        const failedMsg: MessageDto = {
          id: tempId,
          roomId: roomId,
          senderId: userId || '',
          content: contentToSend,
          status: 'Failed',
          type: 'Text',
          createdAt: new Date().toISOString()
        };
        updateMessageStatus(roomId, tempId, failedMsg);
      }
    } finally {
      setIsSendingFirstMessage(false);
    }
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setInputText(e.target.value);

    if (roomId) {
      sendTyping(roomId).catch(e => console.error(e));

      // Clear cũ, set mới: sau 2s ngừng gõ thì gửi sự kiện ngưng
      if (typingTimeoutRef.current) clearTimeout(typingTimeoutRef.current);
      typingTimeoutRef.current = setTimeout(() => {
        stopTyping(roomId).catch(e => console.error(e));
      }, 2000);
    }
  };

  // ---------------- GIAO DIỆN HIỂN THỊ ----------------

  if (!activeChat) {
    return (
      <div className={styles.emptyState}>
        <svg width="64" height="64" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="1.5">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
        <h3>Chọn một cuộc trò chuyện</h3>
        <p>Chọn người dùng từ danh sách bên trái hoặc tìm kiếm ai đó để bắt đầu.</p>
      </div>
    );
  }

  const isVirtual = activeChat.type === 'virtual';
  const headerName = isVirtual
    ? activeChat.targetUser.displayName
    : (activeChat.room.name || activeChat.room.otherUserDisplayName || "Unknown");
  const headerActionTarget = isVirtual
    ? activeChat.targetUser
    : activeChat.room.type === 'DirectMessage' && activeChat.room.otherUserId
      ? {
          id: activeChat.room.otherUserId,
          displayName: activeChat.room.otherUserDisplayName ?? headerName,
          username: activeChat.room.otherUserUsername ?? null,
        }
      : null;
  const inlineDirectCallSession =
    activeChat.type === 'real' &&
    activeChat.room.type === 'DirectMessage' &&
    activeSession?.kind === 'direct-call' &&
    activeSession.sourceRoomId === activeChat.room.id &&
    voiceConnectionStatus !== 'idle'
      ? activeSession
      : null;
  const canStartDirectCall =
    activeChat.type === 'real' &&
    activeChat.room.type === 'DirectMessage' &&
    !inlineDirectCallSession;
  const groupMemberByUserId = new Map(groupMembers.map(member => [member.profile.id, member]));
  const getMessageAuthorTarget = (message: MessageDto) => {
    if (message.senderId === userId) return null;
    const member = groupMemberByUserId.get(message.senderId);
    return member?.profile ?? null;
  };
  const blockedUserIds = new Set(blockedUsers.map(blockedUser => blockedUser.user.id));
  const sharedGroupBlockedMembers =
    activeChat?.type === 'real' && activeChat.room.groupId
      ? groupMembers.filter(member => member.profile.id !== userId && blockedUserIds.has(member.profile.id))
      : [];
  const shouldShowSharedGroupBlockWarning =
    activeChat?.type === 'real' &&
    Boolean(activeChat.room.groupId) &&
    sharedGroupBlockedMembers.length > 0 &&
    !dismissedBlockedGroupWarningByRoom[activeChat.room.id];
  const sharedGroupBlockedNames = sharedGroupBlockedMembers
    .map(member => member.profile.displayName || member.profile.username || 'người dùng đã chặn')
    .slice(0, 3)
    .join(', ');

  const handleDismissBlockedGroupWarning = () => {
    if (activeChat?.type !== 'real') return;

    setDismissedBlockedGroupWarningByRoom(state => ({
      ...state,
      [activeChat.room.id]: true,
    }));
  };

  const handleLeaveSharedGroup = async () => {
    if (!accessToken || activeChat?.type !== 'real' || !activeChat.room.groupId) return;
    if (!window.confirm('Rời nhóm này? Bạn sẽ không còn thấy các kênh và tin nhắn mới trong nhóm.')) return;

    try {
      await leaveGroup(accessToken, activeChat.room.groupId);
      toast.success('Đã rời nhóm.');
      onGroupLeft?.();
    } catch {
      toast.error('Không thể rời nhóm, vui lòng thử lại sau.');
    }
  };

  return (
    <div className={`${styles.chatColumn} ${className || ''}`}>
      {/* Header Room Info */}
      <div className={styles.header}>
        <div className={styles.avatar}>
          {headerName[0]?.toUpperCase()}
        </div>
        <div className={styles.userInfo}>
          <h2 style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            {headerName}
            {activeChat?.type === 'real' && activeChat.room.isPrivate && (
              <span style={{ fontSize: '0.7rem', backgroundColor: '#f04747', padding: '2px 6px', borderRadius: '4px', color: 'white', fontWeight: 600 }}>PRIVATE</span>
            )}
          </h2>
          {isVirtual && <span className={styles.badge}>Chưa có cuộc hội thoại nào</span>}
        </div>

        {canStartDirectCall && (
          <DirectCallButton
            dmRoomId={activeChat.room.id}
            displayName={headerName}
          />
        )}

        {headerActionTarget && (
          <UserActionMenu
            target={headerActionTarget}
            hideMessageAction
          />
        )}

        {/* Nút thêm thành viên (Chỉ hiện cho Owner/Admin trong phòng Private) */}
        {activeChat?.type === 'real' && activeChat.room.isPrivate && activeChat.room.groupId && (currentUserRole === 'Owner' || currentUserRole === 'Admin') && (
          <button 
            className={styles.addMemberBtn}
            onClick={() => setIsAddMemberModalOpen(true)}
            title="Thêm thành viên vào phòng"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
              <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
              <circle cx="8.5" cy="7" r="4"></circle>
              <line x1="20" y1="8" x2="20" y2="14"></line>
              <line x1="23" y1="11" x2="17" y2="11"></line>
            </svg>
          </button>
        )}
      </div>

      {inlineDirectCallSession && (
        <section className={styles.inlineCallDock} aria-label="Cuộc gọi trực tiếp đang diễn ra">
          <Suspense fallback={<div className={styles.inlineCallLoading}>Đang tải cuộc gọi...</div>}>
            <VoiceRoomPanel
              mode="direct-call"
              roomId={inlineDirectCallSession.sourceRoomId}
              sessionId={inlineDirectCallSession.sessionId}
              roomName={inlineDirectCallSession.displayName}
              className={styles.inlineCallPanel}
            />
          </Suspense>
        </section>
      )}

      {shouldShowSharedGroupBlockWarning && (
        <section className={styles.blockedGroupWarning} aria-live="polite">
          <div className={styles.blockedGroupWarningText}>
            <strong>Nhóm này có người bạn đã chặn.</strong>
            <span>
              {sharedGroupBlockedNames}
              {sharedGroupBlockedMembers.length > 3 ? ` và ${sharedGroupBlockedMembers.length - 3} người khác` : ''}
              {' '}vẫn có thể gửi tin nhắn trong kênh chung. Tin nhắn nhóm không bị ẩn ở giai đoạn này.
            </span>
          </div>
          <div className={styles.blockedGroupWarningActions}>
            <button type="button" onClick={handleDismissBlockedGroupWarning}>
              Vào nhóm
            </button>
            <button type="button" className={styles.leaveGroupWarningButton} onClick={() => void handleLeaveSharedGroup()}>
              Rời nhóm
            </button>
          </div>
        </section>
      )}

      {/* Main Message List */}
      <div className={styles.messageList} ref={messageListRef} onScroll={handleScroll}>
        {isLoadingInitial && (
          <div className={styles.loadingWrapper}>Đang tải tin nhắn...</div>
        )}

        {isLoadingMore && (
          <div className={styles.loadingWrapper}>Đang tải thêm...</div>
        )}

        {!isLoadingInitial && messages.length === 0 && (
          <div className={styles.firstMsgPrompt}>
            Hãy là người tiên phong gửi lời chào đến {headerName}! 🤗
          </div>
        )}

        {/* Danh sách tin nhắn */}
        {messages.map((msg, idx) => {
          const isMine = msg.senderId === userId;

          // Tìm vị trí tin cuối cùng của mình trong phòng (để hiện Sent/Read ở đó)
          const lastMyMsgIndex = messages.reduce(
            (last, m, i) => (m.senderId === userId ? i : last),
            -1
          );
          const isLastMine = isMine && idx === lastMyMsgIndex;

          // Tìm xem có user nào đã đọc đến tin này không (để hiện avatar nhỏ)
          // Chỉ kiểm tra với tin của mình
          const readByOthers = isMine
            ? Object.entries(roomReadReceipts).filter(
              ([readUserId, lastReadMsgId]) => readUserId !== userId && lastReadMsgId === msg.id
            )
            : [];

          const showSending = isMine && msg.status === 'Sending';
          const showFailed = isMine && msg.status === 'Failed';
          const showSent = isMine && isLastMine && (msg.status === 'Sent' || msg.status === 'Delivered') && readByOthers.length === 0;

          const hasStatusText = showSending || showFailed || showSent;

          const isFirstUnread =
            initialLastReadIdRef.current &&
            msg.senderId !== userId && // Không hiện vạch trên tin của chính mình
            msg.id > initialLastReadIdRef.current &&
            (idx === 0 || messages[idx - 1].id <= initialLastReadIdRef.current);

          return (
            <div key={msg.id || idx}>
              {isFirstUnread && (
                <div id="unread-divider" className={styles.unreadDivider}>
                  <span>Tin nhắn mới</span>
                </div>
              )}
              <div className={`${styles.messageWrapper} ${isMine ? styles.mine : styles.theirs}`}>
                <div className={styles.messageColumn}>
                  {!isMine && getMessageAuthorTarget(msg) && (
                    <div className={styles.authorActionRow}>
                      <span className={styles.authorName}>
                        {getMessageAuthorTarget(msg)?.displayName}
                      </span>
                      <UserActionMenu
                        target={getMessageAuthorTarget(msg)!}
                        blockWarningMessage="You may still share group spaces with this user. Group messages are not hidden in this phase."
                      />
                    </div>
                  )}
                  {/* Bubble tin nhắn */}
                  <div className={`${styles.bubble} ${msg.status === 'Failed' ? styles.bubbleFailed : ''}`}>
                    {msg.content}
                  </div>

                  {/* Trạng thái tin nhắn — chỉ hiện phía người gửi VÀ khi có status cần hiển thị */}
                  {hasStatusText && (
                    <div className={styles.statusRow}>
                      {showSending && <span className={styles.statusSending}>⏳ Đang gửi...</span>}
                      {showFailed && <span className={styles.statusFailed}>✗ Gửi thất bại</span>}
                      {showSent && <span className={styles.statusSent}>✓ Đã gửi</span>}
                    </div>
                  )}

                  {/* Avatar nhỏ "Đã xem" — hiện dưới tin mà người kia đọc đến */}
                  {readByOthers.length > 0 && (
                    <div className={styles.readReceiptRow}>
                      {readByOthers.map(([readUserId]) => (
                        <div key={readUserId} className={styles.readAvatarSmall} title="Đã xem">
                          👁
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </div>
          );
        })}

        {/* Typing Indicator */}
        {typingUsers.size > 0 && (
          <div className={styles.typingIndicator}>
            <div className={styles.dot}></div>
            <div className={styles.dot}></div>
            <div className={styles.dot}></div>
          </div>
        )}

        {/* Điểm neo để tự động cuộn xuống cùng */}
        <div ref={endOfMessagesRef} />
      </div>

      {/* Input Form Textbox */}
      <form onSubmit={handleSendMessage} className={styles.inputArea}>
        <input
          type="text"
          value={inputText}
          onChange={handleInputChange}
          placeholder={`Nhập tin nhắn...`}
          disabled={isSendingFirstMessage}
          className={styles.textField}
        />
        <button
          type="submit"
          disabled={!inputText.trim() || isSendingFirstMessage}
          className={styles.sendButton}
        >
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <line x1="22" y1="2" x2="11" y2="13" />
            <polygon points="22 2 15 22 11 13 2 9 22 2" />
          </svg>
        </button>
      </form>

      {/* Modal Add Member */}
      {isAddMemberModalOpen && activeChat?.type === 'real' && activeChat.room.groupId && (
        <AddMemberToRoomModal
          groupId={activeChat.room.groupId}
          roomId={activeChat.room.id}
          roomName={activeChat.room.name || headerName}
          onClose={() => setIsAddMemberModalOpen(false)}
          onSuccess={(count) => {
            setIsAddMemberModalOpen(false);
            toast.success(`Đã thêm ${count} thành viên vào phòng.`);
          }}
        />
      )}
    </div>
  );
};

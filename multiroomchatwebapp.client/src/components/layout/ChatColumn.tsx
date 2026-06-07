import { lazy, Suspense, useState, useRef, useEffect, useLayoutEffect } from 'react';
import toast from 'react-hot-toast';
import { useAuth } from '../../context/AuthContext';
import { createAuthClient } from '../../api/apiClient';
import { getGroupMembers } from '../../api/groupApi';
import { cancelPendingChatMedia, getMediaAccessUrl, getMediaContentBlob, uploadChatMedia } from '../../api/mediaApi';
import { useChatStore } from '../../store/useChatStore';
import { useNotificationStore } from '../../store/useNotificationStore';
import { useVoiceStore } from '../../store/useVoiceStore';
import type {
  ActiveChat,
  AttachmentKind,
  RoomDto,
  MessageDto,
  GetMessagesResponse,
  MessageAcceptedResult,
  MessageAttachmentDto,
} from '../../types/chat';
import type { GroupMemberDto, GroupRole } from '../../types/group';
import { determineMessageType } from '../../utils/chatMessagePreview';
import { DirectCallButton } from '../call/DirectCallButton';
import { AddMemberToRoomModal } from '../group/AddMemberToRoomModal';
import { UserActionMenu } from '../user/UserActionMenu';
import styles from './ChatColumn.module.css';

const VoiceRoomPanel = lazy(() =>
  import('./VoiceRoomPanel').then((module) => ({ default: module.VoiceRoomPanel }))
);

const CHAT_MEDIA_ACCEPT = [
  '.jpg',
  '.jpeg',
  '.png',
  '.webp',
  '.mp3',
  '.wav',
  '.ogg',
  '.webm',
  '.mp4',
].join(',');

const BASIC_EMOJI_GROUPS = [
  {
    label: 'Smileys',
    emojis: [
      '😀', '😃', '😄', '😁', '😆', '😅', '😂', '🤣',
      '🙂', '🙃', '😉', '😊', '😇', '😍', '🥰', '😘',
      '😋', '😛', '😜', '🤪', '🤨', '🧐', '🤓', '😎',
      '🥳', '😏', '😒', '😞', '😔', '😟', '😕', '🙁',
      '☹️', '😣', '😖', '😫', '😩', '🥺', '😢', '😭',
      '😤', '😠', '😡', '🤯', '😳', '🥵', '🥶', '😱',
      '😨', '😰', '😥', '😓', '🤗', '🤔', '🤭', '🤫',
      '😶', '😐', '😑', '😬', '🙄', '😴',
    ],
  },
  {
    label: 'Gestures',
    emojis: [
      '👍', '👎', '👌', '✌️', '🤞', '🤟', '🤘',
      '🤙', '👋', '👏', '🙌', '🫶', '🙏', '💪',
    ],
  },
  {
    label: 'Symbols',
    emojis: [
      '❤️', '🧡', '💛', '💚', '💙', '💜', '🤍',
      '💔', '✨', '🔥', '💯', '✅', '❌', '⭐', '🎉',
    ],
  },
];

const CHAT_MEDIA_LIMITS: Record<AttachmentKind, number> = {
  Image: 10 * 1024 * 1024,
  Audio: 25 * 1024 * 1024,
  Video: 100 * 1024 * 1024,
  File: 25 * 1024 * 1024,
};

type PendingAttachmentStatus = 'uploading' | 'ready' | 'failed' | 'removing';

interface PendingAttachment {
  localId: string;
  mediaId?: string;
  kind: AttachmentKind;
  filename: string;
  size: number;
  mimeType: string;
  localPreviewUrl?: string;
  previewUrl?: string;
  expiresAt?: string;
  status: PendingAttachmentStatus;
  error?: string;
  controller?: AbortController;
}

const getClientMediaKind = (file: File): AttachmentKind | null => {
  const dotIndex = file.name.lastIndexOf('.');
  const extension = dotIndex >= 0 ? file.name.slice(dotIndex).toLowerCase() : '';
  const mimeType = file.type.toLowerCase();

  if (['.jpg', '.jpeg', '.png', '.webp'].includes(extension) || mimeType.startsWith('image/')) {
    return 'Image';
  }

  if (['.mp3', '.wav', '.ogg'].includes(extension) || mimeType.startsWith('audio/')) {
    return 'Audio';
  }

  if (extension === '.webm') {
    return mimeType.startsWith('audio/') ? 'Audio' : 'Video';
  }

  if (extension === '.mp4' || mimeType.startsWith('video/')) {
    return 'Video';
  }

  return null;
};

const formatFileSize = (size: number): string => {
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${(size / 1024 / 1024).toFixed(1)} MB`;
};

const toMessageAttachment = (attachment: PendingAttachment): MessageAttachmentDto => ({
  mediaId: attachment.mediaId ?? null,
  kind: attachment.kind,
  filename: attachment.filename,
  size: attachment.size,
  mimeType: attachment.mimeType,
  localPreviewUrl: attachment.localPreviewUrl,
  url: attachment.previewUrl,
  expiresAt: attachment.expiresAt,
});

interface MessageAttachmentRendererProps {
  attachment: MessageAttachmentDto;
  token: string | null | undefined;
}

const MessageAttachmentRenderer = ({ attachment, token }: MessageAttachmentRendererProps) => {
  const [url, setUrl] = useState(attachment.localPreviewUrl ?? attachment.url ?? '');
  const [expiresAt, setExpiresAt] = useState(attachment.expiresAt ?? null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [loadFailed, setLoadFailed] = useState(false);
  const retryRef = useRef(false);
  const objectUrlRef = useRef<string | null>(null);

  const clearObjectUrl = () => {
    if (!objectUrlRef.current) return;

    URL.revokeObjectURL(objectUrlRef.current);
    objectUrlRef.current = null;
  };

  useEffect(() => {
    setUrl(attachment.localPreviewUrl ?? attachment.url ?? '');
    setExpiresAt(attachment.expiresAt ?? null);
    setLoadFailed(false);
    retryRef.current = false;
  }, [attachment.mediaId, attachment.localPreviewUrl, attachment.url, attachment.expiresAt]);

  const refreshUrl = async () => {
    if (!token || !attachment.mediaId || isRefreshing) return;

    setIsRefreshing(true);
    try {
      const result = await getMediaAccessUrl(token, attachment.mediaId);
      setUrl(result.url);
      setExpiresAt(result.expiresAt ?? null);
      setLoadFailed(false);
    } catch (error) {
      console.error('Khong the refresh media URL:', error);
      setLoadFailed(true);
    } finally {
      setIsRefreshing(false);
    }
  };

  const loadContentBlob = async () => {
    if (!token || !attachment.mediaId || isRefreshing) return;

    setIsRefreshing(true);
    try {
      const blob = await getMediaContentBlob(token, attachment.mediaId);
      clearObjectUrl();

      const objectUrl = URL.createObjectURL(blob);
      objectUrlRef.current = objectUrl;
      setUrl(objectUrl);
      setExpiresAt(null);
      setLoadFailed(false);
    } catch (error) {
      console.error('Khong the tai media content:', error);
      setLoadFailed(true);
    } finally {
      setIsRefreshing(false);
    }
  };

  useEffect(() => {
    return () => {
      clearObjectUrl();
    };
  }, []);

  useEffect(() => {
    if (!attachment.mediaId) return;

    if (attachment.localPreviewUrl) return;

    if (attachment.kind !== 'File') {
      if (!url.startsWith('blob:')) {
        void loadContentBlob();
      }
      return;
    }

    if (!url) {
      refreshUrl();
      return;
    }

    if (!expiresAt) return;

    const expiresInMs = new Date(expiresAt).getTime() - Date.now();
    if (expiresInMs <= 60_000) {
      refreshUrl();
    }
  }, [attachment.mediaId, attachment.localPreviewUrl, attachment.kind, token, url, expiresAt]);

  const handleMediaError = () => {
    if (attachment.localPreviewUrl && url === attachment.localPreviewUrl) {
      if (attachment.kind !== 'File') {
        void loadContentBlob();
        return;
      }

      if (attachment.url) {
        setUrl(attachment.url);
        return;
      }
    }

    if (retryRef.current) {
      setLoadFailed(true);
      return;
    }

    retryRef.current = true;
    if (attachment.kind !== 'File') {
      void loadContentBlob();
      return;
    }

    void refreshUrl();
  };

  if (loadFailed) {
    return (
      <div className={styles.attachmentUnavailable}>
        Không tải được media
      </div>
    );
  }

  if (!url || isRefreshing) {
    return (
      <div className={styles.attachmentLoading}>
        Đang tải media...
      </div>
    );
  }

  if (attachment.kind === 'Image') {
    return (
      <button
        type="button"
        className={styles.imageAttachmentButton}
        onClick={() => window.open(url, '_blank', 'noopener,noreferrer')}
        title={attachment.filename}
      >
        <img
          src={url}
          alt={attachment.filename}
          className={styles.imageAttachment}
          onError={handleMediaError}
        />
      </button>
    );
  }

  if (attachment.kind === 'Audio') {
    return (
      <div className={styles.mediaAttachment}>
        <div className={styles.attachmentName}>{attachment.filename}</div>
        <audio controls src={url} className={styles.audioAttachment} onError={handleMediaError} />
      </div>
    );
  }

  if (attachment.kind === 'Video') {
    return (
      <div className={styles.mediaAttachment}>
        <video
          controls
          preload="metadata"
          src={url}
          className={styles.videoAttachment}
          onError={handleMediaError}
        />
        <div className={styles.attachmentName}>{attachment.filename}</div>
      </div>
    );
  }

  return (
    <a
      className={styles.fileAttachment}
      href={url}
      target="_blank"
      rel="noreferrer"
      onClick={(event) => {
        if (!url) event.preventDefault();
      }}
    >
      <span className={styles.fileIcon}>□</span>
      <span className={styles.fileInfo}>
        <span className={styles.attachmentName}>{attachment.filename}</span>
        <span className={styles.attachmentMeta}>{formatFileSize(attachment.size)}</span>
      </span>
    </a>
  );
};

interface ChatColumnProps {
  activeChat: ActiveChat | null;
  /** Callback trả ngược về Layout để update Cột 2 (VD: đang ảo gõ enter -> thành room thật) */
  onChatEvolvedToReal: (realRoom: RoomDto) => void;
  // Các hàm từ useSignalR truyền xuống
  sendMessage: (
    roomId: string,
    content: string,
    clientMessageId: string,
    mediaIds?: string[]
  ) => Promise<MessageAcceptedResult>;
  sendTyping: (roomId: string) => Promise<void>;
  stopTyping: (roomId: string) => Promise<void>;
  markAsRead: (
    roomId: string,
    messageId: string
  ) => Promise<void>;
  joinRoom: (roomId: string) => Promise<void>;
  /** Class CSS từ cha (Layout) để định hình cột */
  className?: string;
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
}: ChatColumnProps) => {
  const { accessToken, user } = useAuth();
  const userId = user?.userId;
  const activeSession = useVoiceStore((state) => state.activeSession);
  const voiceConnectionStatus = useVoiceStore((state) => state.connectionStatus);

  const roomId = activeChat?.type === 'real' ? activeChat.room.id : null;
  const storeMessages = useChatStore(state => roomId ? state.messages[roomId] : undefined);
  const messages = storeMessages || [];

  const storeTypingUsers = useChatStore(state => roomId ? state.typingUsers[roomId] : undefined);
  const typingUsers = storeTypingUsers || new Set();

  const storeHasMore = useChatStore(state => roomId ? state.hasMore[roomId] : undefined);
  const hasMore = storeHasMore ?? true;
  const historyCursor = useChatStore(state => roomId ? state.historyCursor[roomId] : undefined);
  const addMessage = useChatStore(state => state.addMessage);
  const setMessages = useChatStore(state => state.setMessages);
  const prependMessages = useChatStore(state => state.prependMessages);
  const setHasMore = useChatStore(state => state.setHasMore);
  const setHistoryCursor = useChatStore(state => state.setHistoryCursor);
  const updateMessageStatus = useChatStore(state => state.updateMessageStatus);
  const trimRoom = useChatStore(state => state.trimRoom);
  const realtimeSyncVersion = useNotificationStore(state => state.realtimeSyncVersion);

  // Lấy readReceipts của phòng hiện tại để biết người kia đã đọc đến tin nào
  const roomReadReceipts = useChatStore(state => roomId ? state.readReceipts[roomId] : undefined) || {};

  const [inputText, setInputText] = useState('');
  const [pendingAttachments, setPendingAttachments] = useState<PendingAttachment[]>([]);
  const [isEmojiPickerOpen, setIsEmojiPickerOpen] = useState(false);
  const [isSendingFirstMessage, setIsSendingFirstMessage] = useState(false);
  const [isLoadingInitial, setIsLoadingInitial] = useState(false);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [retryingMessageIds, setRetryingMessageIds] = useState<Set<string>>(new Set());
  const endOfMessagesRef = useRef<HTMLDivElement>(null);
  const messageListRef = useRef<HTMLDivElement>(null);
  const typingTimeoutRef = useRef<number | undefined>(undefined);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const composerRef = useRef<HTMLDivElement>(null);
  const textAreaRef = useRef<HTMLTextAreaElement>(null);
  const localAttachmentUrlsRef = useRef<Set<string>>(new Set());
  const lastHistorySyncVersionByRoomRef = useRef<Record<string, number>>({});

  const createLocalAttachmentUrl = (file: File): string => {
    const objectUrl = URL.createObjectURL(file);
    localAttachmentUrlsRef.current.add(objectUrl);
    return objectUrl;
  };

  const releaseLocalAttachmentUrl = (objectUrl?: string) => {
    if (!objectUrl || !localAttachmentUrlsRef.current.has(objectUrl)) return;

    URL.revokeObjectURL(objectUrl);
    localAttachmentUrlsRef.current.delete(objectUrl);
  };

  useEffect(() => {
    return () => {
      localAttachmentUrlsRef.current.forEach((objectUrl) => {
        URL.revokeObjectURL(objectUrl);
      });
      localAttachmentUrlsRef.current.clear();
    };
  }, []);

  // States cho tính năng Add Member
  const [currentUserRole, setCurrentUserRole] = useState<GroupRole | null>(null);
  const [groupMembers, setGroupMembers] = useState<GroupMemberDto[]>([]);
  const [isAddMemberModalOpen, setIsAddMemberModalOpen] = useState(false);

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
    setIsEmojiPickerOpen(false);
    setPendingAttachments((current) => {
      current.forEach((attachment) => {
        releaseLocalAttachmentUrl(attachment.localPreviewUrl);

        if (attachment.status === 'uploading') {
          attachment.controller?.abort();
          return;
        }

        if (attachment.status === 'ready' && attachment.mediaId && accessToken) {
          cancelPendingChatMedia(accessToken, attachment.mediaId)
            .catch((error) => console.error('Khong the huy pending media khi doi phong:', error));
        }
      });

      return [];
    });
  }, [activeChat]);

  useEffect(() => {
    if (!isEmojiPickerOpen) return;

    const handlePointerDown = (event: MouseEvent) => {
      const target = event.target;
      if (
        target instanceof Node &&
        composerRef.current &&
        !composerRef.current.contains(target)
      ) {
        setIsEmojiPickerOpen(false);
      }
    };

    document.addEventListener('mousedown', handlePointerDown);
    return () => document.removeEventListener('mousedown', handlePointerDown);
  }, [isEmojiPickerOpen]);

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
    const shouldSyncAfterReconnect =
      realtimeSyncVersion > 0 &&
      lastHistorySyncVersionByRoomRef.current[roomId] !== realtimeSyncVersion;
    if (existingMsgs.length > 0 && !shouldSyncAfterReconnect) {
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
        setMessages(roomId, dbMessages);
        setHasMore(roomId, res.data.hasMore);
        setHistoryCursor(roomId, res.data.nextCursor ?? null);
        lastHistorySyncVersionByRoomRef.current[roomId] = realtimeSyncVersion;
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
  }, [roomId, accessToken, realtimeSyncVersion]); // Cố tình không đưa messages vào đây để tránh re-fetch

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
      if (
        lastMsg.senderId !== userId &&
        document.visibilityState === 'visible'
      ) {
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
      const cursor = historyCursor;
      if (!cursor) return;

      setIsLoadingMore(true);
      // Ghi nhớ vị trí cuộn hiện tại để giữ nguyên khung nhìn
      const previousScrollHeight = target.scrollHeight;

      try {
        const authClient = createAuthClient(accessToken);
        const res = await authClient.get<GetMessagesResponse>(
          `/api/v1/chat/rooms/${roomId}/messages?beforeMessageId=${encodeURIComponent(cursor)}`
        );

        prependMessages(roomId, res.data.data);
        setHasMore(roomId, res.data.hasMore);
        setHistoryCursor(roomId, res.data.nextCursor ?? null);

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

  const handlePickAttachment = () => {
    if (!activeChat || isSendingFirstMessage) return;
    fileInputRef.current?.click();
  };

  const handleAttachmentSelected = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';

    if (!files.length || !accessToken || !activeChat) return;

    for (const file of files) {
      const kind = getClientMediaKind(file);
      if (!kind) {
        toast.error('Định dạng file chưa được hỗ trợ.');
        continue;
      }

      const maxBytes = CHAT_MEDIA_LIMITS[kind];
      if (file.size > maxBytes) {
        toast.error(`${file.name} vượt quá giới hạn ${formatFileSize(maxBytes)}.`);
        continue;
      }

      const localId = `pending-${Date.now()}-${Math.random().toString(36).slice(2)}`;
      const controller = new AbortController();
      const localPreviewUrl = createLocalAttachmentUrl(file);
      const pendingAttachment: PendingAttachment = {
        localId,
        kind,
        filename: file.name,
        size: file.size,
        mimeType: file.type || 'application/octet-stream',
        localPreviewUrl,
        status: 'uploading',
        controller,
      };

      setPendingAttachments((current) => [...current, pendingAttachment]);

      try {
        const uploaded = await uploadChatMedia(accessToken, file, controller.signal);
        setPendingAttachments((current) =>
          current.map((attachment) =>
            attachment.localId === localId
              ? {
                  ...attachment,
                  mediaId: uploaded.mediaId,
                  kind: uploaded.kind,
                  filename: uploaded.filename,
                  size: uploaded.size,
                  mimeType: uploaded.mimeType,
                  previewUrl: uploaded.previewUrl,
                  expiresAt: uploaded.expiresAt,
                  status: 'ready',
                  controller: undefined,
                }
              : attachment
          )
        );
      } catch (error) {
        if (controller.signal.aborted) {
          setPendingAttachments((current) =>
            current.filter((attachment) => attachment.localId !== localId)
          );
          continue;
        }

        console.error('Upload chat media failed:', error);
        toast.error('Không upload được file. Vui lòng thử lại.');
        setPendingAttachments((current) =>
          current.map((attachment) =>
            attachment.localId === localId
              ? { ...attachment, status: 'failed', error: 'Upload thất bại', controller: undefined }
              : attachment
          )
        );
      }
    }
  };

  const handleRemovePendingAttachment = async (attachment: PendingAttachment) => {
    if (attachment.status === 'uploading') {
      attachment.controller?.abort();
      releaseLocalAttachmentUrl(attachment.localPreviewUrl);
      setPendingAttachments((current) =>
        current.filter((item) => item.localId !== attachment.localId)
      );
      return;
    }

    if (attachment.status === 'failed' || !attachment.mediaId || !accessToken) {
      releaseLocalAttachmentUrl(attachment.localPreviewUrl);
      setPendingAttachments((current) =>
        current.filter((item) => item.localId !== attachment.localId)
      );
      return;
    }

    setPendingAttachments((current) =>
      current.map((item) =>
        item.localId === attachment.localId ? { ...item, status: 'removing' } : item
      )
    );

    try {
      await cancelPendingChatMedia(accessToken, attachment.mediaId);
      releaseLocalAttachmentUrl(attachment.localPreviewUrl);
      setPendingAttachments((current) =>
        current.filter((item) => item.localId !== attachment.localId)
      );
    } catch (error) {
      console.error('Cancel pending media failed:', error);
      toast.error('Không xóa được file đã chọn.');
      setPendingAttachments((current) =>
        current.map((item) =>
          item.localId === attachment.localId ? { ...item, status: 'ready' } : item
        )
      );
    }
  };

  const handleEmojiSelected = (emoji: string) => {
    const textarea = textAreaRef.current;
    const start = textarea?.selectionStart ?? inputText.length;
    const end = textarea?.selectionEnd ?? inputText.length;
    const nextValue = `${inputText.slice(0, start)}${emoji}${inputText.slice(end)}`;

    setInputText(nextValue);

    requestAnimationFrame(() => {
      textarea?.focus();
      const nextPosition = start + emoji.length;
      textarea?.setSelectionRange(nextPosition, nextPosition);
    });
  };

  const handleSendMessage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeChat || !accessToken) return;

    const readyAttachments = pendingAttachments.filter(
      (attachment) => attachment.status === 'ready' && attachment.mediaId
    );
    const isBusyWithAttachments = pendingAttachments.some(
      (attachment) => attachment.status === 'uploading' || attachment.status === 'removing'
    );

    const contentToSend = inputText.trim();
    if (!contentToSend && readyAttachments.length === 0) return;

    if (isBusyWithAttachments) {
      toast.error('Vui lòng chờ file upload xong trước khi gửi.');
      return;
    }

    const optimisticAttachments = readyAttachments.map(toMessageAttachment);
    const mediaIds = readyAttachments
      .map((attachment) => attachment.mediaId)
      .filter((mediaId): mediaId is string => Boolean(mediaId));

    setInputText(''); // Reset giao diện ngay lập tức
    setIsEmojiPickerOpen(false);
    setPendingAttachments([]);

    // Xoá timeout gõ phím
    if (typingTimeoutRef.current) clearTimeout(typingTimeoutRef.current);
    if (roomId) stopTyping(roomId).catch(e => console.error(e));

    // UUID này là khóa đối chiếu ổn định cho toàn bộ vòng đời gửi và retry.
    const clientMessageId = crypto.randomUUID();

    // Xóa vạch Unread ngay khi A nhắn tin (chỉ UI, không gọi API)
    initialLastReadIdRef.current = undefined;

    let optimisticRoomId = roomId;
    let optimisticMessageAdded = false;

    try {
      if (activeChat.type === 'virtual') {
        // [MAGICAL FLOW] - Giờ mới bắt đầu tạo phòng
        setIsSendingFirstMessage(true);
        const targetUserId = activeChat.targetUser.id;

        const roomRes = await createAuthClient(accessToken).post<RoomDto>(`/api/v1/rooms/direct/${targetUserId}`);
        const realRoom = roomRes.data;
        realRoom.otherUserDisplayName = activeChat.targetUser.displayName;
        realRoom.otherUserUsername = activeChat.targetUser.username;
        realRoom.otherUserAvatarUrl = activeChat.targetUser.avatarUrl;

        // Báo cho cha chuyển sang RealRoom
        onChatEvolvedToReal(realRoom);

        // Hiển thị ngay lập tức (Optimistic UI) với status Sending
        const optimisticMessage: MessageDto = {
          id: clientMessageId,
          clientMessageId,
          roomId: realRoom.id,
          senderId: userId || '',
          content: contentToSend,
          status: 'Sending',
          type: determineMessageType(contentToSend, optimisticAttachments),
          createdAt: new Date().toISOString(),
          attachments: optimisticAttachments,
        };
        optimisticRoomId = realRoom.id;
        addMessage(realRoom.id, optimisticMessage);
        optimisticMessageAdded = true;

        const acceptedResult = await sendMessage(
          realRoom.id,
          contentToSend,
          clientMessageId,
          mediaIds
        );
        updateMessageStatus(realRoom.id, clientMessageId, {
          ...optimisticMessage,
          id: acceptedResult.messageId,
          clientMessageId: acceptedResult.clientMessageId,
          status: 'Accepted',
          acceptedAtUtc: acceptedResult.acceptedAtUtc,
        }, 'accepted');
      } else {
        // Hiển thị Optimistic UI với status Sending
        const optimisticMessage: MessageDto = {
          id: clientMessageId,
          clientMessageId,
          roomId: roomId!,
          senderId: userId || '',
          content: contentToSend,
          status: 'Sending',
          type: determineMessageType(contentToSend, optimisticAttachments),
          createdAt: new Date().toISOString(),
          attachments: optimisticAttachments,
        };
        addMessage(roomId!, optimisticMessage);
        optimisticMessageAdded = true;

        // Luồng chat bình thường, phòng đã tồn tại
        const acceptedResult = await sendMessage(
          roomId!,
          contentToSend,
          clientMessageId,
          mediaIds
        );
        updateMessageStatus(roomId!, clientMessageId, {
          ...optimisticMessage,
          id: acceptedResult.messageId,
          clientMessageId: acceptedResult.clientMessageId,
          status: 'Accepted',
          acceptedAtUtc: acceptedResult.acceptedAtUtc,
        }, 'accepted');
      }
    } catch (error) {
      if (!optimisticMessageAdded) {
        setInputText(contentToSend);
        setPendingAttachments(readyAttachments);
      }
      console.error("Gửi tin thất bại", error);
      // Đánh dấu tin tạm là Failed nếu Hub invoke thất bại
      if (optimisticRoomId) {
        const failedMsg: MessageDto = {
          id: clientMessageId,
          clientMessageId,
          roomId: optimisticRoomId,
          senderId: userId || '',
          content: contentToSend,
          status: 'Failed',
          type: determineMessageType(contentToSend, optimisticAttachments),
          createdAt: new Date().toISOString(),
          attachments: optimisticAttachments,
        };
        updateMessageStatus(
          optimisticRoomId,
          clientMessageId,
          failedMsg,
          'rejected'
        );
      }
    } finally {
      setIsSendingFirstMessage(false);
    }
  };

  const handleRetryMessage = async (message: MessageDto) => {
    if (!accessToken || !message.clientMessageId) {
      return;
    }

    if (message.status === 'Accepted' || message.status === 'Sent') {
      toast.error('Tin nhắn này đã được hệ thống tiếp nhận, không thể gửi lại từ giao diện.');
      return;
    }

    const retryKey = message.clientMessageId;
    if (retryingMessageIds.has(retryKey)) {
      return;
    }

    const attachments = message.attachments ?? [];
    const hasAttachmentWithoutMediaId = attachments.some(
      (attachment) => !attachment.mediaId
    );
    if (hasAttachmentWithoutMediaId) {
      toast.error('Không thể gửi lại vì thiếu mã tệp đính kèm.');
      return;
    }

    const mediaIds = attachments
      .map((attachment) => attachment.mediaId)
      .filter((mediaId): mediaId is string => Boolean(mediaId));

    setRetryingMessageIds((current) => new Set(current).add(retryKey));
    updateMessageStatus(
      message.roomId,
      message.clientMessageId,
      {
        ...message,
        status: 'Sending',
      },
      'retrying'
    );

    try {
      const acceptedResult = await sendMessage(
        message.roomId,
        message.content,
        message.clientMessageId,
        mediaIds
      );

      updateMessageStatus(
        message.roomId,
        message.clientMessageId,
        {
          ...message,
          id: acceptedResult.messageId,
          clientMessageId: acceptedResult.clientMessageId,
          status: 'Accepted',
          acceptedAtUtc: acceptedResult.acceptedAtUtc,
        },
        'accepted'
      );
    } catch (error) {
      console.error('Gửi lại tin nhắn thất bại', error);
      toast.error('Chưa gửi lại được tin nhắn.');
      updateMessageStatus(
        message.roomId,
        message.clientMessageId,
        {
          ...message,
          status: 'Failed',
        },
        'rejected'
      );
    } finally {
      setRetryingMessageIds((current) => {
        const next = new Set(current);
        next.delete(retryKey);
        return next;
      });
    }
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
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

  const handleTextAreaKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key !== 'Enter' || e.shiftKey) {
      return;
    }

    e.preventDefault();

    if (canSendMessage) {
      e.currentTarget.form?.requestSubmit();
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
  const hasReadyAttachments = pendingAttachments.some(attachment => attachment.status === 'ready');
  const hasBusyAttachments = pendingAttachments.some(
    attachment => attachment.status === 'uploading' || attachment.status === 'removing'
  );
  const canSendMessage =
    Boolean(inputText.trim() || hasReadyAttachments) &&
    !hasBusyAttachments &&
    !isSendingFirstMessage;

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

          const showSending = isMine && (msg.status === 'Sending' || msg.status === 'Accepted');
          const showFailed = isMine && msg.status === 'Failed';
          const showSent = isMine && isLastMine && (msg.status === 'Sent' || msg.status === 'Delivered') && readByOthers.length === 0;
          const canRetryMessage =
            showFailed &&
            Boolean(msg.clientMessageId);
          const retryKey = msg.clientMessageId ?? msg.id;
          const isRetryingMessage = retryingMessageIds.has(retryKey);

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
                  {msg.attachments && msg.attachments.length > 0 && (
                    <div className={styles.attachmentStack}>
                      {msg.attachments.map((attachment, attachmentIndex) => (
                        <MessageAttachmentRenderer
                          key={attachment.mediaId ?? `${msg.id}-${attachmentIndex}`}
                          attachment={attachment}
                          token={accessToken}
                        />
                      ))}
                    </div>
                  )}

                  {msg.content && (
                    <div className={`${styles.bubble} ${msg.status === 'Failed' ? styles.bubbleFailed : ''}`}>
                      {msg.content}
                    </div>
                  )}

                  {/* Trạng thái tin nhắn — chỉ hiện phía người gửi VÀ khi có status cần hiển thị */}
                  {hasStatusText && (
                    <div className={styles.statusRow}>
                      {showSending && <span className={styles.statusSending}>⏳ Đang gửi...</span>}
                      {showFailed && <span className={styles.statusFailed}>✗ Gửi thất bại</span>}
                      {canRetryMessage && (
                        <button
                          type="button"
                          className={styles.retryButton}
                          disabled={isRetryingMessage}
                          onClick={() => handleRetryMessage(msg)}
                        >
                          {isRetryingMessage ? 'Đang gửi lại...' : 'Gửi lại'}
                        </button>
                      )}
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
      <div className={styles.composer} ref={composerRef}>
        {pendingAttachments.length > 0 && (
          <div className={styles.pendingAttachmentList}>
            {pendingAttachments.map((attachment) => (
              <div key={attachment.localId} className={styles.pendingAttachmentItem}>
                <div className={styles.pendingAttachmentPreview}>
                  {attachment.kind === 'Image' && (attachment.localPreviewUrl || attachment.previewUrl) ? (
                    <img src={attachment.localPreviewUrl ?? attachment.previewUrl} alt={attachment.filename} />
                  ) : (
                    <span>{attachment.kind}</span>
                  )}
                </div>
                <div className={styles.pendingAttachmentInfo}>
                  <span className={styles.pendingAttachmentName}>{attachment.filename}</span>
                  <span className={styles.pendingAttachmentMeta}>
                    {attachment.status === 'uploading' && 'Đang upload...'}
                    {attachment.status === 'ready' && formatFileSize(attachment.size)}
                    {attachment.status === 'failed' && (attachment.error ?? 'Upload thất bại')}
                    {attachment.status === 'removing' && 'Đang xóa...'}
                  </span>
                </div>
                <button
                  type="button"
                  className={styles.pendingRemoveButton}
                  onClick={() => handleRemovePendingAttachment(attachment)}
                  disabled={attachment.status === 'removing'}
                  title="Xóa file"
                >
                  ×
                </button>
              </div>
            ))}
          </div>
        )}

        {isEmojiPickerOpen && (
          <div className={styles.emojiPicker} role="dialog" aria-label="Emoji">
            {BASIC_EMOJI_GROUPS.map((group) => (
              <div key={group.label} className={styles.emojiGroup}>
                <div className={styles.emojiGroupLabel}>{group.label}</div>
                <div className={styles.emojiGrid}>
                  {group.emojis.map((emoji) => (
                    <button
                      key={emoji}
                      type="button"
                      className={styles.emojiOption}
                      onClick={() => handleEmojiSelected(emoji)}
                    >
                      {emoji}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
        )}

      <form onSubmit={handleSendMessage} className={styles.inputArea}>
        <input
          ref={fileInputRef}
          type="file"
          accept={CHAT_MEDIA_ACCEPT}
          multiple
          className={styles.fileInput}
          onChange={handleAttachmentSelected}
        />
        <button
          type="button"
          className={styles.emojiButton}
          onClick={() => setIsEmojiPickerOpen((current) => !current)}
          disabled={isSendingFirstMessage}
          title="Emoji"
          aria-label="Emoji"
          aria-expanded={isEmojiPickerOpen}
        >
          🙂
        </button>
        <button
          type="button"
          className={styles.attachButton}
          onClick={handlePickAttachment}
          disabled={isSendingFirstMessage}
          title="Đính kèm file"
        >
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M21.44 11.05 12.25 20.24a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 1 1-2.83-2.83l8.49-8.48" />
          </svg>
        </button>
        <textarea
          ref={textAreaRef}
          value={inputText}
          onChange={handleInputChange}
          onKeyDown={handleTextAreaKeyDown}
          placeholder={`Nhập tin nhắn...`}
          rows={1}
          disabled={isSendingFirstMessage}
          className={styles.textField}
        />
        <button
          type="submit"
          disabled={!canSendMessage}
          className={styles.sendButton}
        >
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <line x1="22" y1="2" x2="11" y2="13" />
            <polygon points="22 2 15 22 11 13 2 9 22 2" />
          </svg>
        </button>
      </form>
      </div>

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

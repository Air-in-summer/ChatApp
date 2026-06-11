import { useState, useEffect, useRef, useCallback, type MouseEvent } from 'react';
import toast from 'react-hot-toast';
import { useAuth } from '../../context/AuthContext';
import { apiClient } from '../../api/apiClient';
import { getGroupRooms, getGroupMembers, createGroupChannel, leaveGroup, updateGroupRoom, deleteGroupRoom } from '../../api/groupApi';
import { UserSearchModal } from '../discovery/UserSearchModal';
import { GroupSettingsModal } from '../group/GroupSettingsModal';
import { CreateChannelModal } from '../group/CreateChannelModal';
import { useChatStore } from '../../store/useChatStore';
import { useNotificationStore } from '../../store/useNotificationStore';
import { useUserRelationshipsStore } from '../../store/useUserRelationshipsStore';
import { useVoiceConnection } from '../../hooks/useVoiceConnection';
import { VoiceStatusBar } from './VoiceStatusBar';
import type { ActiveChat, RoomDto, UserSearchResult } from '../../types/chat';
import type { GroupDto, GroupRole, GroupMemberDto, CreateGroupChannelRequest } from '../../types/group';
import styles from './RoomListColumn.module.css';

interface RoomListColumnProps {
  /** Ngữ cảnh hiển thị: 'dm' (Tin nhắn cá nhân) hoặc 'group' (Kênh trong Server) */
  context: 'dm' | 'group';
  /** Đối tượng Group hiện tại (Bắt buộc nếu context là 'group') */
  group?: GroupDto;
  /** Callback quay lại danh sách Server */
  onBack?: () => void;
  /** Phòng chat đang được chọn */
  activeChat: ActiveChat | null;
  /** Callback khi chọn một phòng mới */
  onSelectChat: (chat: ActiveChat | null) => void;
  onGroupUpdated?: (group: GroupDto) => void;
  /** Class CSS từ cha (Layout) để định hình cột */
  className?: string;
}

const DmRoomAvatar = ({ room }: { room: RoomDto }) => {
  const [hasError, setHasError] = useState(false);
  const avatarUrl = room.otherUserAvatarUrl;
  const fallback = (room.otherUserDisplayName || room.otherUserUsername || '?')[0]?.toUpperCase() || '?';

  useEffect(() => {
    setHasError(false);
  }, [avatarUrl]);

  if (avatarUrl && !hasError) {
    return (
      <img
        className={styles.avatarImage}
        src={avatarUrl}
        alt=""
        referrerPolicy="no-referrer"
        onError={() => setHasError(true)}
      />
    );
  }

  return <>{fallback}</>;
};

/**
 * Cột 2: Danh sách phòng chat hoặc danh sách Kênh (Channels).
 * Quản lý việc hiển thị danh sách, tìm kiếm người dùng (cho DM) và cập nhật số tin nhắn chưa đọc.
 * 
 * @remarks
 * Luồng xử lý:
 * 1. Tải danh sách phòng từ API dựa trên context (DM hoặc Group).
 * 2. Đồng bộ số tin nhắn chưa đọc (unreadCount) vào global store.
 * 3. Sử dụng Ref để theo dõi snapshot danh sách phòng nhằm tránh re-render/re-fetch vô hạn.
 * 4. Tự động tải lại danh sách khi có phòng mới được tạo hoặc có tin nhắn chưa đọc từ phòng lạ.
 * 5. Render giao diện khác nhau tùy thuộc vào ngữ cảnh DM (phong cách Messenger) hay Group (phong cách Discord).
 */
export const RoomListColumn = ({ context, group, onBack, activeChat, onSelectChat, onGroupUpdated, className }: RoomListColumnProps) => {
  const { isAuthenticated, user } = useAuth();
  const [rooms, setRooms] = useState<RoomDto[]>([]);
  const [isSearchOpen, setIsSearchOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isCreateChannelOpen, setIsCreateChannelOpen] = useState(false);
  const [currentUserRole, setCurrentUserRole] = useState<GroupRole>('Member');
  const [groupMembers, setGroupMembers] = useState<GroupMemberDto[]>([]);
  const [dismissedBlockedGroupWarningByGroup, setDismissedBlockedGroupWarningByGroup] = useState<Record<string, boolean>>({});
  const {
    joinVoiceRoom,
    shouldSwitchVoiceSession,
    leaveCurrentVoiceSessionForSwitch,
  } = useVoiceConnection();

  // Hook lấy dữ liệu unread và metadata từ global store (Zustand)
  const unreadCount = useChatStore(state => state.unreadCount);
  const clearUnread = useChatStore(state => state.clearUnread);
  const setInitialUnreadCounts = useChatStore(state => state.setInitialUnreadCounts);
  const setInitialLastReadIds = useChatStore(state => state.setInitialLastReadIds);
  // roomMetadata: chứa lastMessage cập nhật real-time qua SignalR
  const roomMetadata = useChatStore(state => state.roomMetadata);
  const friends = useUserRelationshipsStore(state => state.friends);
  const blockedUsers = useUserRelationshipsStore(state => state.blockedUsers);
  const presenceByUserId = useUserRelationshipsStore(state => state.presenceByUserId);
  const loadFriends = useUserRelationshipsStore(state => state.loadFriends);
  const loadBlockedUsers = useUserRelationshipsStore(state => state.loadBlockedUsers);
  const loadFriendsPresence = useUserRelationshipsStore(state => state.loadFriendsPresence);

  /** Format thời gian hiển thị (VD: 14:30 hoặc 05/05) */
  const formatTime = (isoString?: string) => {
    if (!isoString) return '';
    const date = new Date(isoString);
    const today = new Date();
    if (date.toDateString() === today.toDateString()) {
      return date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
    }
    return date.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' });
  };

  const formatLastSeen = (isoString?: string | null) => {
    if (!isoString) return '';
    return `Last seen ${formatTime(isoString)}`;
  };

  const getAllowedDmPresence = (otherUserId?: string | null) => {
    if (!otherUserId) return null;
    if (!friends.some(friend => friend.user.id === otherUserId)) return null;
    if (blockedUsers.some(blockedUser => blockedUser.user.id === otherUserId)) return null;

    return presenceByUserId[otherUserId] ?? null;
  };

  useEffect(() => {
    if (!isAuthenticated) return;

    if (context === 'dm') {
      void loadFriends().catch(() => undefined);
      void loadBlockedUsers().catch(() => undefined);
      void loadFriendsPresence().catch(() => undefined);
      return;
    }

    if (context === 'group') {
      void loadBlockedUsers().catch(() => undefined);
    }
  }, [isAuthenticated, context, loadFriends, loadBlockedUsers, loadFriendsPresence]);

  /**
   * [Luồng: Tải dữ liệu phòng]
   * Logic lấy dữ liệu tập trung, hỗ trợ cả 2 ngữ cảnh DM và Group.
   */
  const fetchRoomsLogic = useCallback(async () => {
    if (!isAuthenticated) return;
    try {
      let fetchedRooms: RoomDto[] = [];

      // Nhánh 1: Nếu là Group, lấy danh sách Channel qua GroupId
      if (context === 'group' && group) {
        fetchedRooms = await getGroupRooms(group.id);
      }
      // Nhánh 2: Nếu là DM, lấy toàn bộ phòng của User và lọc DirectMessage
      else if (context === 'dm') {
        const response = await apiClient.get<RoomDto[]>('/api/v1/rooms/my-rooms');
        fetchedRooms = response.data.filter(r => r.type === 'DirectMessage');
      }

      setRooms(fetchedRooms);

      // [Luồng: Đồng bộ trạng thái đọc]
      // Khởi tạo unread count VÀ last read ids từ API vào store để đồng bộ UI
      const unreadMap: Record<string, number> = {};
      const lastReadMap: Record<string, string> = {};
      fetchedRooms.forEach(r => {
        if (r.unreadCount !== undefined && r.unreadCount > 0) {
          unreadMap[r.id] = r.unreadCount;
        }
        if (r.lastReadMessageId) {
          lastReadMap[r.id] = r.lastReadMessageId;
        }
      });
      setInitialUnreadCounts(unreadMap);
      setInitialLastReadIds(lastReadMap);
    } catch (err) {
      console.error('Không thể lấy danh sách phòng:', err);
    }
  }, [isAuthenticated, context, group?.id, setInitialUnreadCounts, setInitialLastReadIds]);

  // Tự động tải lại khi đổi context hoặc GroupID
  useEffect(() => {
    fetchRoomsLogic();

    // [Bước 15.2]: Lấy vai trò của user hiện tại trong group
    if (context === 'group' && group && isAuthenticated && user) {
      const fetchRole = async () => {
        try {
          const members = await getGroupMembers(group.id);
          setGroupMembers(members);

          const me = members.find(m => m.profile.id === user?.userId);
          if (me) {
            setCurrentUserRole(me.role);
          } else {
            // Fallback: nếu chưa fetch được member nhưng mình là owner dựa trên group metadata
            if (group.ownerId === user?.userId) {
              setCurrentUserRole('Owner');
            }
          }
        } catch (err) {
          console.error('Failed to fetch group role:', err);
          setGroupMembers([]);
          // Fallback check ownerId
          if (group.ownerId === user?.userId) {
            setCurrentUserRole('Owner');
          }
        }
      };
      fetchRole();
    } else {
      setCurrentUserRole('Member');
      setGroupMembers([]);
    }
  }, [fetchRoomsLogic, context, group, isAuthenticated, user?.userId]);

  // Snapshot Ref để đọc data "mới nhất" trong các useEffect mà không gây loop phụ thuộc
  const roomsRef = useRef<RoomDto[]>([]);
  useEffect(() => {
    roomsRef.current = rooms;
  }, [rooms]);

  // Alias để tái sử dụng logic tải lại
  const refreshRooms = fetchRoomsLogic;

  // [Luồng: Tự động cập nhật danh sách]
  // Hiệu ứng 1: Re-fetch khi một phòng "Ảo" vừa được "Thật hóa" (có tin nhắn đầu tiên)
  useEffect(() => {
    if (!isAuthenticated) return;
    if (activeChat?.type !== 'real') return;
    // Nếu phòng real này chưa có trong list hiện tại -> mới được tạo -> refresh
    if (roomsRef.current.some(r => r.id === activeChat.room.id)) return;
    refreshRooms();
  }, [activeChat, isAuthenticated, refreshRooms]);

  // Hiệu ứng 2: Re-fetch khi SignalR báo có tin nhắn từ một RoomId chưa từng thấy trong list
  useEffect(() => {
    if (!isAuthenticated) return;
    const hasUnknownRoom = Object.keys(unreadCount).some(
      roomId => unreadCount[roomId] > 0 && !roomsRef.current.some(r => r.id === roomId)
    );
    if (hasUnknownRoom) refreshRooms();
  }, [unreadCount, isAuthenticated, refreshRooms]);

  // Hiệu ứng 3 (Notification): Re-fetch khi Backend báo có phòng mới được tạo trong Group đang mở
  const roomRefetchGroupId = useNotificationStore(s => s.roomRefetchGroupId);
  const realtimeSyncVersion = useNotificationStore(s => s.realtimeSyncVersion);
  const clearRoomRefetch = useNotificationStore(s => s.clearRoomRefetch);

  useEffect(() => {
    if (context !== 'group' || !group) return;
    if (roomRefetchGroupId && roomRefetchGroupId === group.id) {
      refreshRooms();
      clearRoomRefetch();
    }
  }, [roomRefetchGroupId, context, group, refreshRooms, clearRoomRefetch]);

  useEffect(() => {
    if (!isAuthenticated || realtimeSyncVersion === 0) return;

    refreshRooms();
  }, [isAuthenticated, realtimeSyncVersion, refreshRooms]);

  /** Xử lý chọn phòng chat */
  const handleSelectRoom = async (room: RoomDto) => {
    if (room.type === 'Voice') {
      if (!isAuthenticated) return;

      if (!shouldSwitchVoiceSession()) {
        return;
      }

      clearUnread(room.id);
      onSelectChat({ type: 'real', room });

      try {
        await leaveCurrentVoiceSessionForSwitch();
        await joinVoiceRoom(room.id, room.name || 'Voice Channel');
      } catch (error) {
        console.error('Không thể tham gia Voice room:', error);
        toast.error('Không thể tham gia Voice room.');
      }
      return;
    }

    // Xóa badge đỏ ở local UI ngay lập tức để tăng UX cảm giác nhanh
    clearUnread(room.id);
    onSelectChat({ type: 'real', room });
  };

  /** Xử lý chọn từ ô tìm kiếm (Chỉ dành cho DM) */
  const handleSelectFromSearch = (targetUser: UserSearchResult) => {
    // Nếu đã từng chat rồi -> dùng phòng cũ
    const existingRoom = rooms.find(
      r => r.type === 'DirectMessage' && r.otherUserUsername === targetUser.username
    );

    if (existingRoom) {
      onSelectChat({ type: 'real', room: existingRoom });
    } else {
      // Nếu chưa chat bao giờ -> tạo Virtual Room (Chờ tin nhắn đầu tiên mới tạo DB)
      onSelectChat({ type: 'virtual', targetUser });
    }
    setIsSearchOpen(false);
  };

  /**
   * [Bước 15.4]: Xử lý tạo Kênh mới qua API.
   * Cập nhật danh sách và nhảy vào kênh mới ngay lập tức.
   */
  const handleCreateChannelSubmit = async (request: CreateGroupChannelRequest) => {
    if (!isAuthenticated || !group) return;

    try {
      const { roomId } = await createGroupChannel(group.id, request);
      
      toast.success(`Đã tạo kênh #${request.name} thành công!`);
      setIsCreateChannelOpen(false);

      // Tải lại danh sách phòng để lấy data RoomDto đầy đủ cho activeChat
      const updatedRooms = await getGroupRooms(group.id);
      setRooms(updatedRooms);

      // Tìm phòng vừa tạo trong list mới để lấy object RoomDto hoàn chỉnh
      const newRoom = updatedRooms.find(r => r.id === roomId);
      if (newRoom) {
        onSelectChat({ type: 'real', room: newRoom });
      }
    } catch (error: any) {
      const errorMsg = error.response?.data?.detail || 'Không thể tạo kênh. Vui lòng thử lại sau.';
      toast.error(errorMsg);
      throw error; // Để Modal biết và tắt Loading
    }
  };

  const handleRenameGroupRoom = async (
    room: RoomDto,
    event: MouseEvent<HTMLButtonElement>
  ) => {
    event.stopPropagation();
    if (!isAuthenticated || !group) return;

    const nextName = window.prompt('Nhập tên phòng mới', room.name || '');
    if (nextName === null) return;

    const normalizedName = nextName.trim();
    if (!normalizedName) {
      toast.error('Tên phòng không được bỏ trống.');
      return;
    }

    try {
      const updatedRoom = await updateGroupRoom(group.id, room.id, { name: normalizedName });
      setRooms(currentRooms =>
        currentRooms.map(currentRoom =>
          currentRoom.id === updatedRoom.id ? { ...currentRoom, ...updatedRoom } : currentRoom
        )
      );

      if (activeChat?.type === 'real' && activeChat.room.id === updatedRoom.id) {
        onSelectChat({ type: 'real', room: { ...activeChat.room, ...updatedRoom } });
      }

      toast.success('Đã đổi tên phòng.');
      void refreshRooms();
    } catch (error: any) {
      const errorMsg = error.response?.data?.detail || 'Không thể đổi tên phòng.';
      toast.error(errorMsg);
    }
  };

  const handleDeleteTextRoom = async (room: RoomDto, event: MouseEvent<HTMLButtonElement>) => {
    event.stopPropagation();
    if (!isAuthenticated || !group) return;

    if (!window.confirm(`Xoa phong #${room.name || 'khong ten'}? Tin nhan cu se khong bi xoa vat ly.`)) {
      return;
    }

    try {
      await deleteGroupRoom(group.id, room.id);
      setRooms(currentRooms => currentRooms.filter(currentRoom => currentRoom.id !== room.id));

      if (activeChat?.type === 'real' && activeChat.room.id === room.id) {
        onSelectChat(null);
      }

      toast.success('Da xoa phong.');
      void refreshRooms();
    } catch (error: any) {
      const errorMsg = error.response?.data?.detail || 'Khong the xoa phong.';
      toast.error(errorMsg);
    }
  };

  // [Nhánh Render: Ngữ cảnh Group]
  // Hiển thị danh sách kênh của một Server (Discord style)
  // Phân tách kênh Text và Voice ra 2 section riêng biệt
  const handleDismissBlockedGroupWarning = () => {
    if (!group) return;

    setDismissedBlockedGroupWarningByGroup(state => ({
      ...state,
      [group.id]: true,
    }));
  };

  const handleLeaveSharedGroup = async () => {
    if (!isAuthenticated || !group) return;
    if (!window.confirm('Rời nhóm này? Bạn sẽ không còn thấy các kênh và tin nhắn mới trong nhóm.')) return;

    try {
      await leaveGroup(group.id);
      toast.success('Đã rời nhóm.');
      onBack?.();
    } catch {
      toast.error('Không thể rời nhóm, vui lòng thử lại sau.');
    }
  };

  if (context === 'group') {
    // Tách danh sách rooms thành Text channels và Voice channels
    const textRooms = rooms.filter(r => r.type === 'Text');
    const voiceRooms = rooms.filter(r => r.type === 'Voice');
    const canManageGroupRooms = currentUserRole === 'Owner' || currentUserRole === 'Admin';
    const blockedUserIds = new Set(blockedUsers.map(blockedUser => blockedUser.user.id));
    const sharedGroupBlockedMembers = groupMembers.filter(
      member => member.profile.id !== user?.userId && blockedUserIds.has(member.profile.id)
    );
    const shouldShowSharedGroupBlockWarning =
      Boolean(group) &&
      sharedGroupBlockedMembers.length > 0 &&
      !dismissedBlockedGroupWarningByGroup[group!.id];
    const sharedGroupBlockedNames = sharedGroupBlockedMembers
      .map(member => member.profile.displayName || member.profile.username || 'người dùng đã chặn')
      .slice(0, 3)
      .join(', ');

    return (
      <>
        <div className={`${styles.column} ${className || ''}`}>
        {/* Header kênh: Có nút back quay lại danh sách Server */}
        <div className={styles.header}>
          <button className={styles.backBtn} onClick={onBack} title="Quay lại danh sách nhóm">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
          </button>
          <h2 className={styles.title}>{group?.name || 'Kênh'}</h2>

          {/* Nút Settings/Dropdown cho Group (Bước 14.2) */}
          <button
            className={styles.settingsBtn}
            onClick={() => setIsSettingsOpen(true)}
            title="Tùy chỉnh Server"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="12" cy="12" r="3" />
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z" />
            </svg>
          </button>
        </div>

        {/* === Section: Kênh văn bản (Text Channels) === */}
        {shouldShowSharedGroupBlockWarning && (
          <section className={styles.blockedGroupWarning} aria-live="polite">
            <div className={styles.blockedGroupWarningText}>
              <strong>Nhóm này có người bạn đã chặn.</strong>
              <span>
                {sharedGroupBlockedNames}
                {sharedGroupBlockedMembers.length > 3 ? ` và ${sharedGroupBlockedMembers.length - 3} người khác` : ''}
                {' '}vẫn có thể gửi tin trong các kênh chung. Tin nhắn nhóm chưa bị ẩn ở giai đoạn này.
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

        <div className={styles.sectionHeader}>
          <span className={styles.sectionTitle}>Kênh văn bản</span>
          {/* [Bước 15.2]: Nút tạo kênh mới - Chỉ hiện cho Owner/Admin */}
          {(currentUserRole === 'Owner' || currentUserRole === 'Admin') && (
            <button 
              className={styles.addChannelBtn} 
              onClick={() => setIsCreateChannelOpen(true)}
              title="Tạo kênh"
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                <line x1="12" y1="5" x2="12" y2="19" />
                <line x1="5" y1="12" x2="19" y2="12" />
              </svg>
            </button>
          )}
        </div>

        <div className={styles.roomList}>
          {textRooms.length === 0 && voiceRooms.length === 0 ? (
            <div className={styles.emptyList}>
              <p>Chưa có kênh nào.</p>
            </div>
          ) : (
            <>
              {/* Danh sách Text Channels */}
              {textRooms.map(room => (
                <div
                  key={room.id}
                  role="button"
                  tabIndex={0}
                  className={`${styles.roomItem} ${activeChat?.type === 'real' && activeChat.room.id === room.id
                    ? styles.active
                    : ''
                    }`}
                  onClick={() => {
                    void handleSelectRoom(room);
                  }}
                  onKeyDown={(event) => {
                    if (event.key !== 'Enter' && event.key !== ' ') return;
                    event.preventDefault();
                    void handleSelectRoom(room);
                  }}
                >
                  {/* Prefix # cho kênh văn bản */}
                  <div className={styles.avatar}>#</div>
                  <div className={styles.roomInfo}>
                    <span className={`${styles.roomName} ${unreadCount[room.id] > 0 ? styles.unreadBold : ''}`}>
                      {room.name || 'Unknown Channel'}
                    </span>
                  </div>
                  {unreadCount[room.id] > 0 && (
                    <div className={styles.unreadBadge}>
                      {unreadCount[room.id]}
                    </div>
                  )}
                  {canManageGroupRooms && (
                    <div className={styles.roomActions} onClick={(event) => event.stopPropagation()}>
                      <button
                        type="button"
                        className={styles.roomActionBtn}
                        title="Đổi tên phòng"
                        onClick={(event) => void handleRenameGroupRoom(room, event)}
                      >
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                          <path d="M17 3a2.85 2.85 0 0 1 4 4L7.5 20.5 2 22l1.5-5.5Z" />
                          <path d="m15 5 4 4" />
                        </svg>
                      </button>
                      <button
                        type="button"
                        className={`${styles.roomActionBtn} ${styles.deleteRoomActionBtn}`}
                        title="Xoa phong"
                        onClick={(event) => void handleDeleteTextRoom(room, event)}
                      >
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                          <path d="M3 6h18" />
                          <path d="M8 6V4h8v2" />
                          <path d="M19 6l-1 16H6L5 6" />
                          <path d="M10 11v6" />
                          <path d="M14 11v6" />
                        </svg>
                      </button>
                    </div>
                  )}
                </div>
              ))}

              {/* === Section: Kênh thoại (Voice Channels) === */}
              {voiceRooms.length > 0 && (
                <>
                  <div className={styles.sectionHeader}>
                    <span className={styles.sectionTitle}>Kênh thoại</span>
                  </div>
                  {voiceRooms.map(room => (
                    <div
                      key={room.id}
                      role="button"
                      tabIndex={0}
                      className={`${styles.roomItem} ${activeChat?.type === 'real' && activeChat.room.id === room.id
                        ? styles.active
                        : ''
                        }`}
                      onClick={() => {
                        void handleSelectRoom(room);
                      }}
                      onKeyDown={(event) => {
                        if (event.key !== 'Enter' && event.key !== ' ') return;
                        event.preventDefault();
                        void handleSelectRoom(room);
                      }}
                    >
                      {/* Icon loa cho kênh thoại (phân biệt trực quan với #) */}
                      <div className={styles.avatarVoice}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none"
                          stroke="white" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                          <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
                          <path d="M15.54 8.46a5 5 0 0 1 0 7.07" />
                        </svg>
                      </div>
                      <div className={styles.roomInfo}>
                        <span className={styles.roomName}>
                          {room.name || 'Unknown Voice Channel'}
                        </span>
                      </div>
                      {canManageGroupRooms && (
                        <div className={styles.roomActions} onClick={(event) => event.stopPropagation()}>
                          <button
                            type="button"
                            className={styles.roomActionBtn}
                            title="Đổi tên phòng"
                            onClick={(event) => void handleRenameGroupRoom(room, event)}
                          >
                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                              <path d="M17 3a2.85 2.85 0 0 1 4 4L7.5 20.5 2 22l1.5-5.5Z" />
                              <path d="m15 5 4 4" />
                            </svg>
                          </button>
                        </div>
                      )}
                    </div>
                  ))}
                </>
              )}
            </>
          )}
        </div>

        {/* VoiceStatusBar: Thanh nhỏ cuối sidebar khi đang trong phòng Voice */}
        <VoiceStatusBar />
      </div>

        {/* Modal cài đặt Server (Bước 14.3) */}
        {isSettingsOpen && group && (
          <GroupSettingsModal
            group={group}
            onClose={() => setIsSettingsOpen(false)}
            onLeaveSuccess={onBack}
            onGroupUpdated={onGroupUpdated}
          />
        )}

        {/* Modal tạo Kênh mới (Bước 15.3, 15.4) */}
        {isCreateChannelOpen && group && (
          <CreateChannelModal
            groupId={group.id}
            groupName={group.name}
            onClose={() => setIsCreateChannelOpen(false)}
            onSubmit={handleCreateChannelSubmit}
          />
        )}
      </>
    );
  }

  // [Nhánh Render: Ngữ cảnh DM]
  // Hiển thị danh sách tin nhắn cá nhân (Messenger style)

  // Sắp xếp danh sách DM theo thời gian tin nhắn mới nhất (Real-time sort)
  const sortedDmRooms = [...rooms].sort((a, b) => {
    const tA = roomMetadata[a.id]?.lastMessageTimestamp ?? a.lastMessageTimestamp ?? '';
    const tB = roomMetadata[b.id]?.lastMessageTimestamp ?? b.lastMessageTimestamp ?? '';
    return new Date(tB).getTime() - new Date(tA).getTime();
  });

  return (
    <>
      <div className={`${styles.column} ${className || ''}`}>
      <div className={styles.header}>
        <h2 className={styles.title}>Tin nhắn</h2>
      </div>

      {/* Thanh tìm kiếm người dùng mới */}
      <button className={styles.searchBar} onClick={() => setIsSearchOpen(true)}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <circle cx="11" cy="11" r="8" />
          <line x1="21" y1="21" x2="16.65" y2="16.65" />
        </svg>
        <span>Tìm kiếm hoặc bắt đầu trò chuyện mới</span>
      </button>

      <div className={styles.roomList}>
        {sortedDmRooms.length === 0 ? (
          <div className={styles.emptyList}>
            <p>Chưa có cuộc trò chuyện nào.</p>
            <p>Nhấn vào ô tìm kiếm để bắt đầu!</p>
          </div>
        ) : (
          sortedDmRooms.map(room => (
            <button
              key={room.id}
              className={`${styles.roomItem} ${activeChat?.type === 'real' && activeChat.room.id === room.id
                ? styles.active
                : ''
                }`}
              onClick={() => {
                void handleSelectRoom(room);
              }}
            >
              <div className={styles.avatar}>
                <DmRoomAvatar room={room} />
              </div>
              <div className={styles.roomInfo}>
                <div className={styles.roomNameRow}>
                  <span className={`${styles.roomName} ${unreadCount[room.id] > 0 ? styles.unreadBold : ''}`}>
                    {room.otherUserDisplayName ?? room.name ?? 'Unknown'}
                  </span>
                  {(roomMetadata[room.id]?.lastMessageTimestamp ?? room.lastMessageTimestamp) && (
                    <span className={styles.msgTime}>
                      {formatTime(roomMetadata[room.id]?.lastMessageTimestamp ?? room.lastMessageTimestamp)}
                    </span>
                  )}
                </div>

                {getAllowedDmPresence(room.otherUserId) && (
                  <span className={styles.presenceLine}>
                    <span
                      className={getAllowedDmPresence(room.otherUserId)?.isOnline ? styles.onlineDot : styles.offlineDot}
                      aria-hidden="true"
                    />
                    {getAllowedDmPresence(room.otherUserId)?.isOnline
                      ? 'Online'
                      : formatLastSeen(getAllowedDmPresence(room.otherUserId)?.lastSeenAt)}
                  </span>
                )}

                {/* Hiển thị nội dung tin nhắn cuối cùng (Ưu tiên bản cập nhật real-time qua store) */}
                {(roomMetadata[room.id]?.lastMessageContent ?? room.lastMessageContent) ? (
                  <span className={`${styles.roomSub} ${unreadCount[room.id] > 0 ? styles.unreadBoldSub : ''}`}>
                    {roomMetadata[room.id]?.lastMessageContent ?? room.lastMessageContent}
                  </span>
                ) : (
                  <span className={styles.roomSub}>@{room.otherUserUsername}</span>
                )}
              </div>

              {unreadCount[room.id] > 0 && (
                <div className={styles.unreadBadge}>
                  {unreadCount[room.id]}
                </div>
              )}
            </button>
          ))
        )}
      </div>
    </div>

      {/* Modal tìm kiếm người dùng để bắt đầu DM mới */}
      {isSearchOpen && (
        <UserSearchModal
          onClose={() => setIsSearchOpen(false)}
          onSelectUser={handleSelectFromSearch}
        />
      )}
    </>
  );
};

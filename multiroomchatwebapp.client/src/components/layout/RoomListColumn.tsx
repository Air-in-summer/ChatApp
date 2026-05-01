import { useState, useEffect, useRef, useCallback } from 'react';
import { useAuth } from '../../context/AuthContext';
import { createAuthClient } from '../../api/apiClient';
import { UserSearchModal } from '../discovery/UserSearchModal';
import { useChatStore } from '../../store/useChatStore';
import type { ActiveChat, RoomDto, UserSearchResult } from '../../types/chat';
import styles from './RoomListColumn.module.css';

interface RoomListColumnProps {
  context: 'dm' | 'group';
  activeChat: ActiveChat | null;
  onSelectChat: (chat: ActiveChat) => void;
}

/**
 * Cột 2: Danh sách phòng chat và ô tìm kiếm người dùng.
 */
export const RoomListColumn = ({ context, activeChat, onSelectChat }: RoomListColumnProps) => {
  const { accessToken } = useAuth();
  const [rooms, setRooms] = useState<RoomDto[]>([]);
  const [isSearchOpen, setIsSearchOpen] = useState(false);

  // Hook lấy dữ liệu unread và metadata sort phòng
  const unreadCount = useChatStore(state => state.unreadCount);
  const clearUnread = useChatStore(state => state.clearUnread);
  const setInitialUnreadCounts = useChatStore(state => state.setInitialUnreadCounts);
  const setInitialLastReadIds = useChatStore(state => state.setInitialLastReadIds);
  // roomMetadata: lưu lastMessage real-time để sort danh sách phòng mà không re-fetch API
  const roomMetadata = useChatStore(state => state.roomMetadata);

  // Format thời gian hiển thị (VD: 14:30 hoặc Hôm qua)
  const formatTime = (isoString?: string) => {
    if (!isoString) return '';
    const date = new Date(isoString);
    const today = new Date();
    if (date.toDateString() === today.toDateString()) {
      return date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
    }
    return date.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' });
  };

  // Lấy danh sách phòng khi component mount
  useEffect(() => {
    if (!accessToken) return;

    const fetchRooms = async () => {
      try {
        const authClient = createAuthClient(accessToken);
        const response = await authClient.get<RoomDto[]>('/api/v1/rooms/my-rooms');
        setRooms(response.data);
        
        // Khởi tạo unread count VÀ last read ids vào store
        const unreadMap: Record<string, number> = {};
        const lastReadMap: Record<string, string> = {};
        response.data.forEach(r => {
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
    };

    fetchRooms();
  }, [accessToken, setInitialUnreadCounts]);

  // Ref giữ snapshot rooms mới nhất để đọc trong effect mà không tạo dependency gây loop
  const roomsRef = useRef<RoomDto[]>([]);
  useEffect(() => {
    roomsRef.current = rooms;
  }, [rooms]);

  // Hàm refresh danh sách phòng (tách ra để dùng lại ở 2 effect bên dưới)
  const refreshRooms = useCallback(async () => {
    if (!accessToken) return;
    try {
      const authClient = createAuthClient(accessToken);
      const response = await authClient.get<RoomDto[]>('/api/v1/rooms/my-rooms');
      setRooms(response.data);
      // Sync unread VÀ last read từ API (chỉ làm giá trị khởi tạo, không ghi đè real-time)
      const unreadMap: Record<string, number> = {};
      const lastReadMap: Record<string, string> = {};
      response.data.forEach(r => {
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
      console.error('Không thể re-fetch danh sách phòng:', err);
    }
  }, [accessToken, setInitialUnreadCounts]);

  // Effect 1: Re-fetch khi activeChat chuyển sang real room chưa có trong danh sách (vừa tạo phòng)
  // rooms KHÔNG nằm trong dependency → đọc qua roomsRef tránh infinite loop
  useEffect(() => {
    if (!accessToken) return;
    if (activeChat?.type !== 'real') return;
    if (roomsRef.current.some(r => r.id === activeChat.room.id)) return;
    refreshRooms();
  }, [activeChat, accessToken, refreshRooms]);

  // Effect 2: Re-fetch khi xuất hiện roomId hoàn toàn mới trong unreadCount (phòng chưa có trong list)
  // rooms KHÔNG nằm trong dependency → đọc qua roomsRef tránh infinite loop
  useEffect(() => {
    if (!accessToken) return;
    const hasUnknownRoom = Object.keys(unreadCount).some(
      roomId => unreadCount[roomId] > 0 && !roomsRef.current.some(r => r.id === roomId)
    );
    if (hasUnknownRoom) refreshRooms();
  }, [unreadCount, accessToken, refreshRooms]);

  // Khi user chọn một phòng trong danh sách (đã có phòng thật)
  const handleSelectRoom = (room: RoomDto) => {
    clearUnread(room.id);
    onSelectChat({ type: 'real', room });
  };

  // Khi user chọn một người từ UserSearchModal
  const handleSelectFromSearch = (targetUser: UserSearchResult) => {
    // Kiểm tra xem đã có phòng thật với người này chưa
    const existingRoom = rooms.find(
      r => r.type === 'DirectMessage' && r.otherUserUsername === targetUser.username
    );

    if (existingRoom) {
      // Có phòng thật → dùng luôn
      onSelectChat({ type: 'real', room: existingRoom });
    } else {
      // Chưa có → tạo phòng ảo (Virtual Room)
      onSelectChat({ type: 'virtual', targetUser });
    }

    setIsSearchOpen(false);
  };

  // Filter chỉ hiện phòng DM (context = dm)
  const dmRooms = rooms.filter(r => r.type === 'DirectMessage');

  // Sort real-time: ưu tiên timestamp từ roomMetadata (real-time), fallback về lastMessageTimestamp từ API
  const sortedDmRooms = [...dmRooms].sort((a, b) => {
    const tA = roomMetadata[a.id]?.lastMessageTimestamp ?? a.lastMessageTimestamp ?? '';
    const tB = roomMetadata[b.id]?.lastMessageTimestamp ?? b.lastMessageTimestamp ?? '';
    return new Date(tB).getTime() - new Date(tA).getTime();
  });

  if (context === 'group') {
    return (
      <div className={styles.column}>
        <div className={styles.placeholder}>
          <p>Tính năng Nhóm đang được phát triển</p>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.column}>
      {/* Header với nút Search */}
      <div className={styles.header}>
        <h2 className={styles.title}>Tin nhắn</h2>
      </div>

      {/* Ô Search - click để mở Modal */}
      <button className={styles.searchBar} onClick={() => setIsSearchOpen(true)}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <circle cx="11" cy="11" r="8" />
          <line x1="21" y1="21" x2="16.65" y2="16.65" />
        </svg>
        <span>Tìm kiếm hoặc bắt đầu trò chuyện mới</span>
      </button>

      {/* Danh sách phòng */}
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
              className={`${styles.roomItem} ${
                activeChat?.type === 'real' && activeChat.room.id === room.id
                  ? styles.active
                  : ''
              }`}
              onClick={() => handleSelectRoom(room)}
            >
              {/* Avatar chữ cái đầu */}
              <div className={styles.avatar}>
                {(room.otherUserDisplayName || '?')[0]?.toUpperCase() || '?'}
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
                
                {(roomMetadata[room.id]?.lastMessageContent ?? room.lastMessageContent) ? (
                  <span className={`${styles.roomSub} ${unreadCount[room.id] > 0 ? styles.unreadBoldSub : ''}`}>
                    {roomMetadata[room.id]?.lastMessageContent ?? room.lastMessageContent}
                  </span>
                ) : (
                  <span className={styles.roomSub}>@{room.otherUserUsername}</span>
                )}
              </div>
              
              {/* Dấu chấm đỏ tin nhắn chưa đọc */}
              {unreadCount[room.id] > 0 && (
                <div className={styles.unreadBadge}>
                  {unreadCount[room.id]}
                </div>
              )}
            </button>
          ))
        )}
      </div>

      {/* Modal tìm kiếm người dùng */}
      {isSearchOpen && (
        <UserSearchModal
          onClose={() => setIsSearchOpen(false)}
          onSelectUser={handleSelectFromSearch}
        />
      )}
    </div>
  );
};

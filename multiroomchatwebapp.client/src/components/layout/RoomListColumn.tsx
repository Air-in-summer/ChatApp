import { useState, useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { createAuthClient } from '../../api/apiClient';
import { UserSearchModal } from '../discovery/UserSearchModal';
import type { ActiveChat, RoomDto } from '../../types/chat';
import styles from './RoomListColumn.module.css';

interface RoomListColumnProps {
  context: 'dm' | 'group';
  activeChat: ActiveChat | null;
  onSelectChat: (chat: ActiveChat) => void;
}

/**
 * Cột 2: Danh sách phòng chat và ô tìm kiếm người dùng.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Khi mount, gọi API /api/v1/rooms/my-rooms để lấy danh sách phòng có tin nhắn.
 * 2. Ô Search ở trên cùng: click mở UserSearchModal.
 * 3. Khi user chọn ai đó trong Modal → tạo ActiveChat dạng 'virtual' → callback onSelectChat.
 * 4. Nếu người đó đã có phòng thật (trong roomList), tạo dạng 'real' thay vì 'virtual'.
 */
export const RoomListColumn = ({ context, activeChat, onSelectChat }: RoomListColumnProps) => {
  const { accessToken } = useAuth();
  const [rooms, setRooms] = useState<RoomDto[]>([]);
  const [isSearchOpen, setIsSearchOpen] = useState(false);

  // Lấy danh sách phòng khi component mount
  useEffect(() => {
    if (!accessToken) return;

    const fetchRooms = async () => {
      try {
        const authClient = createAuthClient(accessToken);
        const response = await authClient.get<RoomDto[]>('/api/v1/rooms/my-rooms');
        setRooms(response.data);
      } catch (err) {
        console.error('Không thể lấy danh sách phòng:', err);
      }
    };

    fetchRooms();
  }, [accessToken]);

  // Khi user chọn một phòng trong danh sách (đã có phòng thật)
  const handleSelectRoom = (room: RoomDto) => {
    onSelectChat({ type: 'real', room });
  };

  // Khi user chọn một người từ UserSearchModal
  const handleSelectFromSearch = (targetUser: { userId: string; username: string; displayName: string }) => {
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
        {dmRooms.length === 0 ? (
          <div className={styles.emptyList}>
            <p>Chưa có cuộc trò chuyện nào.</p>
            <p>Nhấn vào ô tìm kiếm để bắt đầu!</p>
          </div>
        ) : (
          dmRooms.map(room => (
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
                {(room.otherUserDisplayName ?? '?')[0].toUpperCase()}
              </div>
              <div className={styles.roomInfo}>
                <span className={styles.roomName}>
                  {room.otherUserDisplayName ?? room.name ?? 'Unknown'}
                </span>
                <span className={styles.roomSub}>@{room.otherUserUsername}</span>
              </div>
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

import { lazy, Suspense, useState, useEffect, useRef } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { NavColumn } from './NavColumn';
import type { NavContext } from './NavColumn';
import { UserProfileModal } from './UserProfileModal';
import { RoomListColumn } from './RoomListColumn';
import { ChatColumn } from './ChatColumn';
import { GroupListPanel } from '../group/GroupListPanel';
import { FriendsPanel } from '../friends/FriendsPanel';
import { CallEventToast } from '../call/CallEventToast';
import { DirectCallOverlay } from '../call/DirectCallOverlay';
import { IncomingCallToast } from '../call/IncomingCallToast';
import { VoiceAudioSink } from '../call/VoiceAudioSink';
import { useSignalR } from '../../hooks/useSignalR';
import { useAuth } from '../../context/AuthContext';
import { useChatStore } from '../../store/useChatStore';
import { useNotificationStore } from '../../store/useNotificationStore';
import toast from 'react-hot-toast';
import type { ActiveChat, RoomDto } from '../../types/chat';
import type { GroupDto } from '../../types/group';
import styles from './MainLayout.module.css';

const VoiceRoomPanel = lazy(() =>
  import('./VoiceRoomPanel').then((module) => ({ default: module.VoiceRoomPanel }))
);

/**
 * Layout chính của ứng dụng Dashboard.
 * Quản lý cấu trúc 3 cột và điều hướng giữa các ngữ cảnh (DM ↔ Group).
 *
 * @remarks
 * Luồng xử lý:
 * 1. Khởi tạo SignalR connection (Singleton) dùng chung cho toàn bộ dashboard.
 * 2. Theo dõi state navContext để chuyển đổi giữa tin nhắn cá nhân (DM) và nhóm (Group).
 * 3. Theo dõi selectedGroup để xác định đang xem danh sách nhóm hay chi tiết 1 nhóm.
 * 4. Đồng bộ activeChat vào global store để các hooks SignalR biết context phòng hiện tại.
 */
export const MainLayout = () => {
  // navContext: 'dm' (Tin nhắn cá nhân) hoặc 'group' (Máy chủ/Nhóm)
  const [navContext, setNavContext] = useState<NavContext>('dm');
  // activeChat: Phòng chat đang mở ở Cột 3
  const [activeChat, setActiveChat] = useState<ActiveChat | null>(null);
  // selectedGroup: Thông tin Server đang được chọn (null nghĩa là đang xem danh sách Server)
  const [selectedGroup, setSelectedGroup] = useState<GroupDto | null>(null);
  const [isProfileModalOpen, setIsProfileModalOpen] = useState(false);
  const { user } = useAuth();

  // Hook dùng chung khởi tạo kết nối SignalR (Singleton lifecycle bound to MainLayout)
  const { sendMessage, sendTyping, stopTyping, markAsRead, joinRoom } = useSignalR();
  const setActiveRoomId = useChatStore(state => state.setActiveRoomId);
  const location = useLocation();
  const navigate = useNavigate();

  // [Bước 16.4c] Flag bảo vệ: Skip lần reset đầu tiên khi redirect từ invite link
  // Mặc định = false → useEffect reset DM ↔ Group hoạt động bình thường
  const skipNavResetRef = useRef(false);

  // [Bước 16.4b] Đọc Navigation State từ JoinGroupPage redirect
  // Chỉ chạy 1 lần khi mount. Nếu có joinedGroup → chuyển thẳng vào Server đó.
  // Backward-compatible: khi vào '/' bình thường → state = null → if không chạy.
  useEffect(() => {
    const state = location.state as { joinedGroup?: GroupDto } | null;
    if (state?.joinedGroup) {
      // Bật flag để skip lần reset khi navContext đổi từ 'dm' → 'group'
      skipNavResetRef.current = true;
      setNavContext('group');
      setSelectedGroup(state.joinedGroup);
      // Xóa state khỏi history để tránh re-trigger khi user bấm F5
      navigate('/', { replace: true, state: {} });
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // [Luồng: Đồng bộ Active Room]
  // Đồng bộ activeChat sang global store để useSignalR biết phòng nào đang mở để handle real-time events
  useEffect(() => {
    if (activeChat?.type === 'real') {
      setActiveRoomId(activeChat.room.id);
    } else {
      setActiveRoomId(null);
    }
  }, [activeChat, setActiveRoomId]);

  // [Luồng: Reset điều hướng]
  // Khi chuyển qua lại giữa DM và Group trên NavColumn, reset trạng thái chọn để tránh chồng chéo dữ liệu
  // [Bước 16.4c] Nếu skipNavResetRef = true (đến từ invite link), skip 1 lần rồi tắt flag
  useEffect(() => {
    if (skipNavResetRef.current) {
      skipNavResetRef.current = false;
      return;
    }
    setSelectedGroup(null);
    setActiveChat(null);
  }, [navContext]);

  // [Notification] Xử lý khi bị Kick khỏi Group
  const kickedFromGroup = useNotificationStore(s => s.kickedFromGroup);
  const clearKicked = useNotificationStore(s => s.clearKicked);

  useEffect(() => {
    if (!kickedFromGroup) return;

    toast.error(`Bạn đã bị mời ra khỏi "${kickedFromGroup.groupName}"`);

    // Nếu đang ở trong Group bị kick → văng ra danh sách Server
    if (selectedGroup?.id === kickedFromGroup.groupId) {
      setSelectedGroup(null);
      setActiveChat(null);
    }

    clearKicked();
  }, [kickedFromGroup, selectedGroup, clearKicked]);

  // [Notification] Xử lý khi Group bị giải tán
  const deletedGroup = useNotificationStore(s => s.deletedGroup);
  const clearGroupDeleted = useNotificationStore(s => s.clearGroupDeleted);

  useEffect(() => {
    if (!deletedGroup) return;

    toast.error(`Server "${deletedGroup.groupName}" đã bị giải tán.`);

    // Nếu đang ở trong Group bị xóa → văng ra danh sách Server
    if (selectedGroup?.id === deletedGroup.groupId) {
      setSelectedGroup(null);
      setActiveChat(null);
    }

    clearGroupDeleted();
  }, [deletedGroup, selectedGroup, clearGroupDeleted]);

  /**
   * Xử lý khi một phòng DM ảo (Virtual) được tạo thật trong Database sau tin nhắn đầu tiên.
   * Giúp RoomListColumn tải lại danh sách mà không mất trạng thái chat hiện tại.
   */
  const handleChatEvolved = (realRoom: RoomDto) => {
    setActiveChat({ type: 'real', room: realRoom });
  };

  /** Chuyển sang xem chi tiết một Server */
  const handleSelectGroup = (group: GroupDto) => {
    setSelectedGroup(group);
    setActiveChat(null); // Reset chat cũ khi vào Server mới
  };

  /** Quay lại danh sách Server (MS Teams style) */
  const handleBackToGroupList = () => {
    setSelectedGroup(null);
    setActiveChat(null);
  };

  return (
    <div className={styles.dashboardLayout}>
      {/* Cột 1: Điều hướng (Thanh dọc ngoài cùng bên trái) */}
      <NavColumn
        activeContext={navContext}
        onContextChange={setNavContext}
        onProfileClick={() => setIsProfileModalOpen(true)}
        profileAvatarUrl={user?.avatarUrl}
        profileDisplayName={user?.displayName}
        profileUsername={user?.username}
      />

      {/* [Nhánh 1]: Tin nhắn cá nhân (DM) */}
      {navContext === 'friends' && (
        <FriendsPanel />
      )}

      {navContext === 'dm' && (
        <>
          <RoomListColumn
            context="dm"
            activeChat={activeChat}
            onSelectChat={setActiveChat}
            className={styles.roomListColumn}
          />
          <ChatColumn
            activeChat={activeChat}
            onChatEvolvedToReal={handleChatEvolved}
            sendMessage={sendMessage}
            sendTyping={sendTyping}
            stopTyping={stopTyping}
            markAsRead={markAsRead}
            joinRoom={joinRoom}
            className={styles.chatColumn}
          />
        </>
      )}

      {/* [Nhánh 2]: Danh sách các Server (Nhóm) - Kiểu MS Teams */}
      {navContext === 'group' && !selectedGroup && (
        <GroupListPanel onSelectGroup={handleSelectGroup} />
      )}

      {/* [Nhánh 3]: Chi tiết một Server (Channels + Chat) - Kiểu Discord */}
      {navContext === 'group' && selectedGroup && (
        <>
          <RoomListColumn
            context="group"
            group={selectedGroup}
            onBack={handleBackToGroupList}
            activeChat={activeChat}
            onSelectChat={setActiveChat}
            className={styles.roomListColumn}
          />
          {/* Routing Voice ↔ Text: nếu room là Voice → hiện VoiceRoomPanel, ngược lại → ChatColumn */}
          {activeChat?.type === 'real' && activeChat.room.type === 'Voice' ? (
            <Suspense
              fallback={
                <div className={`${styles.chatColumn} ${styles.emptyState}`}>
                  <div className={styles.spinner} />
                  <p>Đang tải Voice...</p>
                </div>
              }
            >
              <VoiceRoomPanel
                roomId={activeChat.room.id}
                roomName={activeChat.room.name || 'Voice Channel'}
                groupId={activeChat.room.groupId}
                isPrivate={activeChat.room.isPrivate}
                className={styles.chatColumn}
              />
            </Suspense>
          ) : (
            <ChatColumn
              activeChat={activeChat}
              onChatEvolvedToReal={handleChatEvolved}
              sendMessage={sendMessage}
              sendTyping={sendTyping}
              stopTyping={stopTyping}
              markAsRead={markAsRead}
              joinRoom={joinRoom}
              className={styles.chatColumn}
            />
          )}
        </>
      )}

      <IncomingCallToast />
      <CallEventToast />
      <VoiceAudioSink />
      <DirectCallOverlay />
      {isProfileModalOpen && (
        <UserProfileModal onClose={() => setIsProfileModalOpen(false)} />
      )}
    </div>
  );
};

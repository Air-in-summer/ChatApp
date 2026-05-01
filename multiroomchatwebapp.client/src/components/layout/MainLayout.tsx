import { useState, useEffect } from 'react';
import { NavColumn } from './NavColumn';
import { RoomListColumn } from './RoomListColumn';
import { ChatColumn } from './ChatColumn';
import { useSignalR } from '../../hooks/useSignalR';
import { useChatStore } from '../../store/useChatStore';
import type { ActiveChat, RoomDto } from '../../types/chat';
import styles from './MainLayout.module.css';

export const MainLayout = () => {
  const [navContext, setNavContext] = useState<'dm' | 'group'>('dm');
  const [activeChat, setActiveChat] = useState<ActiveChat | null>(null);

  // Hook dùng chung khởi tạo kết nối SignalR (Singleton lifecycle bound to MainLayout)
  const { sendMessage, sendTyping, stopTyping, markAsRead, joinRoom } = useSignalR();
  const setActiveRoomId = useChatStore(state => state.setActiveRoomId);

  // Đồng bộ activeChat sang global store để useSignalR biết phòng nào đang mở
  useEffect(() => {
    if (activeChat?.type === 'real') {
      setActiveRoomId(activeChat.room.id);
    } else {
      setActiveRoomId(null);
    }
  }, [activeChat, setActiveRoomId]);

  // Được gọi bởi ChatColumn khi nó vừa gọi API GetOrCreateDirectRoom thành công
  // Việc đổi activeChat thành type 'real' sẽ trigger RoomListColumn ở Cột 2 tải lại phòng
  const handleChatEvolved = (realRoom: RoomDto) => {
    setActiveChat({ type: 'real', room: realRoom });
  };

  return (
    <div className={styles.dashboardLayout}>
      {/* Cột 1: Điều hướng */}
      <NavColumn
        activeContext={navContext}
        onContextChange={setNavContext}
      />

      {/* Cột 2: Danh sách */}
      <RoomListColumn
        context={navContext}
        activeChat={activeChat}
        onSelectChat={setActiveChat}
      />

      {/* Cột 3: Giao diện Chat */}
      <ChatColumn 
        activeChat={activeChat}
        onChatEvolvedToReal={handleChatEvolved}
        sendMessage={sendMessage}
        sendTyping={sendTyping}
        stopTyping={stopTyping}
        markAsRead={markAsRead}
        joinRoom={joinRoom}
      />
    </div>
  );
};

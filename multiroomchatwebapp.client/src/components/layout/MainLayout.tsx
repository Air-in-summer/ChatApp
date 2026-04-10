import { useState } from 'react';
import { NavColumn } from './NavColumn';
import { RoomListColumn } from './RoomListColumn';
import { ChatColumn } from './ChatColumn';
import type { ActiveChat, RoomDto } from '../../types/chat';
import styles from './MainLayout.module.css';

export const MainLayout = () => {
  const [navContext, setNavContext] = useState<'dm' | 'group'>('dm');
  const [activeChat, setActiveChat] = useState<ActiveChat | null>(null);

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
      />
    </div>
  );
};

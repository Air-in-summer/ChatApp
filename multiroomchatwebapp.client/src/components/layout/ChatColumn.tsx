import { useState, useRef, useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { createAuthClient } from '../../api/apiClient';
import { signalRService } from '../../services/signalRService';
import type { ActiveChat, RoomDto } from '../../types/chat';
import styles from './ChatColumn.module.css';

interface ChatColumnProps {
  activeChat: ActiveChat | null;
  /** Callback trả ngược về Layout để update Cột 2 (VD: đang ảo gõ enter -> thành room thật) */
  onChatEvolvedToReal: (realRoom: RoomDto) => void;
}

export const ChatColumn = ({ activeChat, onChatEvolvedToReal }: ChatColumnProps) => {
  const { accessToken } = useAuth();
  const [messages, setMessages] = useState<any[]>([]); // Sẽ define Message Type chuẩn sau
  const [inputText, setInputText] = useState('');
  const [isSendingFirstMessage, setIsSendingFirstMessage] = useState(false);
  const endOfMessagesRef = useRef<HTMLDivElement>(null);

  // Hook 1: Kết nối SignalR khi Auth thành công và dọn dẹp khi Unmount
  useEffect(() => {
    if (accessToken) {
      signalRService.startConnection(accessToken);
    }
    return () => {
      signalRService.stopConnection();
    };
  }, [accessToken]);

  // Hook 2: Lắng nghe tin nhắn bay về
  useEffect(() => {
    const unsub = signalRService.onMessageReceived((msg) => {
      setMessages(prev => [...prev, msg]);
      setTimeout(() => endOfMessagesRef.current?.scrollIntoView({ behavior: 'smooth' }), 100);
    });
    return () => { unsub(); }; // cleanup
  }, []);

  // Hook 3: Xoá màn hình khi chuyển sang Chat của người khác
  useEffect(() => {
    setMessages([]);
    setInputText('');
  }, [activeChat]);

  // HÀM XỬ LÝ SỰ KIỆN: BẤM ENTER GỬI TIN MÀU NHIỆM
  const handleSendMessage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputText.trim() || !activeChat || !accessToken) return;

    const contentToSend = inputText.trim();
    setInputText(''); // Reset giao diện ngay lập tức

    // Fake hiển thị ngay bên Chat bong bóng phía mình để UX mượt
    setMessages(prev => [...prev, { content: contentToSend, isMine: true }]);
    setTimeout(() => endOfMessagesRef.current?.scrollIntoView({ behavior: 'smooth' }), 50);

    try {
      if (activeChat.type === 'virtual') {
        // [MAGICAL FLOW] - Giờ mới bắt đầu tạo phòng
        setIsSendingFirstMessage(true);
        const targetUserId = activeChat.targetUser.id; // Chú ý: Backend map vào ID đổi ở bước trước
        
        // Cú gọi API Idempotent, không sợ double nếu lỡ spam
        // [SỬA LỖI 401]: Dùng createAuthClient thay vì apiClient gốc
        const roomRes = await createAuthClient(accessToken).post<RoomDto>(`/api/v1/rooms/direct/${targetUserId}`);
        const realRoom = roomRes.data;

        // [SỬA LỖI Chữ Unknown]: API GetOrCreateDirectRoom ở Backend không query DB để lấy tên người kia
        // nhằm tiết kiệm tài nguyên. Frontend đã biết sẵn tên nên chỉ việc đắp qua để dùng liền!
        realRoom.otherUserDisplayName = activeChat.targetUser.displayName;

        // Bão ngay cho cha (MainLayout) chuyển tao sang RealRoom để Cột 2 có tác dụng theo
        onChatEvolvedToReal(realRoom);

        // Phát sóng bằng SignalR dùng ID tươi rới mới tinh
        await signalRService.sendMessage(realRoom.id, contentToSend);
        
      } else {
        // Luồng chat bình thường, phòng đã tồn tại!
        await signalRService.sendMessage(activeChat.room.id, contentToSend);
      }
    } catch (error) {
      console.error("Gửi tin thất bại", error);
    } finally {
      setIsSendingFirstMessage(false);
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
      : activeChat.room.otherUserDisplayName ?? "Unknown";

  return (
    <div className={styles.chatColumn}>
      {/* Header Room Info */}
      <div className={styles.header}>
        <div className={styles.avatar}>
          {headerName[0]?.toUpperCase()}
        </div>
        <div className={styles.userInfo}>
          <h2>{headerName}</h2>
          {isVirtual && <span className={styles.badge}>Chưa có cuộc hội thoại nào</span>}
        </div>
      </div>

      {/* Main Message List */}
      <div className={styles.messageList}>
        {messages.length === 0 && (
          <div className={styles.firstMsgPrompt}>
            Hãy là người tiên phong gửi lời chào đến {headerName}! 🤗
          </div>
        )}

        {messages.map((msg, idx) => (
          <div key={idx} className={`${styles.messageWrapper} ${msg.isMine ? styles.mine : styles.theirs}`}>
            <div className={styles.bubble}>
              {msg.content}
            </div>
          </div>
        ))}
        {/* Điểm neo để tự động cuộn xuống cùng */}
        <div ref={endOfMessagesRef} />
      </div>

      {/* Input Form Textbox */}
      <form onSubmit={handleSendMessage} className={styles.inputArea}>
        <input 
          type="text" 
          value={inputText}
          onChange={e => setInputText(e.target.value)}
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
            <line x1="22" y1="2" x2="11" y2="13"/>
            <polygon points="22 2 15 22 11 13 2 9 22 2"/>
          </svg>
        </button>
      </form>
    </div>
  );
};

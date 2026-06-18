import { useVoiceConnection } from '../../hooks/useVoiceConnection';
import { useVoiceStore } from '../../store/useVoiceStore';
import styles from './VoiceRoomPanel.module.css';

/**
 * Thanh trang thai voice global o cuoi sidebar.
 * Hien voice channel hoac DM call tuy theo `activeSession`.
 */
export const VoiceStatusBar = () => {
  const connectionStatus = useVoiceStore((s) => s.connectionStatus);
  const currentVoiceRoomName = useVoiceStore((s) => s.currentVoiceRoomName);
  const activeSession = useVoiceStore((s) => s.activeSession);
  const { leaveActiveVoiceSession } = useVoiceConnection();

  const handleLeaveVoiceRoom = () => {
    void leaveActiveVoiceSession().catch((error) => {
      console.error('Khong the roi phien voice hien tai:', error);
    });
  };

  if (connectionStatus === 'idle') return null;

  const isConnected = connectionStatus === 'connected';
  const isConnecting = connectionStatus === 'connecting';
  const isReconnecting = connectionStatus === 'reconnecting';
  const isError = connectionStatus === 'error';
  const statusClass = isConnected
    ? styles.connected
    : isConnecting
      ? styles.connecting
      : isReconnecting
        ? styles.reconnecting
        : styles.error;
  const statusLabel = activeSession?.kind === 'direct-call' ? 'Đang gọi' : 'Đã kết nối voice';
  const displayName = activeSession?.displayName || currentVoiceRoomName || (
    activeSession?.kind === 'direct-call' ? 'Cuộc gọi riêng' : 'Kênh thoại'
  );

  return (
    <div className={styles.voiceStatusBar}>
      <div className={styles.voiceStatusInfo}>
        <div className={styles.voiceStatusLabel}>
          <span className={`${styles.statusDot} ${statusClass}`} />
          {isConnected && statusLabel}
          {isConnecting && 'Đang kết nối...'}
          {isReconnecting && 'Đang kết nối lại...'}
          {isError && 'Lỗi kết nối'}
        </div>
        <div className={styles.voiceStatusRoom}>
          {displayName}
        </div>
      </div>

      <button
        className={styles.voiceStatusDisconnect}
        onClick={handleLeaveVoiceRoom}
        title="Ngắt kết nối voice"
        aria-label="Ngắt kết nối voice"
      >
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7 2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91" />
          <line x1="1" y1="1" x2="23" y2="23" />
        </svg>
      </button>
    </div>
  );
};

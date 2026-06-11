import { useState } from 'react';
import toast from 'react-hot-toast';
import { startDirectCall } from '../../api/voiceApi';
import { useAuth } from '../../context/AuthContext';
import { useVoiceConnection } from '../../hooks/useVoiceConnection';
import styles from './DirectCallButton.module.css';

interface DirectCallButtonProps {
  dmRoomId: string;
  displayName: string;
}

export const DirectCallButton = ({ dmRoomId, displayName }: DirectCallButtonProps) => {
  const { isAuthenticated } = useAuth();
  const {
    joinDirectCallSession,
    shouldSwitchVoiceSession,
    leaveCurrentVoiceSessionForSwitch,
  } = useVoiceConnection();
  const [isCalling, setIsCalling] = useState(false);

  const handleStartCall = async () => {
    if (!isAuthenticated || isCalling) return;

    if (!shouldSwitchVoiceSession()) {
      return;
    }

    setIsCalling(true);

    try {
      const response = await startDirectCall(dmRoomId);
      await leaveCurrentVoiceSessionForSwitch(response.session.sessionId);
      await joinDirectCallSession(response, displayName || 'DM call');
    } catch (error) {
      toast.error('Không thể bắt đầu cuộc gọi.');
      console.error('Không thể bắt đầu DM call:', error);
    } finally {
      setIsCalling(false);
    }
  };

  return (
    <button
      type="button"
      className={`${styles.callButton} ${isCalling ? styles.calling : ''}`}
      onClick={handleStartCall}
      disabled={!isAuthenticated || isCalling}
      title={`Gọi ${displayName}`}
      aria-label={`Gọi ${displayName}`}
    >
      <svg
        width="19"
        height="19"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.4"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-6-6A19.79 19.79 0 0 1 2.12 4.18 2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.12.9.33 1.77.63 2.61a2 2 0 0 1-.45 2.11L8.09 9.64a16 16 0 0 0 6.27 6.27l1.2-1.2a2 2 0 0 1 2.11-.45c.84.3 1.71.51 2.61.63A2 2 0 0 1 22 16.92z" />
      </svg>
    </button>
  );
};

import { useEffect, useRef, useState } from 'react';
import type { CSSProperties, PointerEvent as ReactPointerEvent } from 'react';
import toast from 'react-hot-toast';
import { acceptVoiceSession, declineVoiceSession } from '../../api/voiceApi';
import { useAuth } from '../../context/AuthContext';
import { useVoiceConnection } from '../../hooks/useVoiceConnection';
import { useVoiceStore } from '../../store/useVoiceStore';
import styles from './IncomingCallToast.module.css';

export const IncomingCallToast = () => {
  const { accessToken } = useAuth();
  const incomingCall = useVoiceStore((s) => s.incomingCall);
  const clearIncomingCall = useVoiceStore((s) => s.clearIncomingCall);
  const {
    joinDirectCallSession,
    shouldSwitchVoiceSession,
    leaveCurrentVoiceSessionForSwitch,
  } = useVoiceConnection();
  const [isAccepting, setIsAccepting] = useState(false);
  const [isDeclining, setIsDeclining] = useState(false);
  const [position, setPosition] = useState({ x: 24, y: 24 });
  const dragOffsetRef = useRef({ x: 0, y: 0 });
  const isDraggingRef = useRef(false);

  useEffect(() => {
    if (!incomingCall) return;

    const panelWidth = Math.min(360, window.innerWidth - 32);
    setPosition({
      x: Math.max(16, window.innerWidth - panelWidth - 24),
      y: 24,
    });
  }, [incomingCall?.session.sessionId]);

  if (!incomingCall) return null;

  const callerName = incomingCall.callerDisplayName || 'Người dùng';
  const avatarText = callerName.trim().charAt(0).toUpperCase() || '?';
  const isBusy = isAccepting || isDeclining;
  const panelStyle: CSSProperties = {
    left: position.x,
    top: position.y,
  };

  const movePanel = (clientX: number, clientY: number) => {
    const panelWidth = Math.min(360, window.innerWidth - 32);
    const panelHeight = 162;
    const nextX = Math.min(
      Math.max(8, clientX - dragOffsetRef.current.x),
      Math.max(8, window.innerWidth - panelWidth - 8),
    );
    const nextY = Math.min(
      Math.max(8, clientY - dragOffsetRef.current.y),
      Math.max(8, window.innerHeight - panelHeight - 8),
    );

    setPosition({ x: nextX, y: nextY });
  };

  const handleDragStart = (event: ReactPointerEvent<HTMLElement>) => {
    if (isBusy) return;

    isDraggingRef.current = true;
    dragOffsetRef.current = {
      x: event.clientX - position.x,
      y: event.clientY - position.y,
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const handleDragMove = (event: ReactPointerEvent<HTMLElement>) => {
    if (!isDraggingRef.current) return;

    movePanel(event.clientX, event.clientY);
  };

  const handleDragEnd = (event: ReactPointerEvent<HTMLElement>) => {
    isDraggingRef.current = false;
    event.currentTarget.releasePointerCapture(event.pointerId);
  };

  const handleAccept = async () => {
    if (!accessToken || isBusy) return;

    const call = incomingCall;
    const sessionId = call.session.sessionId;

    if (!shouldSwitchVoiceSession(sessionId)) {
      return;
    }

    setIsAccepting(true);

    try {
      const response = await acceptVoiceSession(accessToken, sessionId);
      await leaveCurrentVoiceSessionForSwitch(response.session.sessionId);
      clearIncomingCall();
      await joinDirectCallSession(response, callerName);
    } catch (error) {
      toast.error('Không thể tham gia cuộc gọi.');
      console.error('Không thể accept DM call:', error);
    } finally {
      setIsAccepting(false);
    }
  };

  const handleDecline = async () => {
    if (!accessToken || isBusy) return;

    const sessionId = incomingCall.session.sessionId;
    setIsDeclining(true);

    try {
      await declineVoiceSession(accessToken, sessionId);
      clearIncomingCall();
    } catch (error) {
      toast.error('Không thể từ chối cuộc gọi.');
      console.error('Không thể decline DM call:', error);
    } finally {
      setIsDeclining(false);
    }
  };

  return (
    <section className={styles.incomingCall} style={panelStyle} aria-live="polite" aria-label="Cuộc gọi đến">
      <div
        className={styles.dragHandle}
        onPointerDown={handleDragStart}
        onPointerMove={handleDragMove}
        onPointerUp={handleDragEnd}
        onPointerCancel={handleDragEnd}
      >
        <span className={styles.dragDots} aria-hidden="true" />
        <span>Kéo để di chuyển</span>
      </div>
      <div className={styles.avatar}>{avatarText}</div>
      <div className={styles.content}>
        <p className={styles.eyebrow}>Cuộc gọi đến</p>
        <p className={styles.callerName}>{callerName}</p>
      </div>

      <div className={styles.actions}>
        <button
          type="button"
          className={`${styles.actionButton} ${styles.declineButton}`}
          onClick={handleDecline}
          disabled={isBusy}
        >
          {isDeclining ? 'Đang từ chối...' : 'Từ chối'}
        </button>
        <button
          type="button"
          className={`${styles.actionButton} ${styles.acceptButton}`}
          onClick={handleAccept}
          disabled={isBusy}
        >
          {isAccepting ? 'Đang vào...' : 'Nghe máy'}
        </button>
      </div>
    </section>
  );
};

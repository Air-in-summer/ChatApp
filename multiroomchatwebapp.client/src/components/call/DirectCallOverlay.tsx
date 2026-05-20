import { useEffect, useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { RoomEvent, Track } from 'livekit-client';
import type {
  Participant,
  Room as LiveKitRoom,
  VideoTrack,
} from 'livekit-client';
import type { CSSProperties, PointerEvent as ReactPointerEvent } from 'react';
import { useVoiceConnection } from '../../hooks/useVoiceConnection';
import { useChatStore } from '../../store/useChatStore';
import { useVoiceStore } from '../../store/useVoiceStore';
import styles from './DirectCallOverlay.module.css';

type PendingAction = 'mic' | 'camera' | 'screen' | null;

interface MiniVideoPreview {
  track: VideoTrack;
  label: string;
  muted: boolean;
  variant: 'camera' | 'screen';
}

const getParticipantName = (participant: Participant) =>
  participant.name || participant.identity || 'Người dùng';

const getVideoTrack = (participant: Participant, source: Track.Source) => {
  const publication = participant.getTrackPublication(source);
  if (!publication || publication.isMuted) return null;

  return publication.videoTrack ?? null;
};

const getMiniVideoPreview = (room: LiveKitRoom): MiniVideoPreview | null => {
  const localParticipant = room.localParticipant;
  const remoteParticipants = Array.from(room.remoteParticipants.values());

  for (const participant of remoteParticipants) {
    const track = getVideoTrack(participant, Track.Source.ScreenShare);
    if (track) {
      return {
        track,
        label: `${getParticipantName(participant)} đang chia sẻ màn hình`,
        muted: false,
        variant: 'screen',
      };
    }
  }

  const localScreenTrack = getVideoTrack(localParticipant, Track.Source.ScreenShare);
  if (localScreenTrack) {
    return {
      track: localScreenTrack,
      label: 'Bạn đang chia sẻ màn hình',
      muted: true,
      variant: 'screen',
    };
  }

  for (const participant of remoteParticipants) {
    const track = getVideoTrack(participant, Track.Source.Camera);
    if (track) {
      return {
        track,
        label: `${getParticipantName(participant)} - camera`,
        muted: false,
        variant: 'camera',
      };
    }
  }

  const localCameraTrack = getVideoTrack(localParticipant, Track.Source.Camera);
  if (localCameraTrack) {
    return {
      track: localCameraTrack,
      label: 'Camera của bạn',
      muted: true,
      variant: 'camera',
    };
  }

  return null;
};

const MiniVideo = ({ preview }: { preview: MiniVideoPreview }) => {
  const videoRef = useRef<HTMLVideoElement | null>(null);

  useEffect(() => {
    const videoElement = videoRef.current;
    if (!videoElement) return;

    videoElement.muted = preview.muted;
    videoElement.playsInline = true;
    preview.track.attach(videoElement);

    return () => {
      preview.track.detach(videoElement);
    };
  }, [preview]);

  return (
    <>
      <video
        ref={videoRef}
        className={`${styles.previewVideo} ${preview.variant === 'screen' ? styles.screenVideo : styles.cameraVideo}`}
        autoPlay
        muted={preview.muted}
        playsInline
      />
      <span className={styles.previewLabel}>{preview.label}</span>
    </>
  );
};

export const DirectCallOverlay = () => {
  const panelRef = useRef<HTMLElement | null>(null);
  const dragStateRef = useRef<{ offsetX: number; offsetY: number } | null>(null);
  const [position, setPosition] = useState<{ x: number; y: number } | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [, forceRender] = useState(0);

  const activeSession = useVoiceStore((s) => s.activeSession);
  const connectionStatus = useVoiceStore((s) => s.connectionStatus);
  const liveKitRoom = useVoiceStore((s) => s.liveKitRoom);
  const isMicEnabled = useVoiceStore((s) => s.isMicEnabled);
  const isDeafened = useVoiceStore((s) => s.isDeafened);
  const isCameraEnabled = useVoiceStore((s) => s.isCameraEnabled);
  const isScreenSharing = useVoiceStore((s) => s.isScreenSharing);
  const setMicEnabled = useVoiceStore((s) => s.setMicEnabled);
  const setCameraEnabled = useVoiceStore((s) => s.setCameraEnabled);
  const setScreenSharing = useVoiceStore((s) => s.setScreenSharing);
  const activeRoomId = useChatStore((s) => s.activeRoomId);
  const { leaveActiveVoiceSession } = useVoiceConnection();
  const [pendingAction, setPendingAction] = useState<PendingAction>(null);

  useEffect(() => {
    if (!liveKitRoom) return;

    const refresh = () => forceRender((value) => value + 1);

    liveKitRoom
      .on(RoomEvent.ParticipantConnected, refresh)
      .on(RoomEvent.ParticipantDisconnected, refresh)
      .on(RoomEvent.TrackSubscribed, refresh)
      .on(RoomEvent.TrackUnsubscribed, refresh)
      .on(RoomEvent.TrackPublished, refresh)
      .on(RoomEvent.TrackUnpublished, refresh)
      .on(RoomEvent.LocalTrackPublished, refresh)
      .on(RoomEvent.LocalTrackUnpublished, refresh)
      .on(RoomEvent.TrackMuted, refresh)
      .on(RoomEvent.TrackUnmuted, refresh)
      .on(RoomEvent.ActiveSpeakersChanged, refresh);

    return () => {
      liveKitRoom
        .off(RoomEvent.ParticipantConnected, refresh)
        .off(RoomEvent.ParticipantDisconnected, refresh)
        .off(RoomEvent.TrackSubscribed, refresh)
        .off(RoomEvent.TrackUnsubscribed, refresh)
        .off(RoomEvent.TrackPublished, refresh)
        .off(RoomEvent.TrackUnpublished, refresh)
        .off(RoomEvent.LocalTrackPublished, refresh)
        .off(RoomEvent.LocalTrackUnpublished, refresh)
        .off(RoomEvent.TrackMuted, refresh)
        .off(RoomEvent.TrackUnmuted, refresh)
        .off(RoomEvent.ActiveSpeakersChanged, refresh);
    };
  }, [liveKitRoom]);

  useEffect(() => {
    if (!isDragging) return;

    const handlePointerMove = (event: PointerEvent) => {
      const dragState = dragStateRef.current;
      const panel = panelRef.current;
      if (!dragState || !panel) return;

      const rect = panel.getBoundingClientRect();
      const maxX = Math.max(8, window.innerWidth - rect.width - 8);
      const maxY = Math.max(8, window.innerHeight - rect.height - 8);
      const nextX = Math.min(Math.max(8, event.clientX - dragState.offsetX), maxX);
      const nextY = Math.min(Math.max(8, event.clientY - dragState.offsetY), maxY);

      setPosition({ x: nextX, y: nextY });
    };

    const handlePointerUp = () => {
      dragStateRef.current = null;
      setIsDragging(false);
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);

    return () => {
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
    };
  }, [isDragging]);

  if (
    !activeSession ||
    connectionStatus === 'idle' ||
    activeRoomId === activeSession.sourceRoomId
  ) {
    return null;
  }

  const isConnected = connectionStatus === 'connected';
  const preview = liveKitRoom ? getMiniVideoPreview(liveKitRoom) : null;
  const isBusy = pendingAction !== null || !isConnected || !liveKitRoom;
  const statusLabel = activeSession.kind === 'direct-call' ? 'In Call' : 'Voice Connected';
  const popoutStyle: CSSProperties | undefined = position
    ? { left: position.x, top: position.y, right: 'auto', bottom: 'auto' }
    : undefined;

  const handleDragStart = (event: ReactPointerEvent<HTMLElement>) => {
    if (event.button !== 0) return;

    const rect = panelRef.current?.getBoundingClientRect();
    if (!rect) return;

    dragStateRef.current = {
      offsetX: event.clientX - rect.left,
      offsetY: event.clientY - rect.top,
    };
    setPosition({ x: rect.left, y: rect.top });
    setIsDragging(true);
  };

  const handleToggleMic = async () => {
    if (!liveKitRoom || isBusy || isDeafened) return;

    const nextEnabled = !liveKitRoom.localParticipant.isMicrophoneEnabled;
    setPendingAction('mic');

    try {
      await liveKitRoom.localParticipant.setMicrophoneEnabled(nextEnabled);
      setMicEnabled(liveKitRoom.localParticipant.isMicrophoneEnabled);
    } catch (error) {
      setMicEnabled(liveKitRoom.localParticipant.isMicrophoneEnabled);
      toast.error(nextEnabled ? 'Không thể bật mic.' : 'Không thể tắt mic.');
      console.error('Không thể đổi trạng thái mic từ mini popout:', error);
    } finally {
      setPendingAction(null);
    }
  };

  const handleToggleCamera = async () => {
    if (!liveKitRoom || isBusy) return;

    const nextEnabled = !liveKitRoom.localParticipant.isCameraEnabled;
    setPendingAction('camera');

    try {
      await liveKitRoom.localParticipant.setCameraEnabled(nextEnabled);
      setCameraEnabled(liveKitRoom.localParticipant.isCameraEnabled);
    } catch (error) {
      setCameraEnabled(liveKitRoom.localParticipant.isCameraEnabled);
      toast.error(nextEnabled ? 'Không thể bật camera.' : 'Không thể tắt camera.');
      console.error('Không thể đổi trạng thái camera từ mini popout:', error);
    } finally {
      setPendingAction(null);
    }
  };

  const handleToggleScreenShare = async () => {
    if (!liveKitRoom || isBusy) return;

    const nextEnabled = !liveKitRoom.localParticipant.isScreenShareEnabled;
    setPendingAction('screen');

    try {
      await liveKitRoom.localParticipant.setScreenShareEnabled(
        nextEnabled,
        nextEnabled ? { audio: true } : undefined,
      );
      setScreenSharing(liveKitRoom.localParticipant.isScreenShareEnabled);
    } catch (error) {
      setScreenSharing(liveKitRoom.localParticipant.isScreenShareEnabled);
      toast.error(nextEnabled ? 'Không thể chia sẻ màn hình.' : 'Không thể dừng chia sẻ màn hình.');
      console.error('Không thể đổi trạng thái screen share từ mini popout:', error);
    } finally {
      setPendingAction(null);
    }
  };

  return (
    <section
      ref={panelRef}
      className={`${styles.popout} ${isDragging ? styles.dragging : ''}`}
      style={popoutStyle}
      aria-label="Voice mini popout"
    >
      <div className={styles.dragHandle} onPointerDown={handleDragStart}>
        <div className={styles.titleGroup}>
          <span className={styles.statusDot} />
          <div>
            <p className={styles.eyebrow}>{statusLabel}</p>
            <h3>{activeSession.displayName}</h3>
          </div>
        </div>
        <span className={styles.dragHint}>Kéo để di chuyển</span>
      </div>

      <div className={styles.preview}>
        {preview ? (
          <MiniVideo preview={preview} />
        ) : (
          <div className={styles.emptyPreview}>
            <div className={styles.avatar}>{activeSession.displayName.trim().charAt(0).toUpperCase() || '?'}</div>
            <span>{connectionStatus === 'reconnecting' ? 'Đang kết nối lại...' : 'Không có video'}</span>
          </div>
        )}
      </div>

      <div className={styles.actions}>
        <button
          type="button"
          className={`${styles.controlButton} ${isMicEnabled && !isDeafened ? styles.active : styles.muted}`}
          onClick={handleToggleMic}
          disabled={isBusy || isDeafened}
          title={isMicEnabled ? 'Tắt mic' : 'Bật mic'}
        >
          Mic
        </button>
        <button
          type="button"
          className={`${styles.controlButton} ${isCameraEnabled ? styles.active : ''}`}
          onClick={handleToggleCamera}
          disabled={isBusy}
          title={isCameraEnabled ? 'Tắt camera' : 'Bật camera'}
        >
          Cam
        </button>
        <button
          type="button"
          className={`${styles.controlButton} ${isScreenSharing ? styles.active : ''}`}
          onClick={handleToggleScreenShare}
          disabled={isBusy}
          title={isScreenSharing ? 'Dừng chia sẻ' : 'Chia sẻ màn hình'}
        >
          Share
        </button>
        <button
          type="button"
          className={`${styles.controlButton} ${styles.leaveButton}`}
          onClick={() => {
            void leaveActiveVoiceSession();
          }}
          title="Rời voice"
        >
          End
        </button>
      </div>
    </section>
  );
};

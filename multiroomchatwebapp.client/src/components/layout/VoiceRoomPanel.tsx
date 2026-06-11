import { useEffect, useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { RoomEvent, Track } from 'livekit-client';
import type {
  LocalTrackPublication,
  Participant,
  RemoteParticipant,
  RemoteTrack,
  RemoteTrackPublication,
  VideoTrack,
  TrackPublication,
} from 'livekit-client';
import { useAuth } from '../../context/AuthContext';
import { getGroupMembers } from '../../api/groupApi';
import { useVoiceStore } from '../../store/useVoiceStore';
import { useVoiceConnection } from '../../hooks/useVoiceConnection';
import { VoiceSettingsModal } from './VoiceSettingsModal';
import { AddMemberToRoomModal } from '../group/AddMemberToRoomModal';
import type { GroupRole } from '../../types/group';
import styles from './VoiceRoomPanel.module.css';

const isLocalVideoMediaSource = (source: Track.Source) =>
  source === Track.Source.Camera || source === Track.Source.ScreenShare;

const isLocalMicrophoneSource = (source: Track.Source) =>
  source === Track.Source.Microphone;

const GALLERY_PAGE_SIZE = 4;
const FOCUSED_SECONDARY_PAGE_SIZE = 3;

type MediaTileKind = 'avatar' | 'camera' | 'screen';
type MediaTileSize = 'gallery' | 'main' | 'strip';

interface MediaTileModel {
  id: string;
  participant: Participant;
  participantName: string;
  avatarText: string;
  kind: MediaTileKind;
  track?: VideoTrack;
  label: string;
  isLocal: boolean;
  isSpeaking: boolean;
  isMicMuted: boolean;
}

interface MediaTileProps {
  tile: MediaTileModel;
  size: MediaTileSize;
  isSelected: boolean;
  onSelect: () => void;
}

const getParticipantDisplayName = (participant: Participant, isLocal: boolean) =>
  participant.name || participant.identity || (isLocal ? 'Bạn' : 'Unknown');

const getParticipantStableId = (participant: Participant) =>
  participant.sid || participant.identity || participant.name || 'unknown';

const getActiveVideoTrack = (participant: Participant, source: Track.Source): VideoTrack | null => {
  const publication = participant.getTrackPublication(source);
  if (!publication?.videoTrack || publication.isMuted) return null;

  return publication.videoTrack as VideoTrack;
};

/**
 * Render một media tile thống nhất cho camera, screen share và avatar fallback.
 * Tile được dùng cho cả gallery, main focus và strip phụ để tránh lệch UI giữa voice group/DM.
 */
const MediaTile = ({ tile, size, isSelected, onSelect }: MediaTileProps) => {
  const videoRef = useRef<HTMLVideoElement | null>(null);

  useEffect(() => {
    const videoElement = videoRef.current;
    if (!videoElement || !tile.track) return;

    videoElement.muted = tile.isLocal;
    videoElement.playsInline = true;
    tile.track.attach(videoElement);

    return () => {
      tile.track?.detach(videoElement);
    };
  }, [tile.track, tile.isLocal]);

  const classes = [
    styles.mediaTile,
    styles[`${size}MediaTile`],
    tile.kind === 'screen' ? styles.screenMediaTile : '',
    tile.kind === 'avatar' ? styles.avatarMediaTile : '',
    tile.isSpeaking ? styles.speakingMediaTile : '',
    isSelected ? styles.selectedMediaTile : '',
  ].filter(Boolean).join(' ');

  return (
    <button type="button" className={classes} onClick={onSelect}>
      {tile.track ? (
        <video
          ref={videoRef}
          className={`${styles.mediaVideo} ${
            tile.kind === 'camera' && tile.isLocal ? styles.mirroredVideo : ''
          } ${tile.kind === 'screen' ? styles.screenShareVideo : ''}`}
          autoPlay
          muted={tile.isLocal}
          playsInline
        />
      ) : (
        <div className={styles.avatarMediaBody}>
          <div className={styles.avatarMediaCircle}>{tile.avatarText}</div>
        </div>
      )}
      <span className={styles.mediaTileLabel}>{tile.label}</span>
      {tile.isMicMuted && (
        <span className={styles.mediaMuteBadge} aria-label={`${tile.participantName} đang tắt mic`}>
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none"
            stroke="currentColor" strokeWidth="2.5" strokeLinecap="round">
            <line x1="1" y1="1" x2="23" y2="23" />
          </svg>
        </span>
      )}
    </button>
  );
};

interface VoiceRoomPanelProps {
  /** ID phòng Voice đang hiển thị */
  roomId: string;
  /** Tên phòng Voice đang hiển thị */
  roomName: string;
  /** Class CSS từ cha (Layout) để định hình cột */
  className?: string;
  /** Kieu panel: voice channel co dinh hoac DM direct-call */
  mode?: 'channel' | 'direct-call';
  /** Bat buoc khi mode = direct-call de match dung active session */
  sessionId?: string;
  /** ID group chua voice room, dung cho private voice management */
  groupId?: string | null;
  /** Voice room co phai private khong */
  isPrivate?: boolean;
}

/**
 * VoiceRoomPanel - Component chính hiển thị khi user đang xem phòng Voice.
 * Thay thế ChatColumn ở Cột 3 khi phòng được chọn có type = Voice.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Hiển thị header với tên phòng + trạng thái kết nối.
 * 2. Hiển thị avatar grid của các participant đang trong phòng.
 * 3. Hiển thị thanh điều khiển VoiceControls (Mic/Deafen/Cam/Share/Leave) ở đáy.
 *
 * Lưu ý:
 * - Panel này chỉ hiển thị khi user đang xem (focus) phòng Voice trong sidebar.
 * - Voice connection là GLOBAL — user có thể rời panel này mà Voice vẫn chạy.
 * - Khi chưa connect: hiển thị empty state với nút Join (sẽ implement sau).
 * - Participant data lấy từ LiveKit Room instance (room.remoteParticipants).
 */
export const VoiceRoomPanel = ({
  roomId,
  roomName,
  className,
  mode = 'channel',
  sessionId,
  groupId,
  isPrivate = false,
}: VoiceRoomPanelProps) => {
  const { isAuthenticated, user } = useAuth();
  // Đọc state từ Zustand store
  const connectionStatus = useVoiceStore((s) => s.connectionStatus);
  const activeSession = useVoiceStore((s) => s.activeSession);
  const errorMessage = useVoiceStore((s) => s.errorMessage);
  const isMicEnabled = useVoiceStore((s) => s.isMicEnabled);
  const isDeafened = useVoiceStore((s) => s.isDeafened);
  const isCameraEnabled = useVoiceStore((s) => s.isCameraEnabled);
  const isScreenSharing = useVoiceStore((s) => s.isScreenSharing);
  const liveKitRoom = useVoiceStore((s) => s.liveKitRoom);

  // Actions từ store
  const setMicEnabled = useVoiceStore((s) => s.setMicEnabled);
  const setDeafened = useVoiceStore((s) => s.setDeafened);
  const setCameraEnabled = useVoiceStore((s) => s.setCameraEnabled);
  const setScreenSharing = useVoiceStore((s) => s.setScreenSharing);

  // Hook kết nối Voice
  const { joinVoiceRoom, leaveActiveVoiceSession } = useVoiceConnection();
  const [pendingMediaAction, setPendingMediaAction] = useState<'mic' | 'camera' | 'screen' | null>(null);
  const [roomRenderVersion, setRoomRenderVersion] = useState(0);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isAddMemberModalOpen, setIsAddMemberModalOpen] = useState(false);
  const [currentUserRole, setCurrentUserRole] = useState<GroupRole | null>(null);
  const mainMediaStageRef = useRef<HTMLDivElement | null>(null);

  // Kiểm tra user có đang connect vào ĐÚNG phòng này không
  const isCurrentSession = mode === 'channel'
    ? activeSession?.kind === 'channel' && activeSession.sourceRoomId === roomId
    : activeSession?.kind === 'direct-call' && activeSession.sessionId === sessionId;
  const canJoinFromPanel = mode === 'channel';
  const isInThisRoom = isCurrentSession && connectionStatus === 'connected';
  const isConnecting = isCurrentSession && connectionStatus === 'connecting';
  const isReconnecting = isCurrentSession && connectionStatus === 'reconnecting';
  const hasError = isCurrentSession && connectionStatus === 'error';
  const isActive = isInThisRoom || isConnecting || isReconnecting;
  const localParticipant = liveKitRoom?.localParticipant;
  const [focusedTileId, setFocusedTileId] = useState<string | null>(null);
  const [currentTilePage, setCurrentTilePage] = useState(0);
  const participants = liveKitRoom
    ? [liveKitRoom.localParticipant, ...Array.from(liveKitRoom.remoteParticipants.values())]
    : [];
  const mediaTiles = participants.flatMap<MediaTileModel>((participant) => {
    const isLocal = participant === localParticipant;
    const stableId = getParticipantStableId(participant);
    const participantName = getParticipantDisplayName(participant, isLocal);
    const avatarText = participantName.trim().charAt(0).toUpperCase() || '?';
    const screenTrack = getActiveVideoTrack(participant, Track.Source.ScreenShare);
    const cameraTrack = getActiveVideoTrack(participant, Track.Source.Camera);
    const participantTile: MediaTileModel = {
      id: `participant:${stableId}`,
      participant,
      participantName,
      avatarText,
      kind: cameraTrack ? 'camera' : 'avatar',
      track: cameraTrack ?? undefined,
      label: isLocal ? `${participantName} (bạn)` : participantName,
      isLocal,
      isSpeaking: participant.isSpeaking,
      isMicMuted: !participant.isMicrophoneEnabled,
    };

    if (!screenTrack) return [participantTile];

    return [
      {
        id: `screen:${stableId}`,
        participant,
        participantName,
        avatarText,
        kind: 'screen',
        track: screenTrack,
        label: `${participantName} đang chia sẻ màn hình`,
        isLocal,
        isSpeaking: participant.isSpeaking,
        isMicMuted: !participant.isMicrophoneEnabled,
      },
      participantTile,
    ];
  });
  const focusedTile = focusedTileId
    ? mediaTiles.find((tile) => tile.id === focusedTileId) ?? null
    : null;
  const pagedSourceTiles = focusedTile
    ? mediaTiles.filter((tile) => tile.id !== focusedTile.id)
    : mediaTiles;
  const tilePageSize = focusedTile ? FOCUSED_SECONDARY_PAGE_SIZE : GALLERY_PAGE_SIZE;
  const tilePageCount = Math.max(1, Math.ceil(pagedSourceTiles.length / tilePageSize));
  const safeTilePage = Math.min(currentTilePage, tilePageCount - 1);
  const visiblePageTiles = pagedSourceTiles.slice(
    safeTilePage * tilePageSize,
    safeTilePage * tilePageSize + tilePageSize,
  );
  const shouldShowTilePagination = tilePageCount > 1;
  const tileIdsKey = mediaTiles.map((tile) => tile.id).join('|');
  void roomRenderVersion;

  useEffect(() => {
    if (!focusedTileId) return;
    if (mediaTiles.some((tile) => tile.id === focusedTileId)) return;

    setFocusedTileId(null);
  }, [focusedTileId, tileIdsKey, mediaTiles]);

  useEffect(() => {
    if (currentTilePage <= tilePageCount - 1) return;

    setCurrentTilePage(Math.max(0, tilePageCount - 1));
  }, [currentTilePage, tilePageCount]);

  const handleSelectTile = (tileId: string) => {
    if (focusedTileId === tileId) {
      return;
    }

    setFocusedTileId(tileId);
    setCurrentTilePage(0);
  };

  const handleClearTileFocus = () => {
    setFocusedTileId(null);
    setCurrentTilePage(0);
  };

  const handlePreviousTilePage = () => {
    setCurrentTilePage((page) => Math.max(0, page - 1));
  };

  const handleNextTilePage = () => {
    setCurrentTilePage((page) => Math.min(tilePageCount - 1, page + 1));
  };

  const handleToggleMainTileFullscreen = async () => {
    const stageElement = mainMediaStageRef.current;
    if (!stageElement) return;

    try {
      if (document.fullscreenElement) {
        await document.exitFullscreen();
        return;
      }

      await stageElement.requestFullscreen();
    } catch (error) {
      toast.error('Không thể mở fullscreen cho media tile.');
      console.error('Lỗi khi mở fullscreen media tile:', error);
    }
  };

  const handleJoin = () => {
    if (!canJoinFromPanel) return;

    void joinVoiceRoom(roomId, roomName);
  };

  useEffect(() => {
    if (mode !== 'channel' || !isPrivate || !groupId || !isAuthenticated || !user?.userId) {
      setCurrentUserRole(null);
      return;
    }

    let isMounted = true;

    const loadCurrentUserRole = async () => {
      try {
        const members = await getGroupMembers(groupId);
        if (!isMounted) return;

        const me = members.find(member => member.profile.id === user.userId);
        setCurrentUserRole(me?.role ?? null);
      } catch (error) {
        if (!isMounted) return;

        setCurrentUserRole(null);
        console.error('Failed to fetch current user role for private voice room:', error);
      }
    };

    void loadCurrentUserRole();

    return () => {
      isMounted = false;
    };
  }, [groupId, isAuthenticated, isPrivate, mode, user?.userId]);

  useEffect(() => {
    if (!liveKitRoom) return;

    const syncLocalVideoState = () => {
      const { localParticipant } = liveKitRoom;
      setCameraEnabled(localParticipant.isCameraEnabled);
      setScreenSharing(localParticipant.isScreenShareEnabled);
    };

    const syncLocalMicrophoneState = () => {
      const { localParticipant } = liveKitRoom;
      setMicEnabled(localParticipant.isMicrophoneEnabled);
    };

    const refreshRoomRender = () => {
      setRoomRenderVersion((version) => version + 1);
    };

    const syncIfLocalVideoTrack = (publication: TrackPublication) => {
      if (isLocalVideoMediaSource(publication.source)) {
        syncLocalVideoState();
      }
    };

    const syncIfLocalMicrophoneTrack = (publication: TrackPublication) => {
      if (isLocalMicrophoneSource(publication.source)) {
        syncLocalMicrophoneState();
      }
    };

    const handleLocalTrackPublished = (publication: LocalTrackPublication) => {
      syncIfLocalVideoTrack(publication);
      syncIfLocalMicrophoneTrack(publication);
      refreshRoomRender();
    };

    const handleTrackMuted = (publication: TrackPublication, participant: Participant) => {
      if (participant === liveKitRoom.localParticipant) {
        syncIfLocalVideoTrack(publication);
        syncIfLocalMicrophoneTrack(publication);
      }
      refreshRoomRender();
    };

    const handleLocalTrackUnpublished = (publication: LocalTrackPublication) => {
      syncIfLocalVideoTrack(publication);
      syncIfLocalMicrophoneTrack(publication);
      refreshRoomRender();
    };

    const handleParticipantChanged = (_participant: RemoteParticipant) => {
      refreshRoomRender();
    };

    const handleTrackSubscribed = (
      _track: RemoteTrack,
      _publication: RemoteTrackPublication,
      _participant: RemoteParticipant,
    ) => {
      refreshRoomRender();
    };

    const handleTrackUnsubscribed = (
      _track: RemoteTrack,
      _publication: RemoteTrackPublication,
      _participant: RemoteParticipant,
    ) => {
      refreshRoomRender();
    };

    const handleTrackPublicationChanged = (
      _publication: RemoteTrackPublication,
      _participant: RemoteParticipant,
    ) => {
      refreshRoomRender();
    };

    const handleMediaDevicesError = (error: Error, kind?: MediaDeviceKind) => {
      if (kind === 'videoinput') {
        syncLocalVideoState();
        toast.error('Không thể truy cập camera hoặc màn hình chia sẻ.');
      }
      console.error('Lỗi thiết bị media trong Voice:', error);
    };

    syncLocalVideoState();
    syncLocalMicrophoneState();

    liveKitRoom
      .on(RoomEvent.ParticipantConnected, handleParticipantChanged)
      .on(RoomEvent.ParticipantDisconnected, handleParticipantChanged)
      .on(RoomEvent.TrackSubscribed, handleTrackSubscribed)
      .on(RoomEvent.TrackUnsubscribed, handleTrackUnsubscribed)
      .on(RoomEvent.TrackPublished, handleTrackPublicationChanged)
      .on(RoomEvent.TrackUnpublished, handleTrackPublicationChanged)
      .on(RoomEvent.ActiveSpeakersChanged, refreshRoomRender)
      .on(RoomEvent.LocalTrackPublished, handleLocalTrackPublished)
      .on(RoomEvent.LocalTrackUnpublished, handleLocalTrackUnpublished)
      .on(RoomEvent.TrackMuted, handleTrackMuted)
      .on(RoomEvent.TrackUnmuted, handleTrackMuted)
      .on(RoomEvent.MediaDevicesError, handleMediaDevicesError);

    return () => {
      liveKitRoom
        .off(RoomEvent.ParticipantConnected, handleParticipantChanged)
        .off(RoomEvent.ParticipantDisconnected, handleParticipantChanged)
        .off(RoomEvent.TrackSubscribed, handleTrackSubscribed)
        .off(RoomEvent.TrackUnsubscribed, handleTrackUnsubscribed)
        .off(RoomEvent.TrackPublished, handleTrackPublicationChanged)
        .off(RoomEvent.TrackUnpublished, handleTrackPublicationChanged)
        .off(RoomEvent.ActiveSpeakersChanged, refreshRoomRender)
        .off(RoomEvent.LocalTrackPublished, handleLocalTrackPublished)
        .off(RoomEvent.LocalTrackUnpublished, handleLocalTrackUnpublished)
        .off(RoomEvent.TrackMuted, handleTrackMuted)
        .off(RoomEvent.TrackUnmuted, handleTrackMuted)
        .off(RoomEvent.MediaDevicesError, handleMediaDevicesError);
    };
  }, [liveKitRoom, setCameraEnabled, setMicEnabled, setScreenSharing]);

  const handleToggleMic = async () => {
    if (!liveKitRoom || !isInThisRoom || isDeafened || pendingMediaAction) return;

    const nextEnabled = !liveKitRoom.localParticipant.isMicrophoneEnabled;
    setPendingMediaAction('mic');

    try {
      await liveKitRoom.localParticipant.setMicrophoneEnabled(nextEnabled);
      setMicEnabled(liveKitRoom.localParticipant.isMicrophoneEnabled);
    } catch (error) {
      setMicEnabled(liveKitRoom.localParticipant.isMicrophoneEnabled);
      toast.error(nextEnabled ? 'Không thể bật mic.' : 'Không thể tắt mic.');
      console.error('Lỗi khi bật/tắt mic:', error);
    } finally {
      setPendingMediaAction(null);
    }
  };

  const handleToggleDeafen = async () => {
    if (!liveKitRoom || !isInThisRoom || pendingMediaAction) return;

    const nextDeafened = !isDeafened;
    setPendingMediaAction('mic');

    try {
      if (nextDeafened) {
        await liveKitRoom.localParticipant.setMicrophoneEnabled(false);
        setMicEnabled(false);
      }
      setDeafened(nextDeafened);
    } catch (error) {
      setMicEnabled(liveKitRoom.localParticipant.isMicrophoneEnabled);
      toast.error('Không thể đổi trạng thái tắt tiếng.');
      console.error('Lỗi khi bật/tắt deafen:', error);
    } finally {
      setPendingMediaAction(null);
    }
  };

  const handleToggleCamera = async () => {
    if (!liveKitRoom || !isInThisRoom || pendingMediaAction) return;

    const nextEnabled = !liveKitRoom.localParticipant.isCameraEnabled;
    setPendingMediaAction('camera');

    try {
      await liveKitRoom.localParticipant.setCameraEnabled(nextEnabled);
      setCameraEnabled(liveKitRoom.localParticipant.isCameraEnabled);
    } catch (error) {
      setCameraEnabled(liveKitRoom.localParticipant.isCameraEnabled);
      toast.error(nextEnabled ? 'Không thể bật camera.' : 'Không thể tắt camera.');
      console.error('Lỗi khi bật/tắt camera:', error);
    } finally {
      setPendingMediaAction(null);
    }
  };

  const handleToggleScreenShare = async () => {
    if (!liveKitRoom || !isInThisRoom || pendingMediaAction) return;

    const nextEnabled = !liveKitRoom.localParticipant.isScreenShareEnabled;
    setPendingMediaAction('screen');

    try {
      await liveKitRoom.localParticipant.setScreenShareEnabled(
        nextEnabled,
        nextEnabled ? { audio: true } : undefined,
      );
      setScreenSharing(liveKitRoom.localParticipant.isScreenShareEnabled);
    } catch (error) {
      setScreenSharing(liveKitRoom.localParticipant.isScreenShareEnabled);
      toast.error(nextEnabled ? 'Không thể chia sẻ màn hình.' : 'Không thể dừng chia sẻ màn hình.');
      console.error('Lỗi khi bật/tắt screen share:', error);
    } finally {
      setPendingMediaAction(null);
    }
  };

  /**
   * Lấy label trạng thái kết nối cho badge.
   */
  const getStatusLabel = (): string => {
    if (isInThisRoom) return 'Đã kết nối';
    if (isConnecting) return 'Đang kết nối...';
    if (isReconnecting) return 'Đang kết nối lại...';
    if (hasError) return 'Lỗi kết nối';
    return '';
  };

  /**
   * Lấy CSS class cho trạng thái kết nối.
   */
  const getStatusClass = (): string => {
    if (isInThisRoom) return styles.connected;
    if (isConnecting) return styles.connecting;
    if (isReconnecting) return styles.reconnecting;
    if (hasError) return styles.error;
    return '';
  };

  const canManagePrivateVoiceRoom =
    mode === 'channel' &&
    isPrivate &&
    Boolean(groupId) &&
    (currentUserRole === 'Owner' || currentUserRole === 'Admin');

  return (
    <div className={`${styles.voicePanel} ${mode === 'direct-call' ? styles.directCallPanel : ''} ${className || ''}`}>
      {/* === Header === */}
      <div className={`${styles.header} ${mode === 'direct-call' ? styles.directCallHeader : ''}`}>
        <div className={styles.headerIcon}>
          {/* Icon loa/volume cho Voice Channel */}
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2"
            strokeLinecap="round" strokeLinejoin="round">
            <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
            <path d="M15.54 8.46a5 5 0 0 1 0 7.07" />
            <path d="M19.07 4.93a10 10 0 0 1 0 14.14" />
          </svg>
        </div>
        <div className={styles.headerInfo}>
          <h2>
            {roomName}
            {isPrivate && <span className={styles.privateBadge}>PRIVATE</span>}
          </h2>
          {/* Badge trạng thái kết nối */}
          {isActive && (
            <span className={`${styles.connectionBadge} ${getStatusClass()}`}>
              <span className={`${styles.statusDot} ${getStatusClass()}`} />
              {getStatusLabel()}
            </span>
          )}
        </div>
        {canManagePrivateVoiceRoom && (
          <button
            type="button"
            className={styles.addMemberButton}
            onClick={() => setIsAddMemberModalOpen(true)}
            title="Thêm thành viên vào phòng"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
              <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
              <circle cx="8.5" cy="7" r="4" />
              <line x1="20" y1="8" x2="20" y2="14" />
              <line x1="23" y1="11" x2="17" y2="11" />
            </svg>
          </button>
        )}
      </div>

      {/* === Banner lỗi === */}
      {hasError && errorMessage && (
        <div className={styles.errorBanner}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="12" cy="12" r="10" />
            <line x1="15" y1="9" x2="9" y2="15" />
            <line x1="9" y1="9" x2="15" y2="15" />
          </svg>
          <span>{errorMessage}</span>
        </div>
      )}

      {/* === Khu vực Participants === */}
      <div className={styles.participantsArea}>
        {!isActive ? (
          // Chưa kết nối: hiển thị empty state
          <div className={styles.emptyVoice}>
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none"
              stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
              <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
              <path d="M15.54 8.46a5 5 0 0 1 0 7.07" />
              <path d="M19.07 4.93a10 10 0 0 1 0 14.14" />
            </svg>
            <h3>{mode === 'direct-call' ? 'Cuộc gọi' : 'Kênh Voice'}: {roomName}</h3>
            <p>
              {mode === 'direct-call'
                ? 'Cuộc gọi chưa kết nối hoặc đã bị ngắt.'
                : 'Nhấn nút tham gia để kết nối với mọi người trong kênh thoại này.'}
            </p>
            {canJoinFromPanel && (
              <button
                type="button"
                className={styles.joinButton}
                onClick={handleJoin}
              >
                {hasError ? 'Thử lại' : 'Tham gia Voice'}
              </button>
            )}
          </div>
        ) : (
          // Đã kết nối: media canvas dùng chung cho avatar, camera và screen share.
          <div className={styles.connectedVoiceContent}>
            <div className={`${styles.mediaCanvas} ${mode === 'direct-call' ? styles.directCallMediaCanvas : ''}`}>
              {focusedTile ? (
                <div className={styles.focusedMediaLayout}>
                  <div ref={mainMediaStageRef} className={styles.mainMediaStage}>
                    <MediaTile
                      tile={focusedTile}
                      size="main"
                      isSelected
                      onSelect={() => handleSelectTile(focusedTile.id)}
                    />
                    <button
                      type="button"
                      className={styles.clearFocusButton}
                      onClick={handleClearTileFocus}
                    >
                      Thoát focus
                    </button>
                    <button
                      type="button"
                      className={styles.fullscreenButton}
                      onClick={handleToggleMainTileFullscreen}
                      aria-label="Mở fullscreen media tile"
                    >
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none"
                        stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <polyline points="15 3 21 3 21 9" />
                        <polyline points="9 21 3 21 3 15" />
                        <line x1="21" y1="3" x2="14" y2="10" />
                        <line x1="3" y1="21" x2="10" y2="14" />
                      </svg>
                    </button>
                  </div>

                  {visiblePageTiles.length > 0 && (
                    <div className={styles.secondaryTileStrip}>
                      {visiblePageTiles.map((tile) => (
                        <MediaTile
                          key={tile.id}
                          tile={tile}
                          size="strip"
                          isSelected={tile.id === focusedTile.id}
                          onSelect={() => handleSelectTile(tile.id)}
                        />
                      ))}
                    </div>
                  )}
                </div>
              ) : (
                <div className={`${styles.galleryGrid} ${mode === 'direct-call' ? styles.directCallGalleryGrid : ''}`}>
                  {visiblePageTiles.map((tile) => (
                    <MediaTile
                      key={tile.id}
                      tile={tile}
                      size="gallery"
                      isSelected={false}
                      onSelect={() => handleSelectTile(tile.id)}
                    />
                  ))}
                </div>
              )}

              {shouldShowTilePagination && (
                <div className={styles.tilePagination} aria-label="Chuyển trang media tiles">
                  <button
                    type="button"
                    className={styles.tilePageButton}
                    onClick={handlePreviousTilePage}
                    disabled={safeTilePage === 0}
                  >
                    ‹
                  </button>
                  <span className={styles.tilePageText}>{safeTilePage + 1} / {tilePageCount}</span>
                  <button
                    type="button"
                    className={styles.tilePageButton}
                    onClick={handleNextTilePage}
                    disabled={safeTilePage >= tilePageCount - 1}
                  >
                    ›
                  </button>
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* === Thanh điều khiển Voice (Controls Bar) === */}
      {isActive && (
        <div className={`${styles.controlsBar} ${mode === 'direct-call' ? styles.directCallControlsBar : ''}`}>
          {/* Nút Mic */}
          <button
            className={`${styles.controlBtn} ${isMicEnabled ? styles.active : styles.muted}`}
            onClick={handleToggleMic}
            disabled={!isInThisRoom || isDeafened || pendingMediaAction !== null}
            data-tooltip={pendingMediaAction === 'mic' ? 'Đang xử lý mic...' : isDeafened ? 'Bỏ tắt tiếng trước' : isMicEnabled ? 'Tắt mic' : 'Bật mic'}
            id="voice-btn-mic"
          >
            {isMicEnabled ? (
              // Icon mic bật
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
                stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z" />
                <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                <line x1="12" y1="19" x2="12" y2="23" />
                <line x1="8" y1="23" x2="16" y2="23" />
              </svg>
            ) : (
              // Icon mic tắt (gạch chéo)
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
                stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="1" y1="1" x2="23" y2="23" />
                <path d="M9 9v3a3 3 0 0 0 5.12 2.12M15 9.34V4a3 3 0 0 0-5.94-.6" />
                <path d="M17 16.95A7 7 0 0 1 5 12v-2m14 0v2c0 .76-.13 1.49-.36 2.18" />
                <line x1="12" y1="19" x2="12" y2="23" />
                <line x1="8" y1="23" x2="16" y2="23" />
              </svg>
            )}
          </button>

          {/* Nút Deafen */}
          <button
            className={`${styles.controlBtn} ${isDeafened ? styles.muted : styles.normal}`}
            onClick={handleToggleDeafen}
            disabled={!isInThisRoom || pendingMediaAction !== null}
            data-tooltip={pendingMediaAction === 'mic' ? 'Đang xử lý mic...' : isDeafened ? 'Bỏ tắt tiếng' : 'Tắt tiếng'}
            id="voice-btn-deafen"
          >
            {isDeafened ? (
              // Icon tai tắt
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
                stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="1" y1="1" x2="23" y2="23" />
                <path d="M16.5 12.5a4 4 0 0 0-6-3.5" />
                <path d="M20 16V10a8 8 0 0 0-14.5-4.7" />
                <path d="M4 14v-4" />
                <path d="M4 14a1 1 0 0 0 1 1h2l4.5 4.5A1 1 0 0 0 13 19V5" />
              </svg>
            ) : (
              // Icon tai bật (headphone)
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
                stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M3 18v-6a9 9 0 0 1 18 0v6" />
                <path d="M21 19a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3zM3 19a2 2 0 0 0 2 2h1a2 2 0 0 0 2-2v-3a2 2 0 0 0-2-2H3z" />
              </svg>
            )}
          </button>

          {/* Nút Camera */}
          <button
            className={`${styles.controlBtn} ${isCameraEnabled ? styles.active : styles.normal}`}
            onClick={handleToggleCamera}
            disabled={!isInThisRoom || pendingMediaAction !== null}
            data-tooltip={pendingMediaAction === 'camera' ? 'Đang xử lý camera...' : isCameraEnabled ? 'Tắt camera' : 'Bật camera'}
            id="voice-btn-camera"
          >
            {isCameraEnabled ? (
              // Icon camera bật
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
                stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <polygon points="23 7 16 12 23 17 23 7" />
                <rect x="1" y="5" width="15" height="14" rx="2" ry="2" />
              </svg>
            ) : (
              // Icon camera tắt
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
                stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="1" y1="1" x2="23" y2="23" />
                <path d="M21 21H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h3m4.5-2h6.5a2 2 0 0 1 2 2v6.5" />
                <polygon points="23 7 16 12 23 17 23 7" />
              </svg>
            )}
          </button>

          {/* Nút Screen Share */}
          <button
            className={`${styles.controlBtn} ${isScreenSharing ? styles.active : styles.normal}`}
            onClick={handleToggleScreenShare}
            disabled={!isInThisRoom || pendingMediaAction !== null}
            data-tooltip={pendingMediaAction === 'screen' ? 'Đang xử lý chia sẻ...' : isScreenSharing ? 'Dừng chia sẻ' : 'Chia sẻ màn hình'}
            id="voice-btn-screen"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
              stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <rect x="2" y="3" width="20" height="14" rx="2" ry="2" />
              <line x1="8" y1="21" x2="16" y2="21" />
              <line x1="12" y1="17" x2="12" y2="21" />
            </svg>
          </button>

          {/* Nút Settings */}
          <button
            className={`${styles.controlBtn} ${styles.normal}`}
            onClick={() => setIsSettingsOpen(true)}
            data-tooltip="Cài đặt Voice"
            id="voice-btn-settings"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
              stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="3" />
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09a1.65 1.65 0 0 0-1-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9c.14.31.36.58.64.78.28.2.62.32.96.32H21a2 2 0 0 1 0 4h-.09c-.34 0-.68.12-.96.32-.28.2-.5.47-.64.78z" />
            </svg>
          </button>

          {/* Nút Leave (Rời phòng) */}
          <button
            className={`${styles.controlBtn} ${styles.leave}`}
            onClick={() => {
              void leaveActiveVoiceSession();
            }}
            data-tooltip="Rời phòng"
            id="voice-btn-leave"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
              stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M16 17l5-5-5-5" />
              <path d="M21 12H9" />
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
            </svg>
          </button>
        </div>
      )}

      {isSettingsOpen && (
        <VoiceSettingsModal onClose={() => setIsSettingsOpen(false)} />
      )}

      {isAddMemberModalOpen && groupId && (
        <AddMemberToRoomModal
          groupId={groupId}
          roomId={roomId}
          roomName={roomName}
          onClose={() => setIsAddMemberModalOpen(false)}
          onSuccess={(count) => {
            setIsAddMemberModalOpen(false);
            toast.success(`Đã thêm ${count} thành viên vào phòng.`);
          }}
        />
      )}
    </div>
  );
};

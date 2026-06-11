import { Room, RoomEvent, DisconnectReason, VideoPresets } from 'livekit-client';
import type { RoomOptions } from 'livekit-client';
import { getVoiceSessionToken, getVoiceToken } from '../api/voiceApi';
import { useVoiceStore } from '../store/useVoiceStore';
import type { ActiveVoiceSession } from '../store/useVoiceStore';

const activeVoiceRoomRef: { current: Room | null } = { current: null };
const tokenRefreshTimerRef: { current: number | null } = { current: null };

const toExactDeviceConstraint = (deviceId: string | null): ConstrainDOMString | undefined =>
  deviceId ? { exact: deviceId } : undefined;

export interface JoinVoiceSessionParams {
  session: ActiveVoiceSession;
  token: string;
  liveKitHost: string;
  expiresAtUtc: string;
  expiresInSeconds: number;
}

const isSameVoiceSession = (current: ActiveVoiceSession | null, next: ActiveVoiceSession): boolean => {
  if (!current || current.kind !== next.kind) return false;

  if (current.kind === 'channel' && next.kind === 'channel') {
    return current.sourceRoomId === next.sourceRoomId;
  }

  if (current.kind === 'direct-call' && next.kind === 'direct-call') {
    return current.sessionId === next.sessionId;
  }

  return false;
};

const createRoomOptions = (): RoomOptions => {
  const { deviceSettings } = useVoiceStore.getState();
  const selectedMicDeviceId = toExactDeviceConstraint(deviceSettings.selectedMicId);
  const selectedCamDeviceId = toExactDeviceConstraint(deviceSettings.selectedCamId);
  const selectedSpeakerDeviceId = deviceSettings.selectedSpeakerId;

  return {
    audioCaptureDefaults: {
      ...(selectedMicDeviceId ? { deviceId: selectedMicDeviceId } : {}),
      autoGainControl: true,
      echoCancellation: true,
      noiseSuppression: true,
    },
    videoCaptureDefaults: {
      ...(selectedCamDeviceId ? { deviceId: selectedCamDeviceId } : {}),
      resolution: VideoPresets.h720.resolution,
    },
    ...(selectedSpeakerDeviceId ? { audioOutput: { deviceId: selectedSpeakerDeviceId } } : {}),
    publishDefaults: {
      videoCodec: 'vp8',
      simulcast: true,
    },
    dynacast: true,
    adaptiveStream: true,
    disconnectOnPageLeave: true,
  };
};

const clearTokenRefreshTimer = () => {
  if (tokenRefreshTimerRef.current !== null) {
    window.clearTimeout(tokenRefreshTimerRef.current);
    tokenRefreshTimerRef.current = null;
  }
};

const getRefreshDelayMs = (expiresAtUtc: string, expiresInSeconds: number): number => {
  const expiresAt = new Date(expiresAtUtc).getTime();
  const now = Date.now();
  const refreshBeforeMs = Math.max(60_000, Math.min(5 * 60_000, expiresInSeconds * 1000 * 0.2));
  const delayMs = expiresAt - now - refreshBeforeMs;

  return Math.max(5_000, delayMs);
};

const scheduleTokenRefresh = (expiresAtUtc: string, expiresInSeconds: number) => {
  clearTokenRefreshTimer();

  const delayMs = getRefreshDelayMs(expiresAtUtc, expiresInSeconds);

  tokenRefreshTimerRef.current = window.setTimeout(() => {
    void refreshLiveKitToken().catch((error) => {
      console.error('Không thể refresh LiveKit token:', error);

      cleanupRoom();
      useVoiceStore.getState().setError(
        error instanceof Error ? error.message : 'LiveKit token refresh thất bại'
      );
    });
  }, delayMs);
};

const refreshLiveKitToken = async () => {
  const state = useVoiceStore.getState();
  const session = state.activeSession;

  if (!session || state.connectionStatus === 'idle' || state.connectionStatus === 'error') {
    return;
  }

  const response =
    session.kind === 'channel'
      ? await getVoiceToken(session.sourceRoomId)
      : await getVoiceSessionToken(session.sessionId);

  useVoiceStore.getState().setTokenMetadata({
    expiresAtUtc: response.expiresAtUtc,
    expiresInSeconds: response.expiresInSeconds,
  });

  scheduleTokenRefresh(response.expiresAtUtc, response.expiresInSeconds);
};

/**
 * Dọn dẹp LiveKit Room hiện tại.
 *
 * Input: không có.
 * Output: ngắt kết nối Room nếu đang tồn tại.
 * Error cases: LiveKit tự xử lý lỗi disconnect nội bộ, caller không cần catch.
 */
const cleanupRoom = () => {
  clearTokenRefreshTimer();

  const room = activeVoiceRoomRef.current;
  if (!room) return;

  room.removeAllListeners();
  room.disconnect();
  activeVoiceRoomRef.current = null;
};

/**
 * Gắn các event lifecycle của LiveKit Room vào voice store.
 *
 * Input: LiveKit Room đã tạo nhưng có thể chưa connect.
 * Output: cập nhật trạng thái reconnect/disconnect vào Zustand store.
 * Error cases: event handler không throw ra UI, lỗi kết nối được LiveKit emit qua Disconnected.
 */
const setupRoomEventListeners = (room: Room) => {
  room.on(RoomEvent.Disconnected, (reason?: DisconnectReason) => {
    console.log(`Voice disconnected, reason: ${reason ?? 'unknown'}`);

    if (activeVoiceRoomRef.current === room) {
      activeVoiceRoomRef.current = null;
    }

    room.removeAllListeners();
    useVoiceStore.getState().disconnect();
  });

  room.on(RoomEvent.Reconnecting, () => {
    useVoiceStore.getState().setReconnecting();
  });

  room.on(RoomEvent.Reconnected, () => {
    useVoiceStore.getState().setConnected(room);
  });
};

/**
 * Join một voice session bất kỳ qua LiveKit.
 *
 * Input:
 * - session: metadata session frontend, có thể là voice channel hoặc DM call.
 * - token/liveKitHost: credential backend đã cấp cho LiveKit.
 * - expiresAtUtc/expiresInSeconds: metadata hết hạn token để Phase 4D dùng refresh timer.
 *
 * Output: kết nối LiveKit thành công và cập nhật `useVoiceStore.liveKitRoom`.
 * Error cases: connect lỗi sẽ cleanup Room tạm và set `connectionStatus = error`.
 */
export const joinVoiceSession = async ({
  session,
  token,
  liveKitHost,
  expiresAtUtc,
  expiresInSeconds,
}: JoinVoiceSessionParams) => {
  const currentState = useVoiceStore.getState();

  if (
    isSameVoiceSession(currentState.activeSession, session) &&
    (currentState.connectionStatus === 'connected' || currentState.connectionStatus === 'connecting')
  ) {
    return;
  }

  if (currentState.activeSession && !isSameVoiceSession(currentState.activeSession, session)) {
    cleanupRoom();
  }

  currentState.startConnectingSession({ session });

  try {
    const room = new Room(createRoomOptions());
    activeVoiceRoomRef.current = room;
    setupRoomEventListeners(room);

    await room.connect(liveKitHost, token);
    useVoiceStore.getState().setConnected(room);
    useVoiceStore.getState().setTokenMetadata({ expiresAtUtc, expiresInSeconds });
    scheduleTokenRefresh(expiresAtUtc, expiresInSeconds);

    try {
      const { isMicEnabled, isDeafened } = useVoiceStore.getState();
      if (isMicEnabled && !isDeafened) {
        await room.localParticipant.setMicrophoneEnabled(true);
      }
      useVoiceStore.getState().setMicEnabled(room.localParticipant.isMicrophoneEnabled);
    } catch (error) {
      useVoiceStore.getState().setMicEnabled(false);
      console.error('Không thể bật microphone khi join voice:', error);
    }
  } catch (error) {
    cleanupRoom();
    const errorMessage = error instanceof Error ? error.message : 'Không thể kết nối phòng Voice';
    useVoiceStore.getState().setError(errorMessage);
  }
};

/**
 * Join một Voice Room qua LiveKit.
 *
 * Input:
 * - roomId: ID phòng Voice trong PostgreSQL.
 * - roomName: tên phòng hiển thị trên UI.
 *
 * Output: lấy token voice room rồi forward vào `joinVoiceSession`.
 * Error cases: token/API/connect lỗi sẽ cleanup Room tạm và set `connectionStatus = error`.
 */
export const joinVoiceRoom = async (roomId: string, roomName: string) => {
  const session: ActiveVoiceSession = {
    kind: 'channel',
    sourceRoomId: roomId,
    liveKitRoomName: roomId,
    displayName: roomName,
  };

  const currentState = useVoiceStore.getState();
  if (
    isSameVoiceSession(currentState.activeSession, session) &&
    (currentState.connectionStatus === 'connected' || currentState.connectionStatus === 'connecting')
  ) {
    return;
  }

  try {
    const { token, liveKitHost, expiresAtUtc, expiresInSeconds } = await getVoiceToken(roomId);

    await joinVoiceSession({
      session,
      token,
      liveKitHost,
      expiresAtUtc,
      expiresInSeconds,
    });
  } catch (error) {
    cleanupRoom();
    const errorMessage = error instanceof Error ? error.message : 'Không thể kết nối phòng Voice';
    useVoiceStore.getState().setError(errorMessage);
  }
};

/**
 * Rời Voice Room hiện tại.
 *
 * Input: không có.
 * Output: disconnect LiveKit và reset voice store về idle.
 * Error cases: không throw nếu chưa có Room đang active.
 */
export const leaveVoiceRoom = () => {
  cleanupRoom();
  useVoiceStore.getState().disconnect();
};

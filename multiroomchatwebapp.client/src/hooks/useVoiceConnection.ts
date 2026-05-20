import { useCallback } from 'react';
import { useAuth } from '../context/AuthContext';
import { leaveVoiceSession as leaveVoiceSessionApi } from '../api/voiceApi';
import { useVoiceStore } from '../store/useVoiceStore';
import type { ActiveVoiceSession } from '../store/useVoiceStore';
import type { VoiceSessionTokenResponseDto } from '../api/voiceApi';

const loadVoiceConnectionService = () => import('../services/voiceConnectionService');

const isTargetActiveDirectCall = (activeSession: ActiveVoiceSession | null, targetSessionId?: string) =>
  Boolean(
    activeSession?.kind === 'direct-call' &&
    targetSessionId &&
    activeSession.sessionId === targetSessionId
  );

/**
 * Hook wrapper cho Voice connection khi component đã ở trong voice chunk.
 *
 * Input: lấy `accessToken` từ AuthContext.
 * Output: trả về imperative actions `joinVoiceRoom` và `leaveVoiceRoom`.
 * Error cases: nếu thiếu access token thì không gọi LiveKit service.
 */
export const useVoiceConnection = () => {
  const { accessToken } = useAuth();

  const joinVoiceRoom = useCallback(async (roomId: string, roomName: string) => {
    if (!accessToken) {
      console.error('useVoiceConnection: Không có accessToken');
      return;
    }

    const { joinVoiceRoom: joinVoiceRoomService } = await loadVoiceConnectionService();
    await joinVoiceRoomService(accessToken, roomId, roomName);
  }, [accessToken]);

  const leaveVoiceRoom = useCallback(async () => {
    const { leaveVoiceRoom: leaveVoiceRoomService } = await loadVoiceConnectionService();
    leaveVoiceRoomService();
  }, []);

  const leaveActiveVoiceSession = useCallback(async () => {
    const activeSession = useVoiceStore.getState().activeSession;

    if (activeSession?.kind === 'direct-call' && accessToken) {
      try {
        await leaveVoiceSessionApi(accessToken, activeSession.sessionId);
      } catch (error) {
        console.warn('Không thể báo backend rời DM call trước khi cleanup local:', error);
      }
    }

    const { leaveVoiceRoom: leaveVoiceRoomService } = await loadVoiceConnectionService();
    leaveVoiceRoomService();
  }, [accessToken]);

  const shouldSwitchVoiceSession = useCallback((targetSessionId?: string) => {
    const activeSession = useVoiceStore.getState().activeSession;

    if (!activeSession || isTargetActiveDirectCall(activeSession, targetSessionId)) {
      return true;
    }

    return window.confirm(
      'Bạn đang trong một phiên voice khác. Chuyển sang cuộc gọi này sẽ ngắt phiên hiện tại. Tiếp tục?'
    );
  }, []);

  const leaveCurrentVoiceSessionForSwitch = useCallback(async (targetSessionId?: string) => {
    const activeSession = useVoiceStore.getState().activeSession;

    if (!activeSession || isTargetActiveDirectCall(activeSession, targetSessionId)) {
      return;
    }

    if (activeSession.kind === 'direct-call' && accessToken) {
      try {
        await leaveVoiceSessionApi(accessToken, activeSession.sessionId);
      } catch (error) {
        console.warn('Không thể báo backend rời DM call cũ trước khi chuyển phiên:', error);
      }
    }

    const { leaveVoiceRoom: leaveVoiceRoomService } = await loadVoiceConnectionService();
    leaveVoiceRoomService();
  }, [accessToken]);

  const joinDirectCallSession = useCallback(async (
    response: VoiceSessionTokenResponseDto,
    displayName: string
  ) => {
    if (!accessToken) {
      console.error('useVoiceConnection: Không có accessToken');
      return;
    }

    const { joinVoiceSession: joinVoiceSessionService } = await loadVoiceConnectionService();
    await joinVoiceSessionService({
      accessToken,
      session: {
        kind: 'direct-call',
        sourceRoomId: response.session.sourceRoomId,
        sessionId: response.session.sessionId,
        liveKitRoomName: response.session.liveKitRoomName,
        displayName,
      },
      token: response.token,
      liveKitHost: response.liveKitHost,
      expiresAtUtc: response.expiresAtUtc,
      expiresInSeconds: response.expiresInSeconds,
    });
  }, [accessToken]);

  return {
    joinVoiceRoom,
    joinDirectCallSession,
    leaveVoiceRoom,
    leaveActiveVoiceSession,
    shouldSwitchVoiceSession,
    leaveCurrentVoiceSessionForSwitch,
  };
};

import { useCallback } from 'react';
import { leaveVoiceSession as leaveVoiceSessionApi } from '../api/voiceApi';
import { requestVoiceSwitchConfirmation } from '../services/voiceSwitchConfirmService';
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

const isTargetActiveVoiceChannel = (activeSession: ActiveVoiceSession | null, targetRoomId?: string) =>
  Boolean(
    activeSession?.kind === 'channel' &&
    targetRoomId &&
    activeSession.sourceRoomId === targetRoomId
  );

const isTargetActiveVoiceSession = (
  activeSession: ActiveVoiceSession | null,
  targetSessionId?: string,
  targetRoomId?: string,
) =>
  isTargetActiveDirectCall(activeSession, targetSessionId) ||
  isTargetActiveVoiceChannel(activeSession, targetRoomId);

/**
 * Hook wrapper cho Voice connection khi component đã ở trong voice chunk.
 *
 * Input: dùng BFF session cookie hiện tại của browser.
 * Output: trả về imperative actions `joinVoiceRoom` và `leaveVoiceRoom`.
 * Error cases: nếu session hết hạn thì API voice sẽ trả 401 và auth layer xử lý.
 */
export const useVoiceConnection = () => {
  const joinVoiceRoom = useCallback(async (roomId: string, roomName: string) => {
    const { joinVoiceRoom: joinVoiceRoomService } = await loadVoiceConnectionService();
    await joinVoiceRoomService(roomId, roomName);
  }, []);

  const leaveVoiceRoom = useCallback(async () => {
    const { leaveVoiceRoom: leaveVoiceRoomService } = await loadVoiceConnectionService();
    leaveVoiceRoomService();
  }, []);

  const leaveActiveVoiceSession = useCallback(async () => {
    const activeSession = useVoiceStore.getState().activeSession;

    if (activeSession?.kind === 'direct-call') {
      try {
        await leaveVoiceSessionApi(activeSession.sessionId);
      } catch (error) {
        console.warn('Không thể báo backend rời DM call trước khi cleanup local:', error);
      }
    }

    const { leaveVoiceRoom: leaveVoiceRoomService } = await loadVoiceConnectionService();
    leaveVoiceRoomService();
  }, []);

  const shouldSwitchVoiceSession = useCallback(async (targetSessionId?: string, targetRoomId?: string) => {
    const activeSession = useVoiceStore.getState().activeSession;

    if (!activeSession || isTargetActiveVoiceSession(activeSession, targetSessionId, targetRoomId)) {
      return true;
    }

    return requestVoiceSwitchConfirmation(
      'Bạn đang trong một phiên voice khác. Chuyển sang phiên này sẽ ngắt phiên hiện tại. Tiếp tục?'
    );
  }, []);

  const leaveCurrentVoiceSessionForSwitch = useCallback(async (targetSessionId?: string, targetRoomId?: string) => {
    const activeSession = useVoiceStore.getState().activeSession;

    if (!activeSession || isTargetActiveVoiceSession(activeSession, targetSessionId, targetRoomId)) {
      return;
    }

    if (activeSession.kind === 'direct-call') {
      try {
        await leaveVoiceSessionApi(activeSession.sessionId);
      } catch (error) {
        console.warn('Không thể báo backend rời DM call cũ trước khi chuyển phiên:', error);
      }
    }

    const { leaveVoiceRoom: leaveVoiceRoomService } = await loadVoiceConnectionService();
    leaveVoiceRoomService();
  }, []);

  const joinDirectCallSession = useCallback(async (
    response: VoiceSessionTokenResponseDto,
    displayName: string
  ) => {
    const { joinVoiceSession: joinVoiceSessionService } = await loadVoiceConnectionService();
    await joinVoiceSessionService({
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
  }, []);

  return {
    joinVoiceRoom,
    joinDirectCallSession,
    leaveVoiceRoom,
    leaveActiveVoiceSession,
    shouldSwitchVoiceSession,
    leaveCurrentVoiceSessionForSwitch,
  };
};

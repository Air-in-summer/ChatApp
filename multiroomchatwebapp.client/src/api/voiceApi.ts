import { apiClient } from './apiClient';

export type VoiceSessionKind = 'Channel' | 'DirectCall';

export type VoiceSessionStatus = 'Ringing' | 'Active' | 'Ended' | 'Declined' | 'Missed';

export type VoiceParticipantStatus = 'Invited' | 'Joined' | 'Left' | 'Declined';

export interface VoiceSessionParticipantDto {
  userId: string;
  status: VoiceParticipantStatus;
  invitedAt: string;
  joinedAt: string | null;
  leftAt: string | null;
}

export interface VoiceSessionResponseDto {
  sessionId: string;
  kind: VoiceSessionKind;
  status: VoiceSessionStatus;
  sourceRoomId: string;
  createdByUserId: string;
  liveKitRoomName: string;
  createdAt: string;
  startedAt: string | null;
  endedAt: string | null;
  participants: VoiceSessionParticipantDto[];
}

export interface VoiceSessionTokenResponseDto {
  session: VoiceSessionResponseDto;
  token: string;
  liveKitHost: string;
  expiresAtUtc: string;
  expiresInSeconds: number;
}

export interface VoiceCallIncomingDto {
  session: VoiceSessionResponseDto;
  callerId: string;
  callerDisplayName: string;
}

export interface VoiceCallStatusChangedDto {
  session: VoiceSessionResponseDto;
  actorUserId: string;
}

/**
 * Response trả về từ API Voice Token.
 * Chứa JWT để connect LiveKit + URL của LiveKit Server.
 */
export interface VoiceTokenResponse {
  /** LiveKit Access Token (JWT) - dùng để connect WebSocket đến LiveKit Server */
  token: string;
  /** URL của LiveKit Server, ví dụ: "ws://localhost:7880" (dev) */
  liveKitHost: string;
  /** Thời điểm LiveKit token hết hạn theo UTC */
  expiresAtUtc: string;
  /** Số giây còn hiệu lực của LiveKit token */
  expiresInSeconds: number;
}

/**
 * Gọi API: POST /api/v1/voice/token/{roomId} - Lấy LiveKit Access Token để tham gia phòng Voice
 * 
 * @param roomId - ID của phòng Voice (phải là RoomType.Voice)
 * @returns Promise<VoiceTokenResponse> - JWT token + LiveKit host URL
 * @throws AxiosError
 *   - 401: Session đăng nhập không hợp lệ hoặc hết hạn
 *   - 403: User không phải thành viên của phòng
 *   - 404: Phòng không tồn tại
 * 
 * @example
 * ```ts
 * const { token: liveKitToken, liveKitHost } = await getVoiceToken(roomId);
 * // Dùng liveKitToken + liveKitHost để connect LiveKit SDK
 * ```
 */
export const getVoiceToken = async (roomId: string): Promise<VoiceTokenResponse> => {
  const response = await apiClient.post<VoiceTokenResponse>(`/api/v1/voice/token/${roomId}`);
  return response.data;
};

/**
 * Bắt đầu một DM call từ DirectMessage room.
 */
export const startDirectCall = async (
  dmRoomId: string
): Promise<VoiceSessionTokenResponseDto> => {
  const response = await apiClient.post<VoiceSessionTokenResponseDto>(
    `/api/v1/voice/sessions/direct/${dmRoomId}/start`
  );
  return response.data;
};

/**
 * Accept một DM call đang ringing.
 */
export const acceptVoiceSession = async (
  sessionId: string
): Promise<VoiceSessionTokenResponseDto> => {
  const response = await apiClient.post<VoiceSessionTokenResponseDto>(
    `/api/v1/voice/sessions/${sessionId}/accept`
  );
  return response.data;
};

/**
 * Decline một DM call đang ringing.
 */
export const declineVoiceSession = async (
  sessionId: string
): Promise<VoiceSessionResponseDto> => {
  const response = await apiClient.post<VoiceSessionResponseDto>(
    `/api/v1/voice/sessions/${sessionId}/decline`
  );
  return response.data;
};

/**
 * Lấy lại LiveKit token cho reconnect/refresh của DM call.
 */
export const getVoiceSessionToken = async (
  sessionId: string
): Promise<VoiceSessionTokenResponseDto> => {
  const response = await apiClient.post<VoiceSessionTokenResponseDto>(
    `/api/v1/voice/sessions/${sessionId}/token`
  );
  return response.data;
};

/**
 * Rời khỏi một DM call.
 */
export const leaveVoiceSession = async (
  sessionId: string
): Promise<VoiceSessionResponseDto> => {
  const response = await apiClient.post<VoiceSessionResponseDto>(
    `/api/v1/voice/sessions/${sessionId}/leave`
  );
  return response.data;
};

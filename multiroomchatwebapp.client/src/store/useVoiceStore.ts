import { create } from 'zustand';
import type { Room as LiveKitRoom } from 'livekit-client';
import type { VoiceCallIncomingDto, VoiceCallStatusChangedDto } from '../api/voiceApi';

/**
 * Trạng thái kết nối Voice hiện tại.
 * - idle: Chưa kết nối phòng Voice nào
 * - connecting: Đang xin Token + kết nối đến LiveKit Server
 * - connected: Đã kết nối thành công, đang trong phòng Voice
 * - reconnecting: Mất kết nối tạm thời, đang thử kết nối lại
 * - error: Kết nối thất bại (lỗi mạng, token hết hạn...)
 */
type VoiceConnectionStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'error';

export type VoiceCallUiStatus = 'incoming' | 'accepted' | 'declined' | 'ended';

/**
 * Voice session active trên frontend.
 * Dùng discriminated union để tránh truyền field thừa:
 * - channel không có `sessionId`.
 * - direct-call bắt buộc có `sessionId`.
 */
export type ActiveVoiceSession =
  | {
      kind: 'channel';
      sourceRoomId: string;
      liveKitRoomName: string;
      displayName: string;
    }
  | {
      kind: 'direct-call';
      sourceRoomId: string;
      sessionId: string;
      liveKitRoomName: string;
      displayName: string;
    };

/**
 * Metadata hết hạn của LiveKit token hiện tại.
 */
export interface VoiceTokenMetadata {
  expiresAtUtc: string;
  expiresInSeconds: number;
}

/**
 * Cài đặt thiết bị Voice (Mic, Camera, Speaker) của người dùng.
 * Được lưu vào localStorage để giữ lại giữa các session.
 */
interface DeviceSettings {
  /** ID thiết bị Microphone đã chọn (từ navigator.mediaDevices) */
  selectedMicId: string | null;
  /** ID thiết bị Camera đã chọn */
  selectedCamId: string | null;
  /** ID thiết bị Speaker đã chọn */
  selectedSpeakerId: string | null;
}

/**
 * State của Voice Module - GLOBAL và ĐỘCLẬP hoàn toàn với Chat UI.
 * 
 * @remarks
 * Nguyên tắc thiết kế (theo Discord):
 * - Voice session là global: user có thể đang trong phòng Voice và tự do
 *   chuyển sang bất kỳ Text Room/DM nào để chat. Voice vẫn chạy ngầm.
 * - `useVoiceStore` sống độc lập với `useChatStore`: việc thay đổi 
 *   `activeRoomId` trong chat KHÔNG ảnh hưởng đến `currentVoiceRoomId`.
 * - Khi user join Voice Room → VoiceStatusBar xuất hiện ở góc dưới màn hình.
 * - Khi user leave → trạng thái về idle, VoiceStatusBar ẩn đi.
 */
interface VoiceState {
  // ──────────────────────────────────────────────
  // Trạng thái kết nối
  // ──────────────────────────────────────────────

  /** Trạng thái kết nối Voice hiện tại */
  connectionStatus: VoiceConnectionStatus;

  /** ID phòng Voice đang tham gia (mapping 1:1 với Room.Id trong PostgreSQL) */
  currentVoiceRoomId: string | null;

  /** Tên phòng Voice đang tham gia (hiển thị trên VoiceStatusBar) */
  currentVoiceRoomName: string | null;

  /** Voice session active hiện tại; null khi idle/error */
  activeSession: ActiveVoiceSession | null;

  /** Metadata token active hiện tại; null khi chưa có token */
  tokenMetadata: VoiceTokenMetadata | null;

  /** Cuộc gọi DM đang đổ chuông; null nếu không có incoming call */
  incomingCall: VoiceCallIncomingDto | null;

  /** Event call gần nhất dùng cho UI hiển thị accepted/declined/ended */
  latestCallEvent: VoiceCallStatusChangedDto | null;

  /** Trạng thái call UI gần nhất, tách riêng với trạng thái kết nối LiveKit */
  callUiStatus: VoiceCallUiStatus | null;

  /** 
   * Instance LiveKit Room - object chính để tương tác với SDK.
   * null khi chưa kết nối. Dùng để gọi room.disconnect(), room.switchActiveDevice(), v.v.
   */
  liveKitRoom: LiveKitRoom | null;

  /** Thông báo lỗi khi kết nối thất bại */
  errorMessage: string | null;

  // ──────────────────────────────────────────────
  // Trạng thái media cục bộ (của chính mình)
  // ──────────────────────────────────────────────

  /** Mic có đang bật không (unmuted) */
  isMicEnabled: boolean;

  /** 
   * Deafen = tắt cả nghe lẫn nói (giống Discord).
   * Khi Deafen = true → tự động mute Mic + không nhận audio từ người khác.
   */
  isDeafened: boolean;

  /** Camera có đang bật không */
  isCameraEnabled: boolean;

  /** Đang chia sẻ màn hình không */
  isScreenSharing: boolean;

  // ──────────────────────────────────────────────
  // Cài đặt thiết bị (persist vào localStorage)
  // ──────────────────────────────────────────────

  /** Cài đặt thiết bị đã chọn */
  deviceSettings: DeviceSettings;

  // ──────────────────────────────────────────────
  // Actions
  // ──────────────────────────────────────────────

  /** 
   * Bắt đầu join phòng Voice: set trạng thái connecting + lưu thông tin phòng.
   * Gọi TRƯỚC khi gọi API xin Token.
   */
  startConnecting: (roomId: string, roomName: string) => void;

  /**
   * Bắt đầu join một voice session bất kỳ.
   * Dùng chung cho voice channel và DM call.
   */
  startConnectingSession: (params: {
    session: ActiveVoiceSession;
  }) => void;

  /**
   * Lưu metadata hết hạn của LiveKit token hiện tại.
   */
  setTokenMetadata: (metadata: VoiceTokenMetadata) => void;

  /** Lưu incoming call khi SignalR nhận VoiceCallIncoming */
  setIncomingCall: (payload: VoiceCallIncomingDto) => void;

  /** Xóa incoming call đang hiển thị */
  clearIncomingCall: () => void;

  /** Lưu event accepted khi SignalR nhận VoiceCallAccepted */
  setCallAccepted: (payload: VoiceCallStatusChangedDto) => void;

  /** Lưu event declined khi SignalR nhận VoiceCallDeclined */
  setCallDeclined: (payload: VoiceCallStatusChangedDto) => void;

  /** Lưu event ended khi SignalR nhận VoiceCallEnded */
  setCallEnded: (payload: VoiceCallStatusChangedDto) => void;

  /** Xóa event call gần nhất khỏi UI state */
  clearCallEvent: () => void;

  /**
   * Kết nối thành công: lưu LiveKit Room instance + chuyển trạng thái connected.
   * Gọi SAU khi LiveKit Room đã connect thành công.
   */
  setConnected: (liveKitRoom: LiveKitRoom) => void;

  /**
   * Chuyển sang trạng thái reconnecting (mất kết nối tạm).
   * LiveKit SDK tự xử lý reconnect, UI chỉ cần hiển thị trạng thái.
   */
  setReconnecting: () => void;

  /**
   * Kết nối thất bại hoặc bị ngắt vĩnh viễn.
   * Reset toàn bộ trạng thái Voice về idle.
   */
  setError: (message: string) => void;

  /**
   * Rời phòng Voice: ngắt kết nối + reset toàn bộ trạng thái về idle.
   * Gọi khi user bấm Leave hoặc khi bị disconnect vĩnh viễn.
   * KHÔNG tự động reconnect (theo plan: user phải bấm Join lại thủ công).
   */
  disconnect: () => void;

  // --- Media toggles ---

  /** Bật/tắt Microphone */
  setMicEnabled: (enabled: boolean) => void;

  /** Bật/tắt Deafen (tắt cả nghe lẫn nói) */
  setDeafened: (deafened: boolean) => void;

  /** Bật/tắt Camera */
  setCameraEnabled: (enabled: boolean) => void;

  /** Bật/tắt Screen Share */
  setScreenSharing: (sharing: boolean) => void;

  // --- Device settings ---

  /** Chọn thiết bị Mic (lưu vào localStorage) */
  setSelectedMic: (deviceId: string) => void;

  /** Chọn thiết bị Camera (lưu vào localStorage) */
  setSelectedCam: (deviceId: string) => void;

  /** Chọn thiết bị Speaker (lưu vào localStorage) */
  setSelectedSpeaker: (deviceId: string) => void;
}

// ──────────────────────────────────────────────────────────
// Helper: đọc/ghi device settings từ localStorage
// ──────────────────────────────────────────────────────────

const DEVICE_SETTINGS_KEY = 'voice_device_settings';

/**
 * Đọc cài đặt thiết bị từ localStorage.
 * Trả về default nếu chưa có hoặc dữ liệu bị hỏng.
 */
const loadDeviceSettings = (): DeviceSettings => {
  try {
    const raw = localStorage.getItem(DEVICE_SETTINGS_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as DeviceSettings;
      return {
        selectedMicId: parsed.selectedMicId ?? null,
        selectedCamId: parsed.selectedCamId ?? null,
        selectedSpeakerId: parsed.selectedSpeakerId ?? null,
      };
    }
  } catch {
    // localStorage bị hỏng hoặc parse fail → dùng default
  }
  return { selectedMicId: null, selectedCamId: null, selectedSpeakerId: null };
};

/**
 * Ghi cài đặt thiết bị vào localStorage để persist giữa các session.
 */
const saveDeviceSettings = (settings: DeviceSettings): void => {
  try {
    localStorage.setItem(DEVICE_SETTINGS_KEY, JSON.stringify(settings));
  } catch {
    // Bỏ qua lỗi ghi (quota exceeded, private mode...)
  }
};

// ──────────────────────────────────────────────────────────
// Zustand Store
// ──────────────────────────────────────────────────────────

export const useVoiceStore = create<VoiceState>((set, get) => ({
  // --- Initial state ---
  connectionStatus: 'idle',
  currentVoiceRoomId: null,
  currentVoiceRoomName: null,
  activeSession: null,
  tokenMetadata: null,
  incomingCall: null,
  latestCallEvent: null,
  callUiStatus: null,
  liveKitRoom: null,
  errorMessage: null,
  isMicEnabled: true,    // Mặc định bật mic khi join (giống Discord)
  isDeafened: false,
  isCameraEnabled: false, // Mặc định tắt cam (giống Discord)
  isScreenSharing: false,
  deviceSettings: loadDeviceSettings(),

  // --- Connection actions ---

  startConnecting: (roomId, roomName) =>
    set({
      connectionStatus: 'connecting',
      currentVoiceRoomId: roomId,
      currentVoiceRoomName: roomName,
      activeSession: {
        kind: 'channel',
        sourceRoomId: roomId,
        liveKitRoomName: roomId,
        displayName: roomName,
      },
      tokenMetadata: null,
      liveKitRoom: null,
      errorMessage: null,
    }),

  startConnectingSession: ({ session }) =>
    set({
      connectionStatus: 'connecting',
      currentVoiceRoomId: session.kind === 'channel' ? session.sourceRoomId : null,
      currentVoiceRoomName: session.displayName,
      activeSession: session,
      tokenMetadata: null,
      liveKitRoom: null,
      errorMessage: null,
    }),

  setTokenMetadata: (metadata) => set({ tokenMetadata: metadata }),

  setIncomingCall: (payload) =>
    set({
      incomingCall: payload,
      latestCallEvent: null,
      callUiStatus: 'incoming',
      errorMessage: null,
    }),

  clearIncomingCall: () =>
    set({
      incomingCall: null,
      callUiStatus: null,
    }),

  setCallAccepted: (payload) =>
    set({
      incomingCall: null,
      latestCallEvent: payload,
      callUiStatus: 'accepted',
    }),

  setCallDeclined: (payload) =>
    set({
      incomingCall: null,
      latestCallEvent: payload,
      callUiStatus: 'declined',
    }),

  setCallEnded: (payload) =>
    set({
      incomingCall: null,
      latestCallEvent: payload,
      callUiStatus: 'ended',
    }),

  clearCallEvent: () =>
    set({
      latestCallEvent: null,
      callUiStatus: null,
    }),

  setConnected: (liveKitRoom) =>
    set({
      connectionStatus: 'connected',
      liveKitRoom,
      errorMessage: null,
    }),

  setReconnecting: () =>
    set({ connectionStatus: 'reconnecting' }),

  setError: (message) =>
    set({
      connectionStatus: 'error',
      liveKitRoom: null,
      errorMessage: message,
    }),

  disconnect: () => {
    // Ngắt kết nối LiveKit Room nếu đang active
    const { liveKitRoom } = get();
    if (liveKitRoom) {
      liveKitRoom.disconnect();
    }

    // Reset toàn bộ trạng thái Voice về idle
    // GIỮ NGUYÊN deviceSettings (không reset vì là cài đặt user)
    set({
      connectionStatus: 'idle',
      currentVoiceRoomId: null,
      currentVoiceRoomName: null,
      activeSession: null,
      tokenMetadata: null,
      liveKitRoom: null,
      errorMessage: null,
      isMicEnabled: true,
      isDeafened: false,
      isCameraEnabled: false,
      isScreenSharing: false,
    });
  },

  // --- Media toggles ---

  setMicEnabled: (enabled) => set({ isMicEnabled: enabled }),

  setDeafened: (deafened) =>
    set({
      isDeafened: deafened,
      // Khi Deafen → tự động mute Mic (giống Discord)
      // Khi Un-deafen → KHÔNG tự động unmute (user phải tự bật lại)
      ...(deafened ? { isMicEnabled: false } : {}),
    }),

  setCameraEnabled: (enabled) => set({ isCameraEnabled: enabled }),

  setScreenSharing: (sharing) => set({ isScreenSharing: sharing }),

  // --- Device settings (persist vào localStorage) ---

  setSelectedMic: (deviceId) => {
    const newSettings = { ...get().deviceSettings, selectedMicId: deviceId };
    saveDeviceSettings(newSettings);
    set({ deviceSettings: newSettings });
  },

  setSelectedCam: (deviceId) => {
    const newSettings = { ...get().deviceSettings, selectedCamId: deviceId };
    saveDeviceSettings(newSettings);
    set({ deviceSettings: newSettings });
  },

  setSelectedSpeaker: (deviceId) => {
    const newSettings = { ...get().deviceSettings, selectedSpeakerId: deviceId };
    saveDeviceSettings(newSettings);
    set({ deviceSettings: newSettings });
  },
}));

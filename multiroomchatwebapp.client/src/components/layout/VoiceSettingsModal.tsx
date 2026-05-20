import { useEffect, useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { useVoiceStore } from '../../store/useVoiceStore';
import styles from './VoiceSettingsModal.module.css';

interface VoiceSettingsModalProps {
  /** Đóng modal cài đặt Voice. */
  onClose: () => void;
}

interface DeviceGroups {
  microphones: MediaDeviceInfo[];
  cameras: MediaDeviceInfo[];
  speakers: MediaDeviceInfo[];
}

interface DeviceSelectProps {
  id: string;
  label: string;
  devices: MediaDeviceInfo[];
  selectedDeviceId: string | null;
  onChange: (deviceId: string) => void;
  isLoading: boolean;
  errorMessage: string | null;
  emptyLabel: string;
  placeholderLabel: string;
}

const EMPTY_DEVICE_GROUPS: DeviceGroups = {
  microphones: [],
  cameras: [],
  speakers: [],
};

const DEVICE_LABEL_PREFIX: Record<MediaDeviceKind, string> = {
  audioinput: 'Microphone',
  audiooutput: 'Speaker',
  videoinput: 'Camera',
};

/**
 * Tạo nhãn fallback khi browser chưa cấp quyền nên `enumerateDevices()` chưa trả label thật.
 */
const getDeviceLabel = (device: MediaDeviceInfo, index: number): string =>
  device.label || `${DEVICE_LABEL_PREFIX[device.kind]} ${index + 1}`;

/**
 * Nhóm danh sách thiết bị media theo loại để modal render đúng từng select.
 */
const groupMediaDevices = (devices: MediaDeviceInfo[]): DeviceGroups => ({
  microphones: devices.filter((device) => device.kind === 'audioinput'),
  cameras: devices.filter((device) => device.kind === 'videoinput'),
  speakers: devices.filter((device) => device.kind === 'audiooutput'),
});

/**
 * Select chọn thiết bị voice, có trạng thái loading/error/empty dùng chung cho mic/cam/speaker.
 */
const DeviceSelect = ({
  id,
  label,
  devices,
  selectedDeviceId,
  onChange,
  isLoading,
  errorMessage,
  emptyLabel,
  placeholderLabel,
}: DeviceSelectProps) => {
  const hasSelectedDevice = Boolean(
    selectedDeviceId && devices.some((device) => device.deviceId === selectedDeviceId),
  );
  const selectValue = hasSelectedDevice ? selectedDeviceId ?? '' : '';
  const isDisabled = isLoading || Boolean(errorMessage) || devices.length === 0;
  const placeholder = isLoading
    ? 'Đang tải danh sách thiết bị...'
    : errorMessage
      ? 'Không thể tải danh sách thiết bị'
      : devices.length === 0
        ? emptyLabel
        : placeholderLabel;

  return (
    <>
      <label className={styles.fieldLabel} htmlFor={id}>{label}</label>
      <select
        id={id}
        className={styles.select}
        value={selectValue}
        disabled={isDisabled}
        onChange={(event) => {
          if (event.target.value) {
            onChange(event.target.value);
          }
        }}
      >
        <option value="" disabled>{placeholder}</option>
        {devices.map((device, index) => (
          <option key={`${device.kind}-${device.deviceId || index}`} value={device.deviceId}>
            {getDeviceLabel(device, index)}
          </option>
        ))}
      </select>
    </>
  );
};

/**
 * VoiceSettingsModal - Modal cài đặt thiết bị Voice.
 *
 * Input:
 * - onClose: callback đóng modal.
 *
 * Output:
 * - Load danh sách Mic/Camera/Speaker từ browser và lưu lựa chọn vào useVoiceStore/localStorage.
 *
 * Error cases:
 * - Browser không hỗ trợ MediaDevices API: hiển thị trạng thái không thể tải danh sách.
 * - `enumerateDevices()` bị chặn/lỗi: giữ modal ổn định và cho user đóng modal.
 */
export const VoiceSettingsModal = ({ onClose }: VoiceSettingsModalProps) => {
  const deviceSettings = useVoiceStore((s) => s.deviceSettings);
  const liveKitRoom = useVoiceStore((s) => s.liveKitRoom);
  const connectionStatus = useVoiceStore((s) => s.connectionStatus);
  const setSelectedMic = useVoiceStore((s) => s.setSelectedMic);
  const setSelectedCam = useVoiceStore((s) => s.setSelectedCam);
  const setSelectedSpeaker = useVoiceStore((s) => s.setSelectedSpeaker);
  const [deviceGroups, setDeviceGroups] = useState<DeviceGroups>(EMPTY_DEVICE_GROUPS);
  const [isLoadingDevices, setIsLoadingDevices] = useState(true);
  const [deviceError, setDeviceError] = useState<string | null>(null);
  const [micLevel, setMicLevel] = useState(0);
  const [micTestError, setMicTestError] = useState<string | null>(null);
  const micTestStreamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const mediaSourceRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const animationFrameRef = useRef<number | null>(null);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  useEffect(() => {
    let isMounted = true;
    const mediaDevices = typeof navigator !== 'undefined' ? navigator.mediaDevices : undefined;

    const loadDevices = async () => {
      if (!mediaDevices?.enumerateDevices) {
        if (isMounted) {
          setDeviceGroups(EMPTY_DEVICE_GROUPS);
          setDeviceError('Trình duyệt không hỗ trợ đọc danh sách thiết bị media.');
          setIsLoadingDevices(false);
        }
        return;
      }

      setIsLoadingDevices(true);
      setDeviceError(null);

      try {
        const devices = await mediaDevices.enumerateDevices();
        if (!isMounted) return;

        setDeviceGroups(groupMediaDevices(devices));
      } catch (error) {
        if (!isMounted) return;

        console.error('Lỗi khi tải danh sách thiết bị Voice:', error);
        setDeviceGroups(EMPTY_DEVICE_GROUPS);
        setDeviceError('Không thể đọc danh sách thiết bị media.');
      } finally {
        if (isMounted) {
          setIsLoadingDevices(false);
        }
      }
    };

    const handleDeviceChange = () => {
      void loadDevices();
    };

    void loadDevices();
    mediaDevices?.addEventListener?.('devicechange', handleDeviceChange);

    return () => {
      isMounted = false;
      mediaDevices?.removeEventListener?.('devicechange', handleDeviceChange);
    };
  }, []);

  const stopMicTest = (resetLevel = true) => {
    if (animationFrameRef.current !== null) {
      cancelAnimationFrame(animationFrameRef.current);
      animationFrameRef.current = null;
    }

    mediaSourceRef.current?.disconnect();
    mediaSourceRef.current = null;

    analyserRef.current?.disconnect();
    analyserRef.current = null;

    micTestStreamRef.current?.getTracks().forEach((track) => track.stop());
    micTestStreamRef.current = null;

    if (audioContextRef.current && audioContextRef.current.state !== 'closed') {
      void audioContextRef.current.close();
    }
    audioContextRef.current = null;

    if (resetLevel) {
      setMicLevel(0);
    }
  };

  const startMicTest = async (deviceId: string | null) => {
    stopMicTest();
    setMicTestError(null);

    const mediaDevices = typeof navigator !== 'undefined' ? navigator.mediaDevices : undefined;
    if (!mediaDevices?.getUserMedia) {
      setMicTestError('Trình duyệt không hỗ trợ test microphone.');
      return;
    }

    try {
      const stream = await mediaDevices.getUserMedia({
        audio: deviceId ? { deviceId: { exact: deviceId } } : true,
        video: false,
      });
      const audioContext = new AudioContext();
      const analyser = audioContext.createAnalyser();
      const source = audioContext.createMediaStreamSource(stream);

      analyser.fftSize = 256;
      source.connect(analyser);

      micTestStreamRef.current = stream;
      audioContextRef.current = audioContext;
      analyserRef.current = analyser;
      mediaSourceRef.current = source;

      const dataArray = new Uint8Array(analyser.frequencyBinCount);

      const tick = () => {
        if (analyserRef.current !== analyser) return;

        analyser.getByteTimeDomainData(dataArray);

        let sum = 0;
        for (const value of dataArray) {
          const normalized = (value - 128) / 128;
          sum += normalized * normalized;
        }

        const rms = Math.sqrt(sum / dataArray.length);
        setMicLevel(Math.min(100, Math.round(rms * 180)));
        animationFrameRef.current = requestAnimationFrame(tick);
      };

      tick();
    } catch (error) {
      stopMicTest();
      console.error('Lỗi khi test microphone:', error);
      setMicTestError('Không thể test microphone.');
    }
  };

  useEffect(() => {
    void startMicTest(deviceSettings.selectedMicId);

    return () => {
      stopMicTest(false);
    };
  }, [deviceSettings.selectedMicId]);

  const persistDeviceSelection = (kind: MediaDeviceKind, deviceId: string) => {
    if (kind === 'audioinput') {
      setSelectedMic(deviceId);
      return;
    }

    if (kind === 'videoinput') {
      setSelectedCam(deviceId);
      return;
    }

    if (kind === 'audiooutput') {
      setSelectedSpeaker(deviceId);
    }
  };

  const getDeviceSwitchErrorMessage = (kind: MediaDeviceKind): string => {
    if (kind === 'audioinput') return 'Không thể đổi microphone.';
    if (kind === 'videoinput') return 'Không thể đổi camera.';
    return 'Không thể đổi speaker trên trình duyệt hiện tại.';
  };

  const handleDeviceSelection = async (kind: MediaDeviceKind, deviceId: string) => {
    if (connectionStatus !== 'connected' || !liveKitRoom) {
      persistDeviceSelection(kind, deviceId);
      return;
    }

    try {
      await liveKitRoom.switchActiveDevice(kind, deviceId);
      persistDeviceSelection(kind, deviceId);
    } catch (error) {
      toast.error(getDeviceSwitchErrorMessage(kind));
      console.error('Lỗi khi đổi thiết bị Voice:', error);
    }
  };

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div
        className={styles.modal}
        role="dialog"
        aria-modal="true"
        aria-labelledby="voice-settings-title"
        onClick={(event) => event.stopPropagation()}
      >
        <div className={styles.header}>
          <div>
            <p className={styles.eyebrow}>Voice</p>
            <h2 id="voice-settings-title" className={styles.title}>Cài đặt Voice</h2>
          </div>
          <button className={styles.closeBtn} onClick={onClose} title="Đóng">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none"
              stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </div>

        <div className={styles.content}>
          {deviceError && (
            <p className={styles.deviceError} role="alert">{deviceError}</p>
          )}

          <section className={styles.section}>
            <div className={styles.sectionTitleRow}>
              <div className={styles.sectionIcon}>
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none"
                  stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z" />
                  <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                  <line x1="12" y1="19" x2="12" y2="23" />
                  <line x1="8" y1="23" x2="16" y2="23" />
                </svg>
              </div>
              <h3>Âm thanh đầu vào</h3>
            </div>
            <DeviceSelect
              id="voice-mic-select"
              label="Microphone"
              devices={deviceGroups.microphones}
              selectedDeviceId={deviceSettings.selectedMicId}
              onChange={(deviceId) => void handleDeviceSelection('audioinput', deviceId)}
              isLoading={isLoadingDevices}
              errorMessage={deviceError}
              emptyLabel="Không tìm thấy microphone"
              placeholderLabel="Chọn microphone"
            />
            <div className={styles.meterShell} aria-label="Mức âm lượng microphone">
              {Array.from({ length: 10 }).map((_, index) => (
                <div
                  key={index}
                  className={`${styles.meterBar} ${
                    micLevel >= (index + 1) * 10 ? styles.meterBarActive : ''
                  }`}
                />
              ))}
            </div>
            {micTestError && (
              <p className={styles.micTestError}>{micTestError}</p>
            )}
          </section>

          <section className={styles.section}>
            <div className={styles.sectionTitleRow}>
              <div className={styles.sectionIcon}>
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none"
                  stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <polygon points="23 7 16 12 23 17 23 7" />
                  <rect x="1" y="5" width="15" height="14" rx="2" ry="2" />
                </svg>
              </div>
              <h3>Camera</h3>
            </div>
            <DeviceSelect
              id="voice-camera-select"
              label="Camera"
              devices={deviceGroups.cameras}
              selectedDeviceId={deviceSettings.selectedCamId}
              onChange={(deviceId) => void handleDeviceSelection('videoinput', deviceId)}
              isLoading={isLoadingDevices}
              errorMessage={deviceError}
              emptyLabel="Không tìm thấy camera"
              placeholderLabel="Chọn camera"
            />
          </section>

          <section className={styles.section}>
            <div className={styles.sectionTitleRow}>
              <div className={styles.sectionIcon}>
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none"
                  stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M3 18v-6a9 9 0 0 1 18 0v6" />
                  <path d="M21 19a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3zM3 19a2 2 0 0 0 2 2h1a2 2 0 0 0 2-2v-3a2 2 0 0 0-2-2H3z" />
                </svg>
              </div>
              <h3>Âm thanh đầu ra</h3>
            </div>
            <DeviceSelect
              id="voice-speaker-select"
              label="Speaker"
              devices={deviceGroups.speakers}
              selectedDeviceId={deviceSettings.selectedSpeakerId}
              onChange={(deviceId) => void handleDeviceSelection('audiooutput', deviceId)}
              isLoading={isLoadingDevices}
              errorMessage={deviceError}
              emptyLabel="Không tìm thấy speaker"
              placeholderLabel="Chọn speaker"
            />
          </section>
        </div>

        <div className={styles.footer}>
          <button className={styles.secondaryBtn} onClick={onClose}>Đóng</button>
        </div>
      </div>
    </div>
  );
};

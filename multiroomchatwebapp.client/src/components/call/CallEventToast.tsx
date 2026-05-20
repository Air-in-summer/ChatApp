import { useEffect } from 'react';
import toast from 'react-hot-toast';
import { useVoiceStore } from '../../store/useVoiceStore';

const AUTO_CLEAR_DELAY_MS = 3500;

const getMessage = (status: 'accepted' | 'declined' | 'ended', sessionStatus?: string) => {
  switch (status) {
    case 'accepted':
      return 'Cuoc goi da duoc nghe may.';
    case 'declined':
      return 'Cuoc goi da bi tu choi.';
    case 'ended':
      if (sessionStatus === 'Missed') {
        return 'Cuoc goi bi nho hoac khong co phan hoi.';
      }
      return 'Cuoc goi da ket thuc.';
  }
};

/**
 * Hien thi cac event call da nhan qua SignalR.
 * Component nay chi render side-effect toast va tu clear store sau khi UI da thong bao.
 */
export const CallEventToast = () => {
  const latestCallEvent = useVoiceStore((s) => s.latestCallEvent);
  const callUiStatus = useVoiceStore((s) => s.callUiStatus);
  const clearCallEvent = useVoiceStore((s) => s.clearCallEvent);

  useEffect(() => {
    if (!latestCallEvent) return;
    if (
      callUiStatus !== 'accepted' &&
      callUiStatus !== 'declined' &&
      callUiStatus !== 'ended'
    ) {
      return;
    }

    const toastId = `voice-call-${callUiStatus}-${latestCallEvent.session.sessionId}`;
    const isMissed = callUiStatus === 'ended' && latestCallEvent.session.status === 'Missed';
    const message = getMessage(callUiStatus, latestCallEvent.session.status);

    if (callUiStatus === 'accepted') {
      toast.success(message, { id: toastId });
    } else if (callUiStatus === 'declined' || isMissed) {
      toast.error(message, { id: toastId });
    } else {
      toast(message, { id: toastId });
    }

    const timerId = window.setTimeout(() => {
      clearCallEvent();
    }, AUTO_CLEAR_DELAY_MS);

    return () => {
      window.clearTimeout(timerId);
    };
  }, [callUiStatus, latestCallEvent, clearCallEvent]);

  return null;
};

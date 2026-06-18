import { useEffect, useState } from 'react';
import { ConfirmDialog } from '../ui/ConfirmDialog/ConfirmDialog';
import {
  registerVoiceSwitchConfirmHandler,
  type VoiceSwitchConfirmRequest,
} from '../../services/voiceSwitchConfirmService';

export const VoiceSwitchConfirmDialog = () => {
  const [pendingRequest, setPendingRequest] = useState<VoiceSwitchConfirmRequest | null>(null);

  useEffect(() => {
    return registerVoiceSwitchConfirmHandler((request) => {
      setPendingRequest((currentRequest) => {
        currentRequest?.resolve(false);
        return request;
      });
    });
  }, []);

  const closeWithResult = (confirmed: boolean) => {
    pendingRequest?.resolve(confirmed);
    setPendingRequest(null);
  };

  return (
    <ConfirmDialog
      open={Boolean(pendingRequest)}
      title="Chuyển phiên voice"
      message={pendingRequest?.message ?? ''}
      confirmLabel="Tiếp tục"
      cancelLabel="Hủy"
      variant="danger"
      onCancel={() => closeWithResult(false)}
      onConfirm={() => closeWithResult(true)}
    />
  );
};

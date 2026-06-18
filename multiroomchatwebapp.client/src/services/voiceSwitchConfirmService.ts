import type { ReactNode } from 'react';

export interface VoiceSwitchConfirmRequest {
  message: ReactNode;
  resolve: (confirmed: boolean) => void;
}

type VoiceSwitchConfirmHandler = (request: VoiceSwitchConfirmRequest) => void;

let confirmHandler: VoiceSwitchConfirmHandler | null = null;

export const registerVoiceSwitchConfirmHandler = (handler: VoiceSwitchConfirmHandler) => {
  confirmHandler = handler;

  return () => {
    if (confirmHandler === handler) {
      confirmHandler = null;
    }
  };
};

export const requestVoiceSwitchConfirmation = (message: ReactNode): Promise<boolean> => {
  if (!confirmHandler) {
    console.warn('Voice switch confirm dialog is not mounted.');
    return Promise.resolve(false);
  }

  return new Promise<boolean>((resolve) => {
    confirmHandler?.({ message, resolve });
  });
};

import type { ReactNode } from 'react';
import { Button } from '../Button/Button';
import { Modal } from '../Modal/Modal';
import styles from './ConfirmDialog.module.css';

export type ConfirmVariant = 'default' | 'danger';

export interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: ConfirmVariant;
  loading?: boolean;
  onConfirm: () => void | Promise<void>;
  onCancel: () => void;
}

export const ConfirmDialog = ({
  open,
  title,
  message,
  confirmLabel = 'Xác nhận',
  cancelLabel = 'Hủy',
  variant = 'default',
  loading = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) => {
  return (
    <Modal
      open={open}
      title={title}
      size="sm"
      chrome="confirm"
      closeOnOverlayClick={!loading}
      closeOnEscape={!loading}
      closeDisabled={loading}
      onClose={onCancel}
      footer={(
        <>
          <Button
            className={styles.cancelButton}
            variant="secondary"
            size="sm"
            fullWidth={false}
            disabled={loading}
            onClick={onCancel}
          >
            {cancelLabel}
          </Button>
          <Button
            className={styles.confirmButton}
            variant={variant === 'danger' ? 'danger' : 'primary'}
            size="sm"
            fullWidth={false}
            loading={loading}
            onClick={() => {
              void onConfirm();
            }}
          >
            {confirmLabel}
          </Button>
        </>
      )}
    >
      <div className={`${styles.message} ${variant === 'danger' ? styles.danger : ''}`}>
        {message}
      </div>
    </Modal>
  );
};

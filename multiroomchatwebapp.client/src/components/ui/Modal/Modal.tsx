import { useEffect, useId, type ReactNode } from 'react';
import { IconButton } from '../IconButton/IconButton';
import styles from './Modal.module.css';

export type ModalSize = 'sm' | 'md' | 'lg';
export type ModalChrome = 'default' | 'confirm';

export interface ModalProps {
  open: boolean;
  title?: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  size?: ModalSize;
  chrome?: ModalChrome;
  closeOnOverlayClick?: boolean;
  closeOnEscape?: boolean;
  closeDisabled?: boolean;
  onClose: () => void;
}

const CloseIcon = () => (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
    <path d="M18 6 6 18" />
    <path d="m6 6 12 12" />
  </svg>
);

export const Modal = ({
  open,
  title,
  description,
  children,
  footer,
  size = 'md',
  chrome = 'default',
  closeOnOverlayClick = true,
  closeOnEscape = true,
  closeDisabled = false,
  onClose,
}: ModalProps) => {
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    if (!open || !closeOnEscape) return undefined;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [closeOnEscape, onClose, open]);

  useEffect(() => {
    if (!open) return undefined;

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    return () => {
      document.body.style.overflow = previousOverflow;
    };
  }, [open]);

  if (!open) return null;

  return (
    <div
      className={styles.overlay}
      onMouseDown={(event) => {
        if (closeOnOverlayClick && event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <section
        className={`${styles.modal} ${styles[size]} ${styles[chrome]}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? titleId : undefined}
        aria-describedby={description ? descriptionId : undefined}
        onMouseDown={(event) => event.stopPropagation()}
      >
        {(title || description) && (
          <header className={styles.header}>
            <div className={styles.heading}>
              {title && <h2 id={titleId} className={styles.title}>{title}</h2>}
              {description && (
                <p id={descriptionId} className={styles.description}>{description}</p>
              )}
            </div>
            <IconButton
              aria-label="Đóng"
              icon={<CloseIcon />}
              size="sm"
              variant="ghost"
              tooltip="Đóng"
              disabled={closeDisabled}
              onClick={onClose}
            />
          </header>
        )}

        <div className={styles.body}>{children}</div>

        {footer && <footer className={styles.footer}>{footer}</footer>}
      </section>
    </div>
  );
};

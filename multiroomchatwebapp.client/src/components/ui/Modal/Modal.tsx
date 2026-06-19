import {
  useEffect,
  useId,
  useRef,
  type ReactNode,
  type RefObject,
} from 'react';
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
  initialFocusRef?: RefObject<HTMLElement | null>;
  onClose: () => void;
}

const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');

const openModalStack: string[] = [];
let bodyScrollLockCount = 0;
let originalBodyOverflow = '';

const isTopModal = (modalId: string): boolean =>
  openModalStack[openModalStack.length - 1] === modalId;

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
  initialFocusRef,
  onClose,
}: ModalProps) => {
  const modalId = useId();
  const titleId = useId();
  const descriptionId = useId();
  const modalRef = useRef<HTMLElement>(null);
  const returnFocusRef = useRef<HTMLElement | null>(null);
  const onCloseRef = useRef(onClose);
  const closeOnEscapeRef = useRef(closeOnEscape);
  const closeDisabledRef = useRef(closeDisabled);
  onCloseRef.current = onClose;
  closeOnEscapeRef.current = closeOnEscape;
  closeDisabledRef.current = closeDisabled;

  useEffect(() => {
    if (!open) return undefined;

    returnFocusRef.current = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    openModalStack.push(modalId);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (!isTopModal(modalId)) return;

      if (event.key === 'Escape' && closeOnEscapeRef.current && !closeDisabledRef.current) {
        event.preventDefault();
        onCloseRef.current();
        return;
      }

      if (event.key !== 'Tab') return;

      const focusableElements = Array.from(
        modalRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR) ?? [],
      ).filter(element => !element.hasAttribute('disabled') && element.tabIndex !== -1);

      if (focusableElements.length === 0) {
        event.preventDefault();
        modalRef.current?.focus();
        return;
      }

      const firstElement = focusableElements[0];
      const lastElement = focusableElements[focusableElements.length - 1];
      const activeElement = document.activeElement;

      if (event.shiftKey && (activeElement === firstElement || !modalRef.current?.contains(activeElement))) {
        event.preventDefault();
        lastElement.focus();
      } else if (!event.shiftKey && activeElement === lastElement) {
        event.preventDefault();
        firstElement.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown);

    const focusTarget =
      initialFocusRef?.current
      ?? modalRef.current?.querySelector<HTMLElement>('[autofocus]')
      ?? modalRef.current?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR)
      ?? modalRef.current;
    window.requestAnimationFrame(() => focusTarget?.focus());

    if (bodyScrollLockCount === 0) {
      originalBodyOverflow = document.body.style.overflow;
      document.body.style.overflow = 'hidden';
    }
    bodyScrollLockCount += 1;

    return () => {
      document.removeEventListener('keydown', handleKeyDown);

      const stackIndex = openModalStack.lastIndexOf(modalId);
      if (stackIndex >= 0) {
        openModalStack.splice(stackIndex, 1);
      }

      bodyScrollLockCount = Math.max(0, bodyScrollLockCount - 1);
      if (bodyScrollLockCount === 0) {
        document.body.style.overflow = originalBodyOverflow;
      }

      window.requestAnimationFrame(() => {
        if (returnFocusRef.current?.isConnected) {
          returnFocusRef.current.focus();
        }
      });
    };
  }, [initialFocusRef, modalId, open]);

  if (!open) return null;

  return (
    <div
      className={styles.overlay}
      onMouseDown={(event) => {
        if (
          isTopModal(modalId)
          && closeOnOverlayClick
          && !closeDisabled
          && event.target === event.currentTarget
        ) {
          onClose();
        }
      }}
    >
      <section
        ref={modalRef}
        className={`${styles.modal} ${styles[size]} ${styles[chrome]}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? titleId : undefined}
        aria-describedby={description ? descriptionId : undefined}
        tabIndex={-1}
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

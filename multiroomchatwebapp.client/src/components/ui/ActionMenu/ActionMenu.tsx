import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { IconButton } from '../IconButton/IconButton';
import styles from './ActionMenu.module.css';

export type ActionMenuItemVariant = 'default' | 'danger';
export type ActionMenuAlign = 'start' | 'end';

export interface ActionMenuItem {
  id: string;
  label: string;
  icon?: ReactNode;
  variant?: ActionMenuItemVariant;
  disabled?: boolean;
  loading?: boolean;
  hidden?: boolean;
  onSelect: () => void | Promise<void>;
}

export interface ActionMenuProps {
  triggerLabel: string;
  items: ActionMenuItem[];
  align?: ActionMenuAlign;
  disabled?: boolean;
  triggerIcon?: ReactNode;
  className?: string;
  menuClassName?: string;
}

const DotsIcon = () => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <circle cx="5" cy="12" r="2" />
    <circle cx="12" cy="12" r="2" />
    <circle cx="19" cy="12" r="2" />
  </svg>
);

export const ActionMenu = ({
  triggerLabel,
  items,
  align = 'end',
  disabled = false,
  triggerIcon,
  className = '',
  menuClassName = '',
}: ActionMenuProps) => {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const menuId = useId();
  const visibleItems = items.filter((item) => !item.hidden);

  useEffect(() => {
    if (!open) return undefined;

    const handlePointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    };

    window.addEventListener('pointerdown', handlePointerDown);
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('pointerdown', handlePointerDown);
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [open]);

  return (
    <div ref={rootRef} className={`${styles.root} ${className}`}>
      <IconButton
        aria-label={triggerLabel}
        icon={triggerIcon ?? <DotsIcon />}
        variant="subtle"
        size="sm"
        active={open}
        disabled={disabled || visibleItems.length === 0}
        tooltip={triggerLabel}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((current) => !current)}
      />

      {open && (
        <div
          id={menuId}
          className={`${styles.menu} ${align === 'start' ? styles.alignStart : styles.alignEnd} ${menuClassName}`}
          role="menu"
        >
          {visibleItems.map((item) => {
            const isDisabled = Boolean(item.disabled || item.loading);

            return (
              <button
                key={item.id}
                type="button"
                className={`${styles.item} ${item.variant === 'danger' ? styles.dangerItem : ''}`}
                role="menuitem"
                disabled={isDisabled}
                aria-busy={item.loading || undefined}
                onClick={() => {
                  if (isDisabled) return;
                  void item.onSelect();
                  setOpen(false);
                }}
              >
                {item.loading ? (
                  <span className={styles.loader} aria-hidden="true" />
                ) : item.icon ? (
                  <span className={styles.itemIcon} aria-hidden="true">{item.icon}</span>
                ) : null}
                <span className={styles.itemLabel}>{item.label}</span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
};

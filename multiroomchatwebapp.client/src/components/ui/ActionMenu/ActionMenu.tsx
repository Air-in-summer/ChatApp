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
  const triggerRef = useRef<HTMLButtonElement>(null);
  const itemRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const menuId = useId();
  const visibleItems = items.filter((item) => !item.hidden);

  const closeMenu = (restoreFocus = false) => {
    setOpen(false);
    if (restoreFocus) {
      window.requestAnimationFrame(() => triggerRef.current?.focus());
    }
  };

  const focusItem = (index: number) => {
    const enabledItems = itemRefs.current.filter(
      (item): item is HTMLButtonElement => Boolean(item && !item.disabled),
    );
    if (enabledItems.length === 0) return;

    const normalizedIndex = (index + enabledItems.length) % enabledItems.length;
    enabledItems[normalizedIndex].focus();
  };

  useEffect(() => {
    if (!open) return undefined;

    const handlePointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        closeMenu();
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        closeMenu(true);
      }
    };

    window.addEventListener('pointerdown', handlePointerDown);
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('pointerdown', handlePointerDown);
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    itemRefs.current = itemRefs.current.slice(0, visibleItems.length);
  }, [open, visibleItems.length]);

  const handleMenuKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const enabledItems = itemRefs.current.filter(
      (item): item is HTMLButtonElement => Boolean(item && !item.disabled),
    );
    if (enabledItems.length === 0) return;

    const currentIndex = enabledItems.findIndex(item => item === document.activeElement);

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        focusItem(currentIndex + 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        focusItem(currentIndex <= 0 ? enabledItems.length - 1 : currentIndex - 1);
        break;
      case 'Home':
        event.preventDefault();
        focusItem(0);
        break;
      case 'End':
        event.preventDefault();
        focusItem(enabledItems.length - 1);
        break;
    }
  };

  return (
    <div ref={rootRef} className={`${styles.root} ${className}`}>
      <IconButton
        ref={triggerRef}
        aria-label={triggerLabel}
        icon={triggerIcon ?? <DotsIcon />}
        variant="subtle"
        size="sm"
        active={open}
        disabled={disabled || visibleItems.length === 0}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((current) => !current)}
        onKeyDown={(event) => {
          if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
          event.preventDefault();
          setOpen(true);
          window.requestAnimationFrame(() => {
            focusItem(event.key === 'ArrowUp' ? -1 : 0);
          });
        }}
      />

      {open && (
        <div
          id={menuId}
          className={`${styles.menu} ${align === 'start' ? styles.alignStart : styles.alignEnd} ${menuClassName}`}
          role="menu"
          aria-label={triggerLabel}
          onKeyDown={handleMenuKeyDown}
        >
          {visibleItems.map((item, index) => {
            const isDisabled = Boolean(item.disabled || item.loading);

            return (
              <button
                ref={(element) => {
                  itemRefs.current[index] = element;
                }}
                key={item.id}
                type="button"
                className={`${styles.item} ${item.variant === 'danger' ? styles.dangerItem : ''}`}
                role="menuitem"
                disabled={isDisabled}
                aria-busy={item.loading || undefined}
                onClick={() => {
                  if (isDisabled) return;
                  void item.onSelect();
                  closeMenu(true);
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

import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';
import styles from './IconButton.module.css';

export type IconButtonVariant = 'ghost' | 'subtle' | 'primary' | 'danger' | 'success';
export type IconButtonSize = 'sm' | 'md';

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  'aria-label': string;
  icon: ReactNode;
  variant?: IconButtonVariant;
  size?: IconButtonSize;
  active?: boolean;
  loading?: boolean;
}

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(({
  'aria-label': ariaLabel,
  icon,
  variant = 'ghost',
  size = 'md',
  active = false,
  loading = false,
  className = '',
  disabled,
  type = 'button',
  ...props
}, ref) => {
  const buttonClassName = [
    styles.iconButton,
    styles[variant],
    styles[size],
    active ? styles.active : '',
    loading ? styles.loading : '',
    className,
  ].filter(Boolean).join(' ');
  return (
    <button
      ref={ref}
      type={type}
      className={buttonClassName}
      disabled={disabled || loading}
      aria-label={ariaLabel}
      aria-busy={loading || undefined}
      aria-pressed={active || undefined}
      {...props}
    >
      {loading ? (
        <span className={styles.loader} aria-hidden="true" />
      ) : (
        <span className={styles.icon} aria-hidden="true">{icon}</span>
      )}
    </button>
  );
});

IconButton.displayName = 'IconButton';

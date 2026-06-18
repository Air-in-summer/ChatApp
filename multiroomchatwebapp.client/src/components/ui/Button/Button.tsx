import type { ButtonHTMLAttributes, ReactNode } from 'react';
import styles from './Button.module.css';

export type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  isLoading?: boolean;
  leftIcon?: ReactNode;
  rightIcon?: ReactNode;
  fullWidth?: boolean;
}

export const Button = ({
  children,
  variant = 'primary',
  size = 'md',
  loading,
  isLoading = false,
  leftIcon,
  rightIcon,
  fullWidth = true,
  className = '',
  disabled,
  ...props
}: ButtonProps) => {
  const isBusy = loading ?? isLoading;
  const buttonClassName = [
    styles.button,
    styles[variant],
    styles[size],
    fullWidth ? styles.fullWidth : '',
    isBusy ? styles.loading : '',
    className,
  ].filter(Boolean).join(' ');

  return (
    <button
      className={buttonClassName}
      disabled={disabled || isBusy}
      aria-busy={isBusy || undefined}
      {...props}
    >
      {isBusy ? (
        <span className={styles.loader} aria-hidden="true" />
      ) : leftIcon ? (
        <span className={styles.icon} aria-hidden="true">{leftIcon}</span>
      ) : null}
      {children !== undefined && children !== null && (
        <span className={styles.content}>{children}</span>
      )}
      {!isBusy && rightIcon && (
        <span className={styles.icon} aria-hidden="true">{rightIcon}</span>
      )}
    </button>
  );
};

import { forwardRef, useId } from 'react';
import type { InputHTMLAttributes } from 'react';
import styles from './InputText.module.css';

interface InputTextProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
}

export const InputText = forwardRef<HTMLInputElement, InputTextProps>(
  ({ label, error, className = '', id, 'aria-describedby': ariaDescribedBy, ...props }, ref) => {
    const generatedId = useId();
    const inputId = id ?? generatedId;
    const errorId = `${inputId}-error`;
    const describedBy = [ariaDescribedBy, error ? errorId : null].filter(Boolean).join(' ') || undefined;

    return (
      <div className={`${styles.container} ${className}`}>
        <label className={styles.label} htmlFor={inputId}>{label}</label>
        <input
          ref={ref}
          id={inputId}
          className={`${styles.input} ${error ? styles.inputError : ''}`}
          aria-invalid={Boolean(error) || undefined}
          aria-describedby={describedBy}
          {...props}
        />
        {error && (
          <span id={errorId} className={styles.errorMessage} role="alert">
            {error}
          </span>
        )}
      </div>
    );
  }
);

InputText.displayName = 'InputText';

import { forwardRef } from 'react';
import type { InputHTMLAttributes } from 'react';
import styles from './InputText.module.css';

interface InputTextProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
}

export const InputText = forwardRef<HTMLInputElement, InputTextProps>(
  ({ label, error, className = '', ...props }, ref) => {
    return (
      <div className={`${styles.container} ${className}`}>
        <label className={styles.label}>{label}</label>
        <input
          ref={ref}
          className={`${styles.input} ${error ? styles.inputError : ''}`}
          {...props}
        />
        {error && <span className={styles.errorMessage}>{error}</span>}
      </div>
    );
  }
);

InputText.displayName = 'InputText';

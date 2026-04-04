import type { ReactNode } from 'react';
import styles from './GlassCard.module.css';

interface GlassCardProps {
  children: ReactNode;
  className?: string;
  title?: string;
}

export const GlassCard = ({ children, className = '', title }: GlassCardProps) => {
  return (
    <div className={`${styles.card} ${className}`}>
      {title && <h2 className={styles.title}>{title}</h2>}
      {children}
    </div>
  );
};

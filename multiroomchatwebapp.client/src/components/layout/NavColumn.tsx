import styles from './NavColumn.module.css';

interface NavColumnProps {
  activeContext: 'dm' | 'group';
  onContextChange: (ctx: 'dm' | 'group') => void;
  onProfileClick: () => void;
}

/**
 * Cot 1: thanh dieu huong ngu canh va entry point tai khoan.
 */
export const NavColumn = ({ activeContext, onContextChange, onProfileClick }: NavColumnProps) => {
  return (
    <nav className={styles.navColumn}>
      <div className={styles.logo}>
        <svg width="28" height="28" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
      </div>

      <div className={styles.divider} />

      <button
        className={`${styles.navBtn} ${activeContext === 'dm' ? styles.active : ''}`}
        onClick={() => onContextChange('dm')}
        title="Tin nhắn riêng"
      >
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <path d="M20 2H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h14l4 4V4a2 2 0 0 0-2-2z" />
        </svg>
      </button>

      <button
        className={`${styles.navBtn} ${activeContext === 'group' ? styles.active : ''}`}
        onClick={() => onContextChange('group')}
        title="Nhóm"
      >
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
          <circle cx="9" cy="7" r="4" />
          <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
          <path d="M16 3.13a4 4 0 0 1 0 7.75" />
        </svg>
      </button>

      <div className={styles.spacer} />

      <button
        className={styles.navBtn}
        onClick={onProfileClick}
        title="Tài khoản"
      >
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
          <circle cx="12" cy="7" r="4" />
        </svg>
      </button>
    </nav>
  );
};

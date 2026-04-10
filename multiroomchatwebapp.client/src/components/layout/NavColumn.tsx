import styles from './NavColumn.module.css';

interface NavColumnProps {
  activeContext: 'dm' | 'group';
  onContextChange: (ctx: 'dm' | 'group') => void;
}

/**
 * Cột 1: Thanh điều hướng ngữ cảnh (Nhỏ nhất).
 * Cho phép chuyển giữa Tin nhắn riêng (DM) và Nhóm (Group).
 */
export const NavColumn = ({ activeContext, onContextChange }: NavColumnProps) => {
  return (
    <nav className={styles.navColumn}>
      {/* Logo / Avatar placeholder */}
      <div className={styles.logo}>
        <svg width="28" height="28" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
      </div>

      <div className={styles.divider} />

      {/* Nút chuyển sang Tin nhắn riêng */}
      <button
        className={`${styles.navBtn} ${activeContext === 'dm' ? styles.active : ''}`}
        onClick={() => onContextChange('dm')}
        title="Tin nhắn riêng"
      >
        {/* Icon Direct Message */}
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <path d="M20 2H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h14l4 4V4a2 2 0 0 0-2-2z" />
        </svg>
      </button>

      {/* Nút chuyển sang Nhóm (Placeholder) */}
      <button
        className={`${styles.navBtn} ${activeContext === 'group' ? styles.active : ''}`}
        onClick={() => onContextChange('group')}
        title="Nhóm (Sắp ra mắt)"
        disabled
      >
        {/* Icon Group */}
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
          <circle cx="9" cy="7" r="4" />
          <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
          <path d="M16 3.13a4 4 0 0 1 0 7.75" />
        </svg>
        <span className={styles.comingSoonBadge}>Soon</span>
      </button>
    </nav>
  );
};

import { useEffect, useState } from 'react';
import styles from './NavColumn.module.css';

export type NavContext = 'friends' | 'dm' | 'group';

interface NavColumnProps {
  activeContext: NavContext;
  onContextChange: (ctx: NavContext) => void;
  onProfileClick: () => void;
  profileAvatarUrl?: string | null;
  profileDisplayName?: string | null;
  profileUsername?: string | null;
}

/**
 * Cot 1: thanh dieu huong ngu canh va entry point tai khoan.
 */
export const NavColumn = ({
  activeContext,
  onContextChange,
  onProfileClick,
  profileAvatarUrl,
  profileDisplayName,
  profileUsername,
}: NavColumnProps) => {
  const [hasAvatarError, setHasAvatarError] = useState(false);
  const fallbackText = (profileDisplayName || profileUsername || '?').trim().charAt(0).toUpperCase() || '?';

  useEffect(() => {
    setHasAvatarError(false);
  }, [profileAvatarUrl]);

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
        className={`${styles.navBtn} ${activeContext === 'friends' ? styles.active : ''}`}
        onClick={() => onContextChange('friends')}
        title="Ban be"
      >
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2">
          <path d="M16 11c1.66 0 3-1.57 3-3.5S17.66 4 16 4s-3 1.57-3 3.5S14.34 11 16 11z" />
          <path d="M8 11c1.66 0 3-1.57 3-3.5S9.66 4 8 4 5 5.57 5 7.5 6.34 11 8 11z" />
          <path d="M2 20c0-3.31 2.69-6 6-6" />
          <path d="M22 20c0-3.31-2.69-6-6-6" />
          <path d="M8 14c2.21 0 4 1.79 4 4v2" />
          <path d="M16 14c-2.21 0-4 1.79-4 4v2" />
        </svg>
      </button>

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
        className={`${styles.navBtn} ${styles.profileBtn}`}
        onClick={onProfileClick}
        title="Tài khoản"
      >
        {profileAvatarUrl && !hasAvatarError ? (
          <img
            className={styles.profileAvatar}
            src={profileAvatarUrl}
            alt="Avatar tài khoản"
            referrerPolicy="no-referrer"
            onError={() => setHasAvatarError(true)}
          />
        ) : (
          <span className={styles.profileFallback}>{fallbackText}</span>
        )}
      </button>
    </nav>
  );
};

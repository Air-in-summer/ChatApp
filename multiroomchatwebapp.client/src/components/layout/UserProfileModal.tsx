import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { createAuthClient } from '../../api/apiClient';
import { useAuth } from '../../context/AuthContext';
import styles from './UserProfileModal.module.css';

interface UserProfileModalProps {
  onClose: () => void;
}

interface UserProfile {
  id: string;
  username: string;
  email: string;
  displayName: string;
  avatarUrl?: string | null;
}

/**
 * Modal tai khoan core: cap nhat profile, doi mat khau va dang xuat.
 */
export const UserProfileModal = ({ onClose }: UserProfileModalProps) => {
  const { accessToken, logout, refreshToken } = useAuth();
  const navigate = useNavigate();

  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [displayName, setDisplayName] = useState('');
  const [avatarUrl, setAvatarUrl] = useState('');
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [isProfileSaving, setIsProfileSaving] = useState(false);
  const [isPasswordSaving, setIsPasswordSaving] = useState(false);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const loadProfile = async () => {
      if (!accessToken) return;

      try {
        const client = createAuthClient(accessToken);
        const response = await client.get<UserProfile>('/api/v1/users/me');
        setProfile(response.data);
        setDisplayName(response.data.displayName);
        setAvatarUrl(response.data.avatarUrl ?? '');
      } catch {
        toast.error('Không tải được hồ sơ tài khoản.');
      } finally {
        setIsLoading(false);
      }
    };

    loadProfile();
  }, [accessToken]);

  const handleProfileSave = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!accessToken) return;

    setIsProfileSaving(true);
    try {
      const client = createAuthClient(accessToken);
      const response = await client.put<UserProfile>('/api/v1/users/me/profile', {
        displayName,
        avatarUrl: avatarUrl.trim() || null,
      });

      setProfile(response.data);
      await refreshToken();
      toast.success('Đã cập nhật hồ sơ.');
    } catch (error: unknown) {
      const message =
        (error as { response?: { data?: { message?: string } | string } })?.response?.data;
      toast.error(typeof message === 'string' ? message : message?.message || 'Không cập nhật được hồ sơ.');
    } finally {
      setIsProfileSaving(false);
    }
  };

  const handlePasswordChange = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!accessToken) return;

    if (newPassword !== confirmPassword) {
      toast.error('Mật khẩu xác nhận không khớp.');
      return;
    }

    setIsPasswordSaving(true);
    try {
      const client = createAuthClient(accessToken);
      await client.put('/api/v1/users/me/password', {
        currentPassword,
        newPassword,
      });

      toast.success('Đã đổi mật khẩu. Vui lòng đăng nhập lại.');
      await logout();
      navigate('/login', { replace: true });
    } catch (error: unknown) {
      const message =
        (error as { response?: { data?: { message?: string } | string } })?.response?.data;
      toast.error(typeof message === 'string' ? message : message?.message || 'Không đổi được mật khẩu.');
    } finally {
      setIsPasswordSaving(false);
    }
  };

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className={styles.overlay} onMouseDown={onClose}>
      <div className={styles.modal} onMouseDown={(event) => event.stopPropagation()}>
        <header className={styles.header}>
          <div className={styles.avatarPreview}>
            {avatarUrl ? <img src={avatarUrl} alt={displayName} /> : (displayName || '?')[0].toUpperCase()}
          </div>
          <div className={styles.identity}>
            <h2>Tài khoản</h2>
            <span>{profile ? `@${profile.username}` : 'Đang tải...'}</span>
          </div>
          <button className={styles.iconBtn} type="button" onClick={onClose} title="Đóng">
            ×
          </button>
        </header>

        {isLoading ? (
          <div className={styles.loading}>Đang tải hồ sơ...</div>
        ) : (
          <div className={styles.body}>
            <form className={styles.section} onSubmit={handleProfileSave}>
              <h3>Hồ sơ</h3>
              <label>
                Tên hiển thị
                <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} maxLength={100} />
              </label>
              <label>
                Avatar URL
                <input value={avatarUrl} onChange={(event) => setAvatarUrl(event.target.value)} placeholder="https://..." />
              </label>
              <button className={styles.primaryBtn} type="submit" disabled={isProfileSaving}>
                {isProfileSaving ? 'Đang lưu...' : 'Lưu hồ sơ'}
              </button>
            </form>

            <form className={styles.section} onSubmit={handlePasswordChange}>
              <h3>Đổi mật khẩu</h3>
              <label>
                Mật khẩu hiện tại
                <input type="password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} />
              </label>
              <label>
                Mật khẩu mới
                <input type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} />
              </label>
              <label>
                Xác nhận mật khẩu mới
                <input type="password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} />
              </label>
              <button className={styles.secondaryBtn} type="submit" disabled={isPasswordSaving}>
                {isPasswordSaving ? 'Đang đổi...' : 'Đổi mật khẩu'}
              </button>
            </form>

            <button className={styles.logoutBtn} type="button" onClick={handleLogout}>
              Đăng xuất
            </button>
          </div>
        )}
      </div>
    </div>
  );
};

import { useEffect, useState, type ChangeEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { createAuthClient } from '../../api/apiClient';
import { deleteAvatar, uploadAvatar } from '../../api/mediaApi';
import { useAuth } from '../../context/AuthContext';
import type { UserProfile } from '../../types/auth';
import { getApiErrorMessage } from '../../utils/apiError';
import styles from './UserProfileModal.module.css';

interface UserProfileModalProps {
  onClose: () => void;
}

const MAX_AVATAR_BYTES = 2 * 1024 * 1024;
const ALLOWED_AVATAR_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

/**
 * Modal tài khoản core: cập nhật profile, đổi mật khẩu và đăng xuất.
 */
export const UserProfileModal = ({ onClose }: UserProfileModalProps) => {
  const { accessToken, logout, refreshToken, updateCurrentUserProfile } = useAuth();
  const navigate = useNavigate();

  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [displayName, setDisplayName] = useState('');
  const [avatarUrl, setAvatarUrl] = useState('');
  const [selectedAvatarFile, setSelectedAvatarFile] = useState<File | null>(null);
  const [localAvatarPreviewUrl, setLocalAvatarPreviewUrl] = useState<string | null>(null);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [isProfileSaving, setIsProfileSaving] = useState(false);
  const [isAvatarUploading, setIsAvatarUploading] = useState(false);
  const [isAvatarDeleting, setIsAvatarDeleting] = useState(false);
  const [isPasswordSaving, setIsPasswordSaving] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [hasAvatarPreviewError, setHasAvatarPreviewError] = useState(false);
  const avatarFallbackText = (displayName || profile?.username || '?').trim().charAt(0).toUpperCase() || '?';
  const avatarPreviewUrl = localAvatarPreviewUrl ?? avatarUrl;

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

  useEffect(() => {
    if (!selectedAvatarFile) {
      setLocalAvatarPreviewUrl(null);
      return;
    }

    const previewUrl = URL.createObjectURL(selectedAvatarFile);
    setLocalAvatarPreviewUrl(previewUrl);

    return () => {
      URL.revokeObjectURL(previewUrl);
    };
  }, [selectedAvatarFile]);

  useEffect(() => {
    setHasAvatarPreviewError(false);
  }, [avatarPreviewUrl]);

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
      setAvatarUrl(response.data.avatarUrl ?? '');
      await refreshToken();
      toast.success('Đã cập nhật hồ sơ.');
    } catch (error: unknown) {
      toast.error(getApiErrorMessage(error, 'Không cập nhật được hồ sơ.'));
    } finally {
      setIsProfileSaving(false);
    }
  };

  const handleAvatarFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0] ?? null;
    event.target.value = '';

    if (!file) return;

    if (!ALLOWED_AVATAR_TYPES.has(file.type)) {
      toast.error('Ảnh đại diện chỉ hỗ trợ JPG, PNG hoặc WEBP.');
      return;
    }

    if (file.size > MAX_AVATAR_BYTES) {
      toast.error('Ảnh đại diện không được vượt quá 2MB.');
      return;
    }

    setSelectedAvatarFile(file);
  };

  const handleAvatarUpload = async () => {
    if (!accessToken || !selectedAvatarFile) return;

    setIsAvatarUploading(true);
    try {
      const updatedProfile = await uploadAvatar(accessToken, selectedAvatarFile);
      setProfile(updatedProfile);
      setAvatarUrl(updatedProfile.avatarUrl ?? '');
      setSelectedAvatarFile(null);
      updateCurrentUserProfile(updatedProfile);
      toast.success('Đã cập nhật ảnh đại diện.');
    } catch (error: unknown) {
      toast.error(getApiErrorMessage(error, 'Không tải được ảnh đại diện.'));
    } finally {
      setIsAvatarUploading(false);
    }
  };

  const handleAvatarDelete = async () => {
    if (!accessToken) return;

    setIsAvatarDeleting(true);
    try {
      const updatedProfile = await deleteAvatar(accessToken);
      setProfile(updatedProfile);
      setAvatarUrl(updatedProfile.avatarUrl ?? '');
      setSelectedAvatarFile(null);
      updateCurrentUserProfile(updatedProfile);
      toast.success('Đã xóa ảnh đại diện.');
    } catch (error: unknown) {
      toast.error(getApiErrorMessage(error, 'Không xóa được ảnh đại diện.'));
    } finally {
      setIsAvatarDeleting(false);
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
      toast.error(getApiErrorMessage(error, 'Không đổi được mật khẩu.'));
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
            {avatarPreviewUrl && !hasAvatarPreviewError ? (
              <img
                src={avatarPreviewUrl}
                alt={displayName}
                referrerPolicy="no-referrer"
                onError={() => setHasAvatarPreviewError(true)}
              />
            ) : (
              avatarFallbackText
            )}
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
            <section className={styles.section}>
              <h3>Ảnh đại diện</h3>
              <div className={styles.avatarControls}>
                <label className={styles.filePickerBtn}>
                  Chọn ảnh
                  <input
                    type="file"
                    accept=".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp"
                    onChange={handleAvatarFileChange}
                  />
                </label>
                <button
                  className={styles.primaryBtn}
                  type="button"
                  onClick={handleAvatarUpload}
                  disabled={!selectedAvatarFile || isAvatarUploading}
                >
                  {isAvatarUploading ? 'Đang tải...' : 'Tải lên'}
                </button>
                {selectedAvatarFile && (
                  <button
                    className={styles.secondaryBtn}
                    type="button"
                    onClick={() => setSelectedAvatarFile(null)}
                    disabled={isAvatarUploading}
                  >
                    Hủy chọn
                  </button>
                )}
                <button
                  className={styles.dangerBtn}
                  type="button"
                  onClick={handleAvatarDelete}
                  disabled={!avatarUrl || isAvatarDeleting}
                >
                  {isAvatarDeleting ? 'Đang xóa...' : 'Xóa ảnh'}
                </button>
              </div>
            </section>

            <form className={styles.section} onSubmit={handleProfileSave}>
              <h3>Hồ sơ</h3>
              <label>
                Tên hiển thị
                <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} maxLength={100} />
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

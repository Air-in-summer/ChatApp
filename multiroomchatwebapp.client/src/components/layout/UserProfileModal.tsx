import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { apiClient } from '../../api/apiClient';
import { deleteAvatar, uploadAvatar } from '../../api/mediaApi';
import { useAuth } from '../../context/AuthContext';
import type { UserProfile } from '../../types/auth';
import { getApiErrorMessage } from '../../utils/apiError';
import { getSafeResourceUrl } from '../../utils/safeUrl';
import { Button } from '../ui/Button/Button';
import { ConfirmDialog } from '../ui/ConfirmDialog/ConfirmDialog';
import { Modal } from '../ui/Modal/Modal';
import styles from './UserProfileModal.module.css';

interface UserProfileModalProps {
  onClose: () => void;
}

const MAX_AVATAR_BYTES = 2 * 1024 * 1024;
const ALLOWED_AVATAR_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

/**
 * Modal tài khoản gồm hồ sơ, ảnh đại diện, bảo mật và đăng xuất.
 */
export const UserProfileModal = ({ onClose }: UserProfileModalProps) => {
  const { isAuthenticated, logout, updateCurrentUserProfile } = useAuth();
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
  const [isPasswordModalOpen, setIsPasswordModalOpen] = useState(false);
  const [isLogoutConfirmOpen, setIsLogoutConfirmOpen] = useState(false);
  const [isLoggingOut, setIsLoggingOut] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [hasAvatarPreviewError, setHasAvatarPreviewError] = useState(false);

  const avatarFallbackText = (displayName || profile?.username || '?').trim().charAt(0).toUpperCase() || '?';
  const avatarPreviewUrl = localAvatarPreviewUrl ?? getSafeResourceUrl(avatarUrl);
  const isAvatarBusy = isAvatarUploading || isAvatarDeleting;
  const isMainActionBusy = isProfileSaving || isAvatarBusy;

  useEffect(() => {
    const loadProfile = async () => {
      if (!isAuthenticated) {
        setIsLoading(false);
        return;
      }

      try {
        const response = await apiClient.get<UserProfile>('/api/v1/users/me');
        setProfile(response.data);
        setDisplayName(response.data.displayName);
        setAvatarUrl(response.data.avatarUrl ?? '');
      } catch {
        toast.error('Không tải được hồ sơ tài khoản.');
      } finally {
        setIsLoading(false);
      }
    };

    void loadProfile();
  }, [isAuthenticated]);

  useEffect(() => {
    if (!selectedAvatarFile) {
      setLocalAvatarPreviewUrl(null);
      return undefined;
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

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    return () => {
      document.body.style.overflow = previousOverflow;
    };
  }, []);

  const clearPasswordFields = () => {
    setCurrentPassword('');
    setNewPassword('');
    setConfirmPassword('');
  };

  useEffect(() => {
    if (isPasswordModalOpen || isLogoutConfirmOpen || isMainActionBusy) return undefined;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isLogoutConfirmOpen, isMainActionBusy, isPasswordModalOpen, onClose]);

  const handleCloseMainModal = () => {
    if (isMainActionBusy || isPasswordModalOpen || isLogoutConfirmOpen) return;
    onClose();
  };

  const handleProfileSave = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!isAuthenticated) return;

    setIsProfileSaving(true);
    try {
      const response = await apiClient.put<UserProfile>('/api/v1/users/me/profile', {
        displayName,
        avatarUrl: avatarUrl.trim() || null,
      });

      setProfile(response.data);
      setAvatarUrl(response.data.avatarUrl ?? '');
      updateCurrentUserProfile(response.data);
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
      toast.error('Ảnh đại diện chỉ hỗ trợ JPG, PNG hoặc WebP.');
      return;
    }

    if (file.size > MAX_AVATAR_BYTES) {
      toast.error('Ảnh đại diện không được vượt quá 2MB.');
      return;
    }

    setSelectedAvatarFile(file);
  };

  const handleAvatarUpload = async () => {
    if (!isAuthenticated || !selectedAvatarFile || isAvatarBusy) return;

    setIsAvatarUploading(true);
    try {
      const updatedProfile = await uploadAvatar(selectedAvatarFile);
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
    if (!isAuthenticated || isAvatarBusy) return;

    setIsAvatarDeleting(true);
    try {
      const updatedProfile = await deleteAvatar();
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

  const handleOpenPasswordModal = () => {
    clearPasswordFields();
    setIsPasswordModalOpen(true);
  };

  const handleClosePasswordModal = () => {
    if (isPasswordSaving) return;
    setIsPasswordModalOpen(false);
    clearPasswordFields();
  };

  const handlePasswordChange = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!isAuthenticated || isPasswordSaving) return;

    if (newPassword !== confirmPassword) {
      toast.error('Mật khẩu xác nhận không khớp.');
      return;
    }

    setIsPasswordSaving(true);
    try {
      await apiClient.put('/api/v1/users/me/password', {
        currentPassword,
        newPassword,
      });

      toast.success('Đã đổi mật khẩu. Vui lòng đăng nhập lại.');
      clearPasswordFields();
      await logout();
      navigate('/login', { replace: true });
    } catch (error: unknown) {
      toast.error(getApiErrorMessage(error, 'Không đổi được mật khẩu.'));
    } finally {
      setIsPasswordSaving(false);
    }
  };

  const handleLogout = async () => {
    if (isLoggingOut) return;

    setIsLoggingOut(true);
    try {
      await logout();
      navigate('/login', { replace: true });
    } catch {
      toast.error('Không thể đăng xuất. Vui lòng thử lại.');
    } finally {
      setIsLoggingOut(false);
    }
  };

  return (
    <>
      <div
        className={styles.overlay}
        onMouseDown={(event) => {
          if (event.target === event.currentTarget) {
            handleCloseMainModal();
          }
        }}
      >
        <div
          className={styles.modal}
          role="dialog"
          aria-modal="true"
          aria-labelledby="user-profile-title"
          onMouseDown={(event) => event.stopPropagation()}
        >
          <aside className={styles.sidebar}>
            <div className={styles.sidebarIdentity}>
              <div className={styles.sidebarAvatar}>
                {avatarPreviewUrl && !hasAvatarPreviewError ? (
                  <img
                    src={avatarPreviewUrl}
                    alt=""
                    referrerPolicy="no-referrer"
                    onError={() => setHasAvatarPreviewError(true)}
                  />
                ) : (
                  avatarFallbackText
                )}
              </div>
              <div className={styles.sidebarIdentityText}>
                <strong>{displayName || profile?.username || 'Tài khoản'}</strong>
                <span>{profile ? `@${profile.username}` : 'Đang tải...'}</span>
              </div>
            </div>

            <button className={`${styles.sidebarItem} ${styles.sidebarItemActive}`} type="button">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                <path d="M20 21a8 8 0 0 0-16 0" />
                <circle cx="12" cy="7" r="4" />
              </svg>
              <span>Tài khoản</span>
            </button>

            <div className={styles.sidebarSpacer} />

            <button
              className={`${styles.sidebarItem} ${styles.logoutItem}`}
              type="button"
              disabled={isLoggingOut}
              onClick={() => setIsLogoutConfirmOpen(true)}
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
                <path d="m16 17 5-5-5-5" />
                <path d="M21 12H9" />
              </svg>
              <span>Đăng xuất</span>
            </button>
          </aside>

          <section className={styles.mainPanel}>
            <header className={styles.topbar}>
              <h1 id="user-profile-title">Tài khoản</h1>
              <button
                className={styles.closeButton}
                type="button"
                aria-label="Đóng"
                title="Đóng"
                disabled={isMainActionBusy}
                onClick={handleCloseMainModal}
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
                  <path d="M18 6 6 18" />
                  <path d="m6 6 12 12" />
                </svg>
              </button>
            </header>

            {isLoading ? (
              <div className={styles.loading}>Đang tải hồ sơ...</div>
            ) : (
              <div className={styles.content}>
                <section className={styles.contentSection}>
                  <div className={styles.sectionHeading}>
                    <h2>Thông tin cá nhân</h2>
                  </div>

                  <div className={styles.avatarCard}>
                    <label className={styles.avatarEditor}>
                      <span className={styles.largeAvatar}>
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
                      </span>
                      <span className={styles.avatarEditOverlay}>Chỉnh sửa ảnh</span>
                      <input
                        type="file"
                        accept=".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp"
                        disabled={isAvatarBusy}
                        onChange={handleAvatarFileChange}
                      />
                    </label>

                    <div className={styles.avatarDetails}>
                      <strong>Ảnh đại diện</strong>
                      <span>Nhấn vào ảnh để chọn JPG, PNG hoặc WebP. Tối đa 2MB.</span>
                      {selectedAvatarFile && (
                        <span className={styles.selectedFileName}>{selectedAvatarFile.name}</span>
                      )}
                    </div>

                    <div className={styles.avatarActions}>
                      {selectedAvatarFile && (
                        <>
                          <button
                            className={styles.primaryAction}
                            type="button"
                            onClick={handleAvatarUpload}
                            disabled={isAvatarBusy}
                          >
                            {isAvatarUploading ? 'Đang tải...' : 'Tải ảnh lên'}
                          </button>
                          <button
                            className={styles.secondaryAction}
                            type="button"
                            onClick={() => setSelectedAvatarFile(null)}
                            disabled={isAvatarBusy}
                          >
                            Hủy chọn
                          </button>
                        </>
                      )}
                      <button
                        className={styles.dangerAction}
                        type="button"
                        onClick={handleAvatarDelete}
                        disabled={!avatarUrl || isAvatarBusy}
                      >
                        {isAvatarDeleting ? 'Đang xóa...' : 'Xóa ảnh'}
                      </button>
                    </div>
                  </div>

                  <form className={styles.profileForm} onSubmit={handleProfileSave}>
                    <div className={styles.readonlyField}>
                      <span>Tên đăng nhập</span>
                      <strong>{profile ? `@${profile.username}` : 'Chưa tải được'}</strong>
                    </div>

                    <label className={styles.formField} htmlFor="profile-display-name">
                      <span>Tên hiển thị</span>
                      <input
                        id="profile-display-name"
                        value={displayName}
                        maxLength={100}
                        disabled={isProfileSaving}
                        onChange={(event) => setDisplayName(event.target.value)}
                      />
                    </label>

                    <button className={styles.primaryAction} type="submit" disabled={isProfileSaving}>
                      {isProfileSaving ? 'Đang lưu...' : 'Lưu thay đổi'}
                    </button>
                  </form>
                </section>

                <section className={`${styles.contentSection} ${styles.securitySection}`}>
                  <div className={styles.sectionHeading}>
                    <h2>Mật khẩu & Bảo mật</h2>
                  </div>

                  <div className={styles.securityRow}>
                    <div>
                      <strong>Mật khẩu</strong>
                      <span>••••••••</span>
                    </div>
                    <button className={styles.secondaryAction} type="button" onClick={handleOpenPasswordModal}>
                      Đổi mật khẩu
                    </button>
                  </div>
                </section>
              </div>
            )}
          </section>
        </div>
      </div>

      <Modal
        open={isPasswordModalOpen}
        title="Đổi mật khẩu"
        size="sm"
        closeOnOverlayClick={!isPasswordSaving}
        closeOnEscape={!isPasswordSaving}
        closeDisabled={isPasswordSaving}
        onClose={handleClosePasswordModal}
        footer={(
          <>
            <Button
              className={styles.modalCancelButton}
              variant="secondary"
              size="sm"
              fullWidth={false}
              disabled={isPasswordSaving}
              onClick={handleClosePasswordModal}
            >
              Hủy
            </Button>
            <Button
              type="submit"
              form="change-password-form"
              size="sm"
              fullWidth={false}
              loading={isPasswordSaving}
            >
              Đổi mật khẩu
            </Button>
          </>
        )}
      >
        <form id="change-password-form" className={styles.passwordForm} onSubmit={handlePasswordChange}>
          <label className={styles.formField} htmlFor="current-password">
            <span>Mật khẩu hiện tại</span>
            <input
              id="current-password"
              type="password"
              autoComplete="current-password"
              value={currentPassword}
              disabled={isPasswordSaving}
              onChange={(event) => setCurrentPassword(event.target.value)}
              autoFocus
              required
            />
          </label>
          <label className={styles.formField} htmlFor="new-password">
            <span>Mật khẩu mới</span>
            <input
              id="new-password"
              type="password"
              autoComplete="new-password"
              value={newPassword}
              disabled={isPasswordSaving}
              onChange={(event) => setNewPassword(event.target.value)}
              required
            />
          </label>
          <label className={styles.formField} htmlFor="confirm-password">
            <span>Xác nhận mật khẩu mới</span>
            <input
              id="confirm-password"
              type="password"
              autoComplete="new-password"
              value={confirmPassword}
              disabled={isPasswordSaving}
              onChange={(event) => setConfirmPassword(event.target.value)}
              required
            />
          </label>
        </form>
      </Modal>

      <ConfirmDialog
        open={isLogoutConfirmOpen}
        title="Đăng xuất"
        message="Bạn có muốn đăng xuất khỏi tài khoản này?"
        confirmLabel="Đăng xuất"
        cancelLabel="Hủy"
        variant="danger"
        loading={isLoggingOut}
        onCancel={() => {
          if (isLoggingOut) return;
          setIsLogoutConfirmOpen(false);
        }}
        onConfirm={handleLogout}
      />
    </>
  );
};

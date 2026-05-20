import { useState, useEffect, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { joinGroupByInviteCode } from '../../api/groupApi';
import styles from './JoinGroupPage.module.css';

/**
 * Trang trung gian xử lý lời mời tham gia Server.
 * Được mount khi user truy cập route `/join/:inviteCode`.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Lấy `inviteCode` từ URL params.
 * 2. Gọi API `joinGroupByInviteCode` ngay khi mount.
 * 3. Thành công → Redirect về trang chủ kèm GroupDto qua navigation state (Bước 16.4).
 * 4. Thất bại → Hiển thị giao diện lỗi với nút quay lại trang chủ.
 *
 * Lưu ý:
 * - Dùng `useRef` flag để chống double-call khi React 18 Strict Mode mount 2 lần.
 * - Trang này luôn nằm trong ProtectedRoute → user đã đăng nhập mới vào được.
 */
export const JoinGroupPage = () => {
  const { inviteCode } = useParams<{ inviteCode: string }>();
  const navigate = useNavigate();
  const { accessToken } = useAuth();

  // Trạng thái hiển thị: loading (mặc định) hoặc error (khi API thất bại)
  const [error, setError] = useState<string | null>(null);

  // Flag chống gọi API 2 lần (React 18 Strict Mode double-mount)
  const hasCalledRef = useRef(false);

  // [Luồng xử lý: Gọi API tham gia Server]
  // Bước 16.3: Tự động gọi API ngay khi component mount
  useEffect(() => {
    // Guard: Chỉ gọi API 1 lần duy nhất
    if (hasCalledRef.current) return;
    hasCalledRef.current = true;

    // Guard: Thiếu dữ liệu cần thiết
    if (!inviteCode || !accessToken) {
      setError('Mã mời không hợp lệ.');
      return;
    }

    const joinGroup = async () => {
      try {
        const groupDto = await joinGroupByInviteCode(accessToken, inviteCode);

        // Bước 16.4a: Redirect về trang chủ, truyền GroupDto qua navigation state
        navigate('/', { replace: true, state: { joinedGroup: groupDto } });
      } catch (err: unknown) {
        // Phân loại lỗi để hiển thị message phù hợp
        const axiosError = err as { response?: { status?: number; data?: { detail?: string } } };

        if (axiosError.response?.status === 404) {
          setError('Mã mời không hợp lệ hoặc Server không còn tồn tại.');
        } else {
          setError(
            axiosError.response?.data?.detail
            || 'Đã xảy ra lỗi khi tham gia Server. Vui lòng thử lại.'
          );
        }
      }
    };

    joinGroup();
  }, [inviteCode, accessToken, navigate]);

  return (
    <div className={styles.pageContainer}>
      <div className={styles.card}>
        {/* Trạng thái Lỗi */}
        {error ? (
          <>
            {/* Icon cảnh báo */}
            <svg className={styles.errorIcon} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="10" />
              <line x1="12" y1="8" x2="12" y2="12" />
              <line x1="12" y1="16" x2="12.01" y2="16" />
            </svg>

            <h2 className={styles.errorTitle}>Không thể tham gia Server</h2>
            <p className={styles.errorMessage}>{error}</p>

            <button
              className={styles.backButton}
              onClick={() => navigate('/', { replace: true })}
            >
              {/* Icon mũi tên */}
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="19" y1="12" x2="5" y2="12" />
                <polyline points="12 19 5 12 12 5" />
              </svg>
              Quay lại trang chủ
            </button>
          </>
        ) : (
          /* Trạng thái Loading (mặc định) */
          <>
            <div className={styles.spinnerGlow}>
              <div className={styles.spinner} />
            </div>
            <p className={styles.loadingText}>Đang xử lý lời mời...</p>
            <p className={styles.loadingSubtext}>Vui lòng đợi trong giây lát</p>
          </>
        )}
      </div>
    </div>
  );
};

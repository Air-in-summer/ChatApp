import { useEffect } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { GlassCard } from '../../../components/ui/GlassCard/GlassCard';
import { useAuth } from '../../../context/AuthContext';
import { sanitizeInternalReturnUrl } from '../../../utils/returnUrl';
import styles from './OAuthCallbackPage.module.css';

const OAUTH_ERROR_MESSAGES: Record<string, string> = {
  access_denied: 'Bạn đã hủy đăng nhập Google.',
  account_conflict: 'Email này đã tồn tại. Hãy đăng nhập bằng mật khẩu trước.',
  email_not_verified: 'Google chưa xác minh email của tài khoản này.',
  oauth_failed: 'Đăng nhập Google thất bại.',
  user_inactive: 'Tài khoản này đang bị khóa.',
  session_expired: 'Không thể khôi phục phiên đăng nhập.',
};

/**
 * Trang callback OAuth phía frontend.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Đọc lỗi OAuth nếu backend redirect về kèm query error.
 * 2. Nếu không có lỗi, gọi loadSession để đọc BFF session cookie.
 * 3. Redirect về returnUrl nội bộ an toàn hoặc dashboard.
 * 4. Nếu session không hợp lệ, đưa user về Login page với thông báo rõ ràng.
 */
export const OAuthCallbackPage = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { loadSession } = useAuth();

  const rawReturnUrl = searchParams.get('returnUrl');
  const returnUrl = sanitizeInternalReturnUrl(rawReturnUrl);
  const errorCode = searchParams.get('oauthError') || searchParams.get('error');

  useEffect(() => {
    let isCancelled = false;

    const finishLogin = async () => {
      if (errorCode) {
        const message = OAUTH_ERROR_MESSAGES[errorCode] || 'Đăng nhập Google thất bại.';
        toast.error(message);
        navigate(`/login?oauthError=${encodeURIComponent(errorCode)}&returnUrl=${encodeURIComponent(returnUrl)}`, {
          replace: true,
        });
        return;
      }

      const sessionUser = await loadSession();
      if (isCancelled) return;

      if (sessionUser) {
        toast.success('Đăng nhập thành công!');
        navigate(returnUrl, { replace: true });
        return;
      }

      toast.error(OAUTH_ERROR_MESSAGES.session_expired);
      navigate(`/login?oauthError=session_expired&returnUrl=${encodeURIComponent(returnUrl)}`, {
        replace: true,
      });
    };

    void finishLogin();

    return () => {
      isCancelled = true;
    };
  }, [errorCode, loadSession, navigate, returnUrl]);

  return (
    <div className={styles.pageContainer}>
      <GlassCard title="Đăng nhập Google" className={styles.callbackCard}>
        <div className={styles.statusRow}>
          <div className="spinner" />
          <span>Đang hoàn tất đăng nhập...</span>
        </div>
      </GlassCard>
    </div>
  );
};

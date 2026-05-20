import { useEffect, useRef } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { GlassCard } from '../../../components/ui/GlassCard/GlassCard';
import { useAuth } from '../../../context/AuthContext';
import { sanitizeInternalReturnUrl } from '../../../utils/returnUrl';
import styles from './OAuthCallbackPage.module.css';

const OAUTH_ERROR_MESSAGES: Record<string, string> = {
  access_denied: 'Ban da huy dang nhap Google.',
  account_conflict: 'Email nay da ton tai. Hay dang nhap bang mat khau truoc.',
  email_not_verified: 'Google chua xac minh email cua tai khoan nay.',
  user_inactive: 'Tai khoan nay dang bi khoa.',
  session_expired: 'Khong the khoi phuc phien dang nhap.',
};

/**
 * Trang callback OAuth phia frontend.
 *
 * @remarks
 * Luong xu ly:
 * 1. Doc loi OAuth neu backend redirect ve kem query error.
 * 2. Neu khong co loi, goi refresh token de lay JWT noi bo vao RAM.
 * 3. Redirect ve returnUrl noi bo an toan hoac dashboard.
 * 4. Neu refresh that bai, dua user ve Login page voi thong bao ro rang.
 */
export const OAuthCallbackPage = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { refreshToken } = useAuth();
  const hasStartedRef = useRef(false);

  const rawReturnUrl = searchParams.get('returnUrl');
  const returnUrl = sanitizeInternalReturnUrl(rawReturnUrl);
  const errorCode = searchParams.get('oauthError') || searchParams.get('error');

  useEffect(() => {
    if (hasStartedRef.current) return;
    hasStartedRef.current = true;

    let isMounted = true;

    const finishLogin = async () => {
      if (errorCode) {
        const message = OAUTH_ERROR_MESSAGES[errorCode] || 'Dang nhap Google that bai.';
        toast.error(message);
        navigate(`/login?oauthError=${encodeURIComponent(errorCode)}&returnUrl=${encodeURIComponent(returnUrl)}`, {
          replace: true,
        });
        return;
      }

      const accessToken = await refreshToken();
      if (!isMounted) return;

      if (accessToken) {
        toast.success('Dang nhap thanh cong!');
        navigate(returnUrl, { replace: true });
        return;
      }

      toast.error(OAUTH_ERROR_MESSAGES.session_expired);
      navigate(`/login?oauthError=session_expired&returnUrl=${encodeURIComponent(returnUrl)}`, {
        replace: true,
      });
    };

    finishLogin();

    return () => {
      isMounted = false;
    };
  }, [errorCode, navigate, refreshToken, returnUrl]);

  return (
    <div className={styles.pageContainer}>
      <GlassCard title="Dang nhap Google" className={styles.callbackCard}>
        <div className={styles.statusRow}>
          <div className="spinner" />
          <span>Dang hoan tat dang nhap...</span>
        </div>
      </GlassCard>
    </div>
  );
};

import { useState, useEffect } from 'react';
import { Link, useNavigate, useLocation, useSearchParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { buildApiUrl } from '../../../api/apiClient';
import { useAuth } from '../../../context/AuthContext';
import { GlassCard } from '../../../components/ui/GlassCard/GlassCard';
import { InputText } from '../../../components/ui/InputText/InputText';
import { Button } from '../../../components/ui/Button/Button';
import { getApiErrorMessage } from '../../../utils/apiError';
import { sanitizeInternalReturnUrl } from '../../../utils/returnUrl';
import styles from './LoginPage.module.css';

/**
 * Trang đăng nhập.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Validate form phía Frontend.
 * 2. Gọi hàm `login` từ AuthContext → gọi API /api/auth/login.
 * 3. Backend cấp BFF session cookie và trả thông tin user/session.
 * 4. AuthContext cập nhật trạng thái đăng nhập.
 * 5. Điều hướng về Dashboard.
 *
 * Lưu ý: KHÔNG còn lưu bất kỳ token nào vào localStorage nữa.
 */
export const LoginPage = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { login, isAuthenticated } = useAuth(); // Lấy thêm isAuthenticated

  // Đọc returnUrl từ state (do ProtectedRoute truyền) HOẶC từ query string (?returnUrl=...)
  const rawReturnUrl = (location.state as { returnUrl?: string } | null)?.returnUrl || searchParams.get('returnUrl');
  const returnUrl = sanitizeInternalReturnUrl(rawReturnUrl);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [googleLoading, setGoogleLoading] = useState(false);
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({});

  const validateForm = () => {
    const newErrors: typeof errors = {};
    if (!email) newErrors.email = 'Email là bắt buộc';
    else if (!/\S+@\S+\.\S+/.test(email)) newErrors.email = 'Email không hợp lệ';
    if (!password) newErrors.password = 'Mật khẩu là bắt buộc';

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  // Tự động chuyển hướng nếu đã đăng nhập
  useEffect(() => {
    if (isAuthenticated) {
      navigate(returnUrl, { replace: true });
    }
  }, [isAuthenticated, navigate, returnUrl]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validateForm()) return;

    setLoading(true);
    try {
      // Gọi qua AuthContext → tự xử lý API + lưu RAM
      await login(email, password);
      toast.success('Đăng nhập thành công!');
      // Redirect về returnUrl (nếu có và hợp lệ), không thì về Dashboard
      // Guard: chỉ chấp nhận returnUrl bắt đầu bằng '/' để chống Open Redirect attack
      navigate(returnUrl, { replace: true });
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'Sai email hoặc mật khẩu'));
    } finally {
      setLoading(false);
    }
  };

  const handleGoogleLogin = () => {
    setGoogleLoading(true);

    const query = new URLSearchParams({ returnUrl });
    window.location.assign(
      `${buildApiUrl('/api/auth/google/login')}?${query.toString()}`
    );
  };

  return (
    <div className={styles.pageContainer}>
      <GlassCard title="Đăng Nhập" className={styles.loginCard}>
        <form onSubmit={handleSubmit} className={styles.formContainer}>
          <InputText
            label="Email"
            type="email"
            placeholder="nhap.email@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            error={errors.email}
          />

          <InputText
            label="Mật khẩu"
            type="password"
            placeholder="••••••••"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={errors.password}
          />

          <div className={styles.forgotPassword}>
            <Link to="#">Quên mật khẩu?</Link>
          </div>

          <Button type="submit" isLoading={loading}>
            Đăng Nhập
          </Button>

          <div className={styles.divider}>
            <span>hoặc</span>
          </div>

          <button
            type="button"
            className={styles.googleButton}
            onClick={handleGoogleLogin}
            disabled={loading || googleLoading}
          >
            <span className={styles.googleMark}>G</span>
            <span>{googleLoading ? 'Đang chuyển hướng...' : 'Đăng nhập bằng Google'}</span>
          </button>
        </form>

        <div className={styles.footerLink}>
          Chưa có tài khoản? <Link to="/register">Đăng ký ngay</Link>
        </div>
      </GlassCard>
    </div>
  );
};

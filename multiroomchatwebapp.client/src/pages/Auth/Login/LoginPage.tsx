import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { useAuth } from '../../../context/AuthContext';
import { GlassCard } from '../../../components/ui/GlassCard/GlassCard';
import { InputText } from '../../../components/ui/InputText/InputText';
import { Button } from '../../../components/ui/Button/Button';
import styles from './LoginPage.module.css';

/**
 * Trang đăng nhập.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Validate form phía Frontend.
 * 2. Gọi hàm `login` từ AuthContext → gọi API /api/auth/login.
 * 3. Backend trả về AccessToken trong body, RefreshToken trong HttpOnly Cookie.
 * 4. AuthContext lưu AccessToken vào RAM.
 * 5. Điều hướng về Dashboard.
 *
 * Lưu ý: KHÔNG còn lưu bất kỳ token nào vào localStorage nữa.
 */
export const LoginPage = () => {
  const navigate = useNavigate();
  const { login } = useAuth(); // Lấy hàm login từ AuthContext
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({});

  const validateForm = () => {
    const newErrors: typeof errors = {};
    if (!email) newErrors.email = 'Email là bắt buộc';
    else if (!/\S+@\S+\.\S+/.test(email)) newErrors.email = 'Email không hợp lệ';
    if (!password) newErrors.password = 'Mật khẩu là bắt buộc';

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validateForm()) return;

    setLoading(true);
    try {
      // Gọi qua AuthContext → tự xử lý API + lưu RAM
      await login(email, password);
      toast.success('Đăng nhập thành công!');
      navigate('/'); // Dashboard
    } catch (err: unknown) {
      // Axios bọc lỗi server vào err.response.data
      const message =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        || 'Sai email hoặc mật khẩu';
      toast.error(message);
    } finally {
      setLoading(false);
    }
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
        </form>

        <div className={styles.footerLink}>
          Chưa có tài khoản? <Link to="/register">Đăng ký ngay</Link>
        </div>
      </GlassCard>
    </div>
  );
};

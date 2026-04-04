import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { GlassCard } from '../../../components/ui/GlassCard/GlassCard';
import { InputText } from '../../../components/ui/InputText/InputText';
import { Button } from '../../../components/ui/Button/Button';
import styles from './LoginPage.module.css';

export const LoginPage = () => {
  const navigate = useNavigate();
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
      // Tương lai sẽ gọi API fetch('/api/auth/login') ở đây
      const response = await fetch('http://localhost:5202/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password })
      });
      
      const data = await response.json();
      
      if (!response.ok) {
        throw new Error(data.error || data.message || 'Server error');
      }

      // Xử lý thành công
      localStorage.setItem('accessToken', data.accessToken);
      localStorage.setItem('refreshToken', data.refreshToken); // Tạm thời để localStorage trước khi có cookie
      
      toast.success('Đăng nhập thành công!');
      // Navigate to home or dashboard
      navigate('/');
    } catch (err: any) {
      toast.error(err.message || 'Lỗi đăng nhập');
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

import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { GlassCard } from '../../../components/ui/GlassCard/GlassCard';
import { InputText } from '../../../components/ui/InputText/InputText';
import { Button } from '../../../components/ui/Button/Button';
import styles from './RegisterPage.module.css';

export const RegisterPage = () => {
  const navigate = useNavigate();
  const [formData, setFormData] = useState({
    username: '',
    displayName: '',
    email: '',
    password: ''
  });
  const [loading, setLoading] = useState(false);
  const [errors, setErrors] = useState<Partial<typeof formData>>({});

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setFormData({ ...formData, [e.target.name]: e.target.value });
  };

  const validateForm = () => {
    const newErrors: typeof errors = {};
    if (!formData.username) newErrors.username = 'Username là bắt buộc';
    else if (!/^[a-zA-Z0-9._]+$/.test(formData.username)) 
      newErrors.username = 'Username chỉ được dùng chữ, số, dấu chấm/gạch dưới';
    
    if (!formData.displayName) newErrors.displayName = 'Tên hiển thị là bắt buộc';
    
    if (!formData.email) newErrors.email = 'Email là bắt buộc';
    else if (!/\S+@\S+\.\S+/.test(formData.email)) newErrors.email = 'Email không hợp lệ';
    
    if (!formData.password) newErrors.password = 'Mật khẩu là bắt buộc';
    else if (formData.password.length < 8) newErrors.password = 'Tối thiểu 8 ký tự';

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validateForm()) return;

    setLoading(true);
    try {
      const response = await fetch('http://localhost:5202/api/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(formData)
      });
      
      const data = await response.json();
      
      if (!response.ok) {
        throw new Error(data.error || data.message || 'Server error');
      }

      toast.success('Đăng ký thành công! Hãy đăng nhập.');
      navigate('/login');
    } catch (err: any) {
      toast.error(err.message || 'Lỗi đăng ký');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className={styles.pageContainer}>
      <GlassCard title="Tạo Tài Khoản" className={styles.registerCard}>
        <form onSubmit={handleSubmit} className={styles.formContainer}>
          <div className={styles.row}>
            <InputText
              label="Username"
              name="username"
              placeholder="user_test_01"
              value={formData.username}
              onChange={handleChange}
              error={errors.username}
            />
            <InputText
              label="Tên hiển thị"
              name="displayName"
              placeholder="Thái Nguyễn 🇻🇳"
              value={formData.displayName}
              onChange={handleChange}
              error={errors.displayName}
            />
          </div>

          <InputText
            label="Email"
            name="email"
            type="email"
            placeholder="nhap.email@example.com"
            value={formData.email}
            onChange={handleChange}
            error={errors.email}
          />
          
          <InputText
            label="Mật khẩu"
            name="password"
            type="password"
            placeholder="••••••••"
            value={formData.password}
            onChange={handleChange}
            error={errors.password}
          />

          <Button type="submit" isLoading={loading} className={styles.submitBtn}>
            Đăng Ký
          </Button>
        </form>

        <div className={styles.footerLink}>
          Đã có tài khoản? <Link to="/login">Đăng nhập</Link>
        </div>
      </GlassCard>
    </div>
  );
};

import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import type { ReactNode } from 'react';

interface ProtectedRouteProps {
  children: ReactNode;
}

/**
 * Bảo vệ Route khỏi người dùng chưa xác thực.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Nếu isLoading (đang kiểm tra Cookie) → Hiển thị Spinner toàn màn hình.
 *    (Quan trọng: tránh redirect sai khi chưa kịp khôi phục phiên làm việc)
 * 2. Nếu chưa đăng nhập → Chuyển về /login.
 * 3. Nếu đã đăng nhập → Cho phép truy cập.
 */
export const ProtectedRoute = ({ children }: ProtectedRouteProps) => {
  const { isAuthenticated, isLoading } = useAuth();
  const location = useLocation();

  // Đang kiểm tra phiên làm việc sau F5.
  if (isLoading) {
    return (
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100vh',
        background: 'var(--bg-dark-primary)',
      }}>
        <div className="spinner" />
      </div>
    );
  }

  // Chưa đăng nhập → redirect về /login, truyền kèm returnUrl để quay lại đúng trang sau login
  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ returnUrl: `${location.pathname}${location.search}${location.hash}` }} />;
  }

  return <>{children}</>;
};

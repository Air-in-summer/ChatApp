import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { Toaster } from 'react-hot-toast';

import { AuthProvider } from './context/AuthContext';
import { ProtectedRoute } from './components/auth/ProtectedRoute';
import { MainLayout } from './components/layout/MainLayout';
import { LoginPage } from './pages/Auth/Login/LoginPage';
import { OAuthCallbackPage } from './pages/Auth/OAuthCallback/OAuthCallbackPage';
import { RegisterPage } from './pages/Auth/Register/RegisterPage';
import { JoinGroupPage } from './pages/JoinGroup/JoinGroupPage';

import './App.css';

/**
 * Root component - Bọc toàn bộ app trong AuthProvider.
 *
 * @remarks
 * Cấu trúc Route:
 * - /login, /register : Public (ai cũng vào được)
 * - / (và các sub-route) : Protected (phải đăng nhập, bọc bởi ProtectedRoute)
 *
 * Luồng khi F5 trang ở Dashboard:
 * 1. AuthProvider tự động gọi /api/auth/session.
 * 2. ProtectedRoute thấy isLoading=true → hiện Spinner, KHÔNG redirect.
 * 3. Khi refresh xong → isLoading=false, isAuthenticated=true → render MainLayout bình thường.
 */
function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        {/* Toast Alert toàn cục - kiểu kính mờ khớp design system */}
        <Toaster
          position="top-right"
          toastOptions={{
            style: {
              background: 'rgba(20, 30, 50, 0.92)',
              color: '#f8fafc',
              backdropFilter: 'blur(12px)',
              border: '1px solid rgba(255, 255, 255, 0.1)',
              borderRadius: '12px',
              fontSize: '0.875rem',
            }
          }}
        />

        <Routes>
          {/* Public routes - không cần đăng nhập */}
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/oauth/callback" element={<OAuthCallbackPage />} />

          {/* Protected routes - phải đăng nhập */}
          <Route
            path="/join/:inviteCode"
            element={
              <ProtectedRoute>
                <JoinGroupPage />
              </ProtectedRoute>
            }
          />
          <Route
            path="/"
            element={
              <ProtectedRoute>
                <MainLayout />
              </ProtectedRoute>
            }
          />

          {/* Fallback - chuyển về login nếu route không khớp */}
          <Route path="*" element={<Navigate to="/login" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;

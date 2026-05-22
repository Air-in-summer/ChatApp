import { createContext, useContext, useState, useEffect, useCallback, useMemo, useRef, type ReactNode } from 'react';
import { apiClient, configureAuthInterceptors } from '../api/apiClient';
import type { AuthUser, AuthClientResponse } from '../types/auth';

/**
 * Định nghĩa contract của AuthContext.
 * accessToken chỉ tồn tại trong RAM, không bao giờ được persist.
 */
interface AuthContextType {
  /** AccessToken ngắn hạn (15 phút). null = chưa đăng nhập. */
  accessToken: string | null;
  /** Thông tin user đang đăng nhập. null = chưa đăng nhập. */
  user: AuthUser | null;
  /**
   * isLoading = true khi đang kiểm tra phiên đăng nhập (lần đầu app load).
   * Dùng để tránh redirect về /login khi chưa kịp refresh token.
   */
  isLoading: boolean;
  /** true nếu có accessToken hợp lệ trong bộ nhớ. */
  isAuthenticated: boolean;
  /** Đăng nhập và lưu token vào RAM. */
  login: (email: string, password: string) => Promise<void>;
  /** Đăng xuất: gọi API xóa Cookie + xóa RAM. */
  logout: () => Promise<void>;
  /** Dùng nội bộ (AuthProvider) và Axios interceptor để lấy token mới. */
  refreshToken: () => Promise<string | null>;
}

const AuthContext = createContext<AuthContextType | null>(null);

/**
 * AuthProvider - Bọc ngoài toàn bộ App để cung cấp trạng thái xác thực.
 *
 * @remarks
 * Luồng khởi tạo (khi F5 trang):
 * 1. isLoading = true → giữ nguyên, ProtectedRoute hiện Spinner.
 * 2. Tự động gọi POST /api/auth/refresh để dùng HttpOnly Cookie đổi lấy AccessToken mới.
 * 3. Nếu thành công → lưu token + user vào RAM, isLoading = false.
 * 4. Nếu thất bại (Cookie hết hạn / chưa đăng nhập) → isLoading = false, token = null.
 * 5. ProtectedRoute kiểm tra isAuthenticated và điều hướng phù hợp.
 */
// Lưu promise để chống gọi API nhiều lần đồng thời (Race Condition)
// Đặc biệt khi React 18 Strict Mode mount component 2 lần.
let refreshPromise: Promise<string | null> | null = null;

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const accessTokenRef = useRef<string | null>(null);

  useEffect(() => {
    accessTokenRef.current = accessToken;
  }, [accessToken]);

  const refreshToken = useCallback(async (): Promise<string | null> => {
    // Nếu có một tiến trình refresh đang chạy, return luôn tiến trình đó
    if (refreshPromise) return refreshPromise;

    refreshPromise = (async () => {
      try {
        const response = await apiClient.post<AuthClientResponse>('/api/auth/refresh');
        const data = response.data;

        setAccessToken(data.accessToken);
        setUser({
          userId: data.userId,
          username: data.username,
          displayName: data.displayName,
          avatarUrl: data.avatarUrl,
        });

        return data.accessToken;
      } catch {
        setAccessToken(null);
        setUser(null);
        return null;
      } finally {
        // Hoàn thành xong thì dọn dẹp lock
        refreshPromise = null;
      }
    })();

    return refreshPromise;
  }, []);


  /**
   * Lần F5 đầu tiên, thử khôi phục phiên từ Cookie.
   * Chỉ chạy một lần duy nhất khi mount.
   */
  useEffect(() => {
    const initializeAuth = async () => {
      await refreshToken();
      setIsLoading(false); // Dù thành công hay thất bại, đều tắt loading
    };

    initializeAuth();
  }, [refreshToken]);

  /**
   * Đăng ký Axios interceptor bridge để request bị 401 có thể refresh token và retry một lần.
   */
  useEffect(() => {
    configureAuthInterceptors({
      getAccessToken: () => accessTokenRef.current,
      refreshAccessToken: refreshToken,
    });
  }, [refreshToken]);

  /**
   * Đăng nhập: Gọi API, nhận AccessToken về RAM.
   * Backend sẽ tự Set-Cookie HttpOnly cho RefreshToken.
   *
   * @param email - Email người dùng.
   * @param password - Mật khẩu.
   * @throws Error nếu đăng nhập thất bại (sai mật khẩu, tài khoản bị khóa...).
   */
  const login = useCallback(async (email: string, password: string): Promise<void> => {
    const response = await apiClient.post<AuthClientResponse>('/api/auth/login', {
      email,
      password,
    });
    const data = response.data;

    setAccessToken(data.accessToken);
    setUser({
      userId: data.userId,
      username: data.username,
      displayName: data.displayName,
      avatarUrl: data.avatarUrl,
    });
  }, []);

  /**
   * Đăng xuất: Gọi API thu hồi RefreshToken trong DB + xóa Cookie.
   * Sau đó xóa trạng thái trong RAM.
   */
  const logout = useCallback(async (): Promise<void> => {
    try {
      await apiClient.post('/api/auth/logout');
    } catch {
      // Kể cả khi API lỗi, vẫn xóa RAM để logout phía client
    } finally {
      setAccessToken(null);
      setUser(null);
    }
  }, []);

  // [FIX] Memoize context value để tránh tạo object mới mỗi render
  // → ngăn toàn bộ consumer re-render khi AuthProvider re-render nhưng data không đổi
  const value: AuthContextType = useMemo(() => ({
    accessToken,
    user,
    isLoading,
    isAuthenticated: !!accessToken,
    login,
    logout,
    refreshToken,
  }), [accessToken, user, isLoading, login, logout, refreshToken]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};

/**
 * Hook tiện ích để truy cập AuthContext từ bất kỳ component nào.
 * Phải được dùng bên trong AuthProvider.
 *
 * @throws Error nếu được gọi ngoài AuthProvider.
 */
export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth phải được dùng bên trong <AuthProvider>');
  }
  return context;
};

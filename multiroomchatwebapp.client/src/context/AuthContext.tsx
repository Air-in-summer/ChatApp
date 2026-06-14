import { createContext, useContext, useState, useEffect, useCallback, useMemo, type ReactNode } from 'react';
import {
  apiClient,
  configureBffUnauthorizedHandler,
  resetBffCsrfToken,
} from '../api/apiClient';
import type { AuthUser, AuthClientResponse, AuthSessionResponse, UserProfile } from '../types/auth';

/**
 * Định nghĩa contract của AuthContext.
 * Contract xác thực frontend dùng BFF session.
 */
interface AuthContextType {
  /** Thông tin user đang đăng nhập. null = chưa đăng nhập. */
  user: AuthUser | null;
  /**
   * isLoading = true khi đang kiểm tra phiên đăng nhập (lần đầu app load).
   * Dùng để tránh redirect về /login khi chưa khôi phục xong session.
   */
  isLoading: boolean;
  /** true nếu có user session hợp lệ trong state. */
  isAuthenticated: boolean;
  /** Đăng nhập và lưu thông tin user của BFF session vào RAM. */
  login: (email: string, password: string) => Promise<void>;
  /** Đăng ký tài khoản mới và nhận session đăng nhập nếu backend cấp thành công. */
  register: (request: RegisterRequest) => Promise<void>;
  /** Đăng xuất: gọi API xóa Cookie + xóa RAM. */
  logout: () => Promise<void>;
  /** Tải lại trạng thái đăng nhập bằng BFF session cookie. */
  loadSession: () => Promise<AuthUser | null>;
  updateCurrentUserProfile: (profile: UserProfile) => void;
}

interface RegisterRequest {
  username: string;
  displayName: string;
  email: string;
  password: string;
}

const AuthContext = createContext<AuthContextType | null>(null);
const AUTH_BROADCAST_CHANNEL = 'chatapp-auth';
const AUTH_LOGOUT_EVENT = 'logout';

/**
 * AuthProvider - Bọc ngoài toàn bộ App để cung cấp trạng thái xác thực.
 *
 * @remarks
 * Luồng khởi tạo (khi F5 trang):
 * 1. isLoading = true → giữ nguyên, ProtectedRoute hiện Spinner.
 * 2. Tự động gọi GET /api/auth/session để khôi phục BFF session.
 * 3. Nếu thành công → lưu user vào RAM, isLoading = false.
 * 4. Nếu thất bại (session hết hạn / chưa đăng nhập) → isLoading = false, user = null.
 * 5. ProtectedRoute kiểm tra isAuthenticated và điều hướng phù hợp.
 */
export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const broadcastLogout = useCallback((): void => {
    if (typeof BroadcastChannel === 'undefined') return;

    const channel = new BroadcastChannel(AUTH_BROADCAST_CHANNEL);
    channel.postMessage({ type: AUTH_LOGOUT_EVENT });
    channel.close();
  }, []);

  const clearAuthState = useCallback((): void => {
    resetBffCsrfToken();
    setUser(null);
  }, []);

  const mapSessionToUser = useCallback((data: AuthSessionResponse): AuthUser => ({
    userId: data.userId,
    username: data.username,
    displayName: data.displayName,
    avatarUrl: data.avatarUrl,
  }), []);

  const loadSession = useCallback(async (): Promise<AuthUser | null> => {
    try {
      const response = await apiClient.get<AuthSessionResponse>('/api/auth/session');
      const sessionUser = mapSessionToUser(response.data);

      setUser(sessionUser);
      resetBffCsrfToken();

      return sessionUser;
    } catch {
      clearAuthState();
      return null;
    }
  }, [clearAuthState, mapSessionToUser]);

  const applyAuthResponse = useCallback((data: AuthClientResponse): void => {
    setUser({
      userId: data.userId,
      username: data.username,
      displayName: data.displayName,
      avatarUrl: data.avatarUrl,
    });
    resetBffCsrfToken();
  }, []);


  /**
   * Lần F5 đầu tiên, thử khôi phục phiên từ BFF session cookie.
   * Chỉ chạy một lần duy nhất khi mount.
   */
  useEffect(() => {
    const initializeAuth = async () => {
      await loadSession();
      setIsLoading(false);
    };

    initializeAuth();
  }, [loadSession]);

  useEffect(() => {
    configureBffUnauthorizedHandler(() => {
      clearAuthState();
    });

    return () => configureBffUnauthorizedHandler(null);
  }, [clearAuthState]);

  useEffect(() => {
    if (typeof BroadcastChannel === 'undefined') return;

    const channel = new BroadcastChannel(AUTH_BROADCAST_CHANNEL);
    channel.onmessage = event => {
      if (event.data?.type === AUTH_LOGOUT_EVENT) {
        clearAuthState();
      }
    };

    return () => channel.close();
  }, [clearAuthState]);

  /**
   * Đăng nhập: gọi API để backend tạo BFF session.
   * Frontend chỉ giữ thông tin user, không nhận hoặc lưu application token.
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

    applyAuthResponse(response.data);
  }, [applyAuthResponse]);

  const register = useCallback(async (request: RegisterRequest): Promise<void> => {
    const response = await apiClient.post<AuthClientResponse>('/api/auth/register', request);

    applyAuthResponse(response.data);
  }, [applyAuthResponse]);

  /**
   * Đăng xuất: gọi API thu hồi BFF session và xóa cookie phiên.
   * Sau đó xóa trạng thái trong RAM.
   */
  const updateCurrentUserProfile = useCallback((profile: UserProfile): void => {
    setUser(currentUser => {
      if (!currentUser || currentUser.userId !== profile.id) {
        return currentUser;
      }

      return {
        ...currentUser,
        username: profile.username,
        displayName: profile.displayName,
        avatarUrl: profile.avatarUrl,
      };
    });
  }, []);

  const logout = useCallback(async (): Promise<void> => {
    try {
      await apiClient.post('/api/auth/logout', undefined, {
        _skipBffUnauthorized: true,
      });
    } catch {
      // Kể cả khi API lỗi, vẫn xóa RAM để logout phía client
    } finally {
      clearAuthState();
      broadcastLogout();
    }
  }, [broadcastLogout, clearAuthState]);

  // [FIX] Memoize context value để tránh tạo object mới mỗi render
  // → ngăn toàn bộ consumer re-render khi AuthProvider re-render nhưng data không đổi
  const value: AuthContextType = useMemo(() => ({
    user,
    isLoading,
    isAuthenticated: !!user,
    login,
    register,
    logout,
    loadSession,
    updateCurrentUserProfile,
  }), [user, isLoading, login, register, logout, loadSession, updateCurrentUserProfile]);

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

import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios';

export const API_BASE_URL = 'https://localhost:7222';

type AuthRetryConfig = InternalAxiosRequestConfig & {
  _retry?: boolean;
};

let getAccessTokenForRetry: (() => string | null) | null = null;
let refreshAccessTokenForRetry: (() => Promise<string | null>) | null = null;

/**
 * Dang ky bridge tu AuthContext vao api layer de interceptor co the refresh token.
 */
export const configureAuthInterceptors = (handlers: {
  getAccessToken: () => string | null;
  refreshAccessToken: () => Promise<string | null>;
}) => {
  getAccessTokenForRetry = handlers.getAccessToken;
  refreshAccessTokenForRetry = handlers.refreshAccessToken;
};

/**
 * Axios instance được cấu hình làm nền tảng cho toàn bộ API calls.
 *
 * @remarks
 * Các cấu hình quan trọng:
 * 1. baseURL: Trỏ đến Backend API.
 * 2. withCredentials: true → Trình duyệt tự đính kèm HttpOnly Cookie (refreshToken)
 *    trong mọi request. Đây là cơ chế cốt lõi của hệ thống bảo mật mới.
 * 3. Interceptors được thiết lập để token management tự động.
 *
 * Lưu ý: accessToken KHÔNG được đặt ở đây vì nó cần lấy từ Context (RAM)
 * mỗi lần gọi. Điều này được xử lý ở authApiClient bên dưới.
 */
export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  withCredentials: true, // Bắt buộc để Cookie tự đi kèm mỗi request
  headers: {
    'Content-Type': 'application/json',
  },
});

/**
 * Tạo một Axios instance mới kèm token, dùng cho các request đã xác thực.
 *
 * @param token - AccessToken lấy từ AuthContext (RAM-only).
 * @returns Axios instance với header Authorization được gắn sẵn.
 *
 * @remarks
 * Lý do tạo instance riêng thay vì setHeader mặc định:
 * Token nằm trong RAM (React Context), không thể truy cập trực tiếp
 * khi khởi tạo module. Mỗi lần gọi, caller truyền token hiện tại vào.
 */
export const createAuthClient = (token: string) => {
  const instance = axios.create({
    baseURL: API_BASE_URL,
    withCredentials: true,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
  });

  instance.interceptors.request.use((config) => {
    const latestToken = getAccessTokenForRetry?.();
    if (latestToken) {
      config.headers.Authorization = `Bearer ${latestToken}`;
    }

    return config;
  });

  instance.interceptors.response.use(
    (response) => response,
    async (error: AxiosError) => {
      const originalRequest = error.config as AuthRetryConfig | undefined;

      if (
        error.response?.status !== 401 ||
        !originalRequest ||
        originalRequest._retry ||
        !refreshAccessTokenForRetry
      ) {
        return Promise.reject(error);
      }

      originalRequest._retry = true;
      const newAccessToken = await refreshAccessTokenForRetry();
      if (!newAccessToken) {
        return Promise.reject(error);
      }

      originalRequest.headers.Authorization = `Bearer ${newAccessToken}`;
      return instance(originalRequest);
    }
  );

  return instance;
};

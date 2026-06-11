/**
 * Định nghĩa các kiểu dữ liệu liên quan đến xác thực người dùng.
 * Phản ánh chính xác DTOs trả về từ Backend AuthController.
 */

/**
 * Thông tin user lưu trong RAM (RAM-only, không persist).
 * Dữ liệu này tương ứng với phần public của auth/session response.
 */
export interface AuthUser {
  userId: string;
  username: string;
  displayName: string;
  avatarUrl?: string | null;
}

/**
 * Response public từ API Login/Register trong BFF mode.
 * Không chứa access token hoặc refresh token.
 */
export interface AuthClientResponse {
  userId: string;
  username: string;
  displayName: string;
  avatarUrl?: string | null;
  expiresAtUtc?: string;
}

export interface AuthSessionResponse {
  userId: string;
  username: string;
  displayName: string;
  avatarUrl?: string | null;
  expiresAtUtc: string;
}

export interface AuthUserResponse {
  userId: string;
  username: string;
  displayName: string;
  avatarUrl?: string | null;
  expiresAtUtc?: string;
}

export interface UserProfile {
  id: string;
  username: string;
  email: string;
  displayName: string;
  avatarUrl?: string | null;
}

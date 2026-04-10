/**
 * Định nghĩa các kiểu dữ liệu liên quan đến xác thực người dùng.
 * Phản ánh chính xác DTOs trả về từ Backend AuthController.
 */

/**
 * Thông tin user lưu trong RAM (RAM-only, không persist).
 * Dữ liệu này tương ứng với AuthClientResponse từ Backend.
 */
export interface AuthUser {
  userId: string;
  username: string;
  displayName: string;
}

/**
 * Response từ API Login/Register/Refresh.
 * Lưu ý: refreshToken KHÔNG có trong này - nó nằm trong HttpOnly Cookie.
 */
export interface AuthClientResponse {
  accessToken: string;
  userId: string;
  username: string;
  displayName: string;
}

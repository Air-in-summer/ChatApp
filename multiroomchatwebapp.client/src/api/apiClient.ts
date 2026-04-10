import axios from 'axios';

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
  baseURL: 'https://localhost:7222',
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
    baseURL: 'https://localhost:7222',
    withCredentials: true,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
  });

  return instance;
};

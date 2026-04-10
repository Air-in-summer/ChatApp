/**
 * Định nghĩa các kiểu dữ liệu liên quan đến Room và User Discovery.
 */

/**
 * Dữ liệu phòng chat trả về từ API /api/v1/rooms/my-rooms
 */
export interface RoomDto {
  id: string;
  type: 'DirectMessage' | 'Group';
  name: string | null;
  otherUserDisplayName?: string;
  otherUserUsername?: string;
}

/**
 * Kết quả tìm kiếm người dùng từ API /api/v1/users/search
 * Lưu ý: Backend trả về field `id` (không phải `userId`)
 */
export interface UserSearchResult {
  id: string;
  username: string;
  displayName: string;
}

/**
 * Trạng thái "Phòng Ảo" (Virtual Room) - Chưa được tạo trong DB.
 * Chỉ tồn tại trên RAM, khi gửi tin nhắn đầu tiên mới tạo phòng thật.
 */
export interface VirtualRoom {
  isVirtual: true;
  targetUser: UserSearchResult;
}

/**
 * Phòng chat đang được chọn trên UI.
 * Có thể là phòng thật (có ID) hoặc phòng ảo (chưa có ID).
 */
export type ActiveChat =
  | { type: 'real'; room: RoomDto }
  | { type: 'virtual'; targetUser: UserSearchResult };

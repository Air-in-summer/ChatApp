/**
 * Định nghĩa các kiểu dữ liệu liên quan đến Room và User Discovery.
 */

/**
 * Dữ liệu phòng chat trả về từ API /api/v1/rooms/my-rooms
 */
export interface RoomDto {
  id: string;
  type: 'DirectMessage' | 'Group' | 'Text' | 'Voice';
  name: string | null;
  otherUserId?: string | null;
  otherUserDisplayName?: string;
  otherUserUsername?: string;
  otherUserAvatarUrl?: string | null;
  lastMessageContent?: string;
  lastMessageTimestamp?: string;
  unreadCount?: number;
  lastReadMessageId?: string;
  isPrivate?: boolean;
  groupId?: string;
}

/**
 * Kết quả tìm kiếm người dùng từ API /api/v1/users/search
 * Lưu ý: Backend trả về field `id` (không phải `userId`)
 */
export interface UserSearchResult {
  id: string;
  username: string;
  displayName: string;
  avatarUrl?: string | null;
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

/**
 * Trạng thái của một tin nhắn để xử lý Optimistic UI
 */
export type MessageStatus = 'Sending' | 'Accepted' | 'Sent' | 'Delivered' | 'Read' | 'Failed';

export type AttachmentKind = 'Image' | 'Audio' | 'Video' | 'File';

export interface MessageAttachmentDto {
  mediaId?: string | null;
  kind: AttachmentKind;
  filename: string;
  size: number;
  mimeType: string;
  localPreviewUrl?: string;
  url?: string;
  thumbnailUrl?: string | null;
  expiresAt?: string | null;
}

export interface MediaUploadResultDto {
  mediaId: string;
  kind: AttachmentKind;
  filename: string;
  size: number;
  mimeType: string;
  previewUrl: string;
  expiresAt: string;
}

export interface MediaAccessUrlDto {
  mediaId: string;
  url: string;
  expiresAt?: string | null;
}

/**
 * Dữ liệu tin nhắn trả về từ Backend hoặc tạo tạm ở Frontend
 */
export interface MessageDto {
  id: string; // MessageId chính thức, hoặc clientMessageId trước khi server trả Accepted
  clientMessageId?: string | null;
  roomId: string;
  senderId: string;
  type: string;
  content: string;
  status: MessageStatus;
  createdAt: string; // ISO String
  acceptedAtUtc?: string | null;
  attachments?: MessageAttachmentDto[] | null;
}

export interface MessageAcceptedResult {
  clientMessageId: string;
  messageId: string;
  streamId: string;
  acceptedAtUtc: string;
}

export interface MessagePersistedDto {
  roomId: string;
  clientMessageId: string;
  messageId: string;
  persistedAtUtc: string;
  status: 'Sent';
}

export interface MessagePersistenceFailedDto {
  roomId: string;
  clientMessageId: string;
  messageId: string;
  code: string;
  failedAtUtc: string;
}

export interface MessageRetractedDto {
  roomId: string;
  clientMessageId: string;
  messageId: string;
  code: string;
  retractedAtUtc: string;
}

export interface GetMessagesResponse {
  data: MessageDto[];
  hasMore: boolean;
  nextCursor?: string | null;
}

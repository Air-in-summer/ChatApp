import { apiClient } from './apiClient';
import type {
  EditMessageRequest,
  MessageDeletedDto,
  MessageEditedDto,
  MessageDto,
  MessagePinnedDto,
  MessageReactionRequest,
  MessageReactionUpdatedDto,
  MessageUnpinnedDto,
} from '../types/chat';

/**
 * Sua noi dung tin nhan cua chinh nguoi gui.
 */
export const editMessage = async (
  roomId: string,
  messageId: string,
  request: EditMessageRequest
): Promise<MessageEditedDto> => {
  const response = await apiClient.patch<MessageEditedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}`,
    request
  );
  return response.data;
};

/**
 * Xoa mem tin nhan voi moi nguoi.
 */
export const deleteMessage = async (
  roomId: string,
  messageId: string
): Promise<MessageDeletedDto> => {
  const response = await apiClient.delete<MessageDeletedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}`
  );
  return response.data;
};

/**
 * Them reaction cua nguoi dung hien tai vao tin nhan.
 */
export const addMessageReaction = async (
  roomId: string,
  messageId: string,
  request: MessageReactionRequest
): Promise<MessageReactionUpdatedDto> => {
  const response = await apiClient.put<MessageReactionUpdatedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}/reactions`,
    request
  );
  return response.data;
};

/**
 * Go reaction cua nguoi dung hien tai khoi tin nhan.
 */
export const removeMessageReaction = async (
  roomId: string,
  messageId: string,
  request: MessageReactionRequest
): Promise<MessageReactionUpdatedDto> => {
  const response = await apiClient.delete<MessageReactionUpdatedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}/reactions`,
    { data: request }
  );
  return response.data;
};

/**
 * Ghim mot tin nhan trong phong.
 */
export const pinMessage = async (
  roomId: string,
  messageId: string
): Promise<MessagePinnedDto> => {
  const response = await apiClient.post<MessagePinnedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}/pin`
  );
  return response.data;
};

/**
 * Bo ghim mot tin nhan trong phong.
 */
export const unpinMessage = async (
  roomId: string,
  messageId: string
): Promise<MessageUnpinnedDto> => {
  const response = await apiClient.delete<MessageUnpinnedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}/pin`
  );
  return response.data;
};

/**
 * Lay danh sach tin nhan dang duoc ghim, moi nhat truoc.
 */
export const getPinnedMessages = async (
  roomId: string,
  limit = 50
): Promise<MessageDto[]> => {
  const response = await apiClient.get<MessageDto[]>(
    `/api/v1/chat/rooms/${roomId}/pins`,
    { params: { limit } }
  );
  return response.data;
};

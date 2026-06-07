import { createAuthClient } from './apiClient';
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
  token: string,
  roomId: string,
  messageId: string,
  request: EditMessageRequest
): Promise<MessageEditedDto> => {
  const client = createAuthClient(token);
  const response = await client.patch<MessageEditedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}`,
    request
  );
  return response.data;
};

/**
 * Xoa mem tin nhan voi moi nguoi.
 */
export const deleteMessage = async (
  token: string,
  roomId: string,
  messageId: string
): Promise<MessageDeletedDto> => {
  const client = createAuthClient(token);
  const response = await client.delete<MessageDeletedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}`
  );
  return response.data;
};

/**
 * Them reaction cua nguoi dung hien tai vao tin nhan.
 */
export const addMessageReaction = async (
  token: string,
  roomId: string,
  messageId: string,
  request: MessageReactionRequest
): Promise<MessageReactionUpdatedDto> => {
  const client = createAuthClient(token);
  const response = await client.put<MessageReactionUpdatedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}/reactions`,
    request
  );
  return response.data;
};

/**
 * Go reaction cua nguoi dung hien tai khoi tin nhan.
 */
export const removeMessageReaction = async (
  token: string,
  roomId: string,
  messageId: string,
  request: MessageReactionRequest
): Promise<MessageReactionUpdatedDto> => {
  const client = createAuthClient(token);
  const response = await client.delete<MessageReactionUpdatedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}/reactions`,
    { data: request }
  );
  return response.data;
};

/**
 * Ghim mot tin nhan trong phong.
 */
export const pinMessage = async (
  token: string,
  roomId: string,
  messageId: string
): Promise<MessagePinnedDto> => {
  const client = createAuthClient(token);
  const response = await client.post<MessagePinnedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}/pin`
  );
  return response.data;
};

/**
 * Bo ghim mot tin nhan trong phong.
 */
export const unpinMessage = async (
  token: string,
  roomId: string,
  messageId: string
): Promise<MessageUnpinnedDto> => {
  const client = createAuthClient(token);
  const response = await client.delete<MessageUnpinnedDto>(
    `/api/v1/chat/rooms/${roomId}/messages/${messageId}/pin`
  );
  return response.data;
};

/**
 * Lay danh sach tin nhan dang duoc ghim, moi nhat truoc.
 */
export const getPinnedMessages = async (
  token: string,
  roomId: string,
  limit = 50
): Promise<MessageDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<MessageDto[]>(
    `/api/v1/chat/rooms/${roomId}/pins`,
    { params: { limit } }
  );
  return response.data;
};

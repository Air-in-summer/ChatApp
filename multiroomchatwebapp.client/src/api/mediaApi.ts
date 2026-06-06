import { createAuthClient } from './apiClient';
import type { UserProfile } from '../types/auth';
import type { MediaAccessUrlDto, MediaUploadResultDto } from '../types/chat';

/**
 * Goi API upload avatar noi bo bang multipart/form-data.
 *
 * @param token - AccessToken hien tai trong AuthContext.
 * @param file - File anh .jpg/.jpeg/.png/.webp da chon tu input.
 * @returns Profile user sau khi backend cap nhat avatarUrl.
 */
export const uploadAvatar = async (token: string, file: File): Promise<UserProfile> => {
  const client = createAuthClient(token);
  const formData = new FormData();
  formData.append('file', file);

  const response = await client.post<UserProfile>('/api/v1/media/avatar', formData, {
    headers: {
      'Content-Type': 'multipart/form-data',
    },
  });

  return response.data;
};

/**
 * Goi API reset avatar ve fallback.
 *
 * @param token - AccessToken hien tai trong AuthContext.
 * @returns Profile user sau khi backend xoa avatarUrl.
 */
export const deleteAvatar = async (token: string): Promise<UserProfile> => {
  const client = createAuthClient(token);
  const response = await client.delete<UserProfile>('/api/v1/media/avatar');
  return response.data;
};

export const uploadChatMedia = async (
  token: string,
  file: File,
  signal?: AbortSignal
): Promise<MediaUploadResultDto> => {
  const client = createAuthClient(token);
  const formData = new FormData();
  formData.append('file', file);

  const response = await client.post<MediaUploadResultDto>('/api/v1/media/chat/pending', formData, {
    headers: {
      'Content-Type': 'multipart/form-data',
    },
    signal,
  });

  return response.data;
};

export const cancelPendingChatMedia = async (
  token: string,
  mediaId: string
): Promise<void> => {
  const client = createAuthClient(token);
  await client.delete(`/api/v1/media/chat/pending/${mediaId}`);
};

export const getMediaAccessUrl = async (
  token: string,
  mediaId: string,
  signal?: AbortSignal
): Promise<MediaAccessUrlDto> => {
  const client = createAuthClient(token);
  const response = await client.post<MediaAccessUrlDto>(
    `/api/v1/media/${mediaId}/access-url`,
    undefined,
    { signal }
  );

  return response.data;
};

export const getMediaContentBlob = async (
  token: string,
  mediaId: string,
  signal?: AbortSignal
): Promise<Blob> => {
  const client = createAuthClient(token);
  const response = await client.get<Blob>(
    `/api/v1/media/${mediaId}/content`,
    {
      responseType: 'blob',
      signal,
    }
  );

  return response.data;
};

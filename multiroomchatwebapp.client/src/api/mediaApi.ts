import { apiClient } from './apiClient';
import type { UserProfile } from '../types/auth';
import type { MediaAccessUrlDto, MediaUploadResultDto } from '../types/chat';
import type { GroupDto } from '../types/group';

/**
 * Goi API upload avatar noi bo bang multipart/form-data.
 *
 * @param file - File anh .jpg/.jpeg/.png/.webp da chon tu input.
 * @returns Profile user sau khi backend cap nhat avatarUrl.
 */
export const uploadAvatar = async (file: File): Promise<UserProfile> => {
  const formData = new FormData();
  formData.append('file', file);

  const response = await apiClient.post<UserProfile>('/api/v1/media/avatar', formData, {
    headers: {
      'Content-Type': 'multipart/form-data',
    },
  });

  return response.data;
};

/**
 * Goi API reset avatar ve fallback.
 *
 * @returns Profile user sau khi backend xoa avatarUrl.
 */
export const deleteAvatar = async (): Promise<UserProfile> => {
  const response = await apiClient.delete<UserProfile>('/api/v1/media/avatar');
  return response.data;
};

export const uploadGroupIcon = async (
  groupId: string,
  file: File
): Promise<GroupDto> => {
  const formData = new FormData();
  formData.append('file', file);

  const response = await apiClient.post<GroupDto>(`/api/v1/media/groups/${groupId}/icon`, formData, {
    headers: {
      'Content-Type': 'multipart/form-data',
    },
  });

  return response.data;
};

export const uploadChatMedia = async (
  file: File,
  signal?: AbortSignal
): Promise<MediaUploadResultDto> => {
  const formData = new FormData();
  formData.append('file', file);

  const response = await apiClient.post<MediaUploadResultDto>('/api/v1/media/chat/pending', formData, {
    headers: {
      'Content-Type': 'multipart/form-data',
    },
    signal,
  });

  return response.data;
};

export const cancelPendingChatMedia = async (
  mediaId: string
): Promise<void> => {
  await apiClient.delete(`/api/v1/media/chat/pending/${mediaId}`);
};

export const getMediaAccessUrl = async (
  mediaId: string,
  signal?: AbortSignal
): Promise<MediaAccessUrlDto> => {
  const response = await apiClient.post<MediaAccessUrlDto>(
    `/api/v1/media/${mediaId}/access-url`,
    undefined,
    { signal }
  );

  return response.data;
};

export const getMediaContentBlob = async (
  mediaId: string,
  signal?: AbortSignal
): Promise<Blob> => {
  const response = await apiClient.get<Blob>(
    `/api/v1/media/${mediaId}/content`,
    {
      responseType: 'blob',
      signal,
    }
  );

  return response.data;
};

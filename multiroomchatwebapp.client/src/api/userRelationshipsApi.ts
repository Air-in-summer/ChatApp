import { apiClient } from './apiClient';
import type {
  BlockedUserDto,
  BlockUserRequest,
  CreateFriendRequestRequest,
  FriendDto,
  FriendRequestDto,
  PresenceDto,
} from '../types/userRelationships';

const RELATIONSHIPS_BASE_URL = '/api/v1/users/relationships';

export const getFriends = async (): Promise<FriendDto[]> => {
  const response = await apiClient.get<FriendDto[]>(`${RELATIONSHIPS_BASE_URL}/friends`);
  return response.data;
};

export const removeFriend = async (userId: string): Promise<void> => {
  await apiClient.delete(`${RELATIONSHIPS_BASE_URL}/friends/${userId}`);
};

export const getIncomingFriendRequests = async (): Promise<FriendRequestDto[]> => {
  const response = await apiClient.get<FriendRequestDto[]>(`${RELATIONSHIPS_BASE_URL}/friend-requests/incoming`);
  return response.data;
};

export const getOutgoingFriendRequests = async (): Promise<FriendRequestDto[]> => {
  const response = await apiClient.get<FriendRequestDto[]>(`${RELATIONSHIPS_BASE_URL}/friend-requests/outgoing`);
  return response.data;
};

export const createFriendRequest = async (
  receiverId: string,
): Promise<FriendRequestDto> => {
  const request: CreateFriendRequestRequest = { receiverId };
  const response = await apiClient.post<FriendRequestDto>(`${RELATIONSHIPS_BASE_URL}/friend-requests`, request);
  return response.data;
};

export const acceptFriendRequest = async (
  requestId: string,
): Promise<FriendRequestDto> => {
  const response = await apiClient.post<FriendRequestDto>(
    `${RELATIONSHIPS_BASE_URL}/friend-requests/${requestId}/accept`,
  );
  return response.data;
};

export const declineFriendRequest = async (
  requestId: string,
): Promise<FriendRequestDto> => {
  const response = await apiClient.post<FriendRequestDto>(
    `${RELATIONSHIPS_BASE_URL}/friend-requests/${requestId}/decline`,
  );
  return response.data;
};

export const cancelFriendRequest = async (
  requestId: string,
): Promise<FriendRequestDto> => {
  const response = await apiClient.post<FriendRequestDto>(
    `${RELATIONSHIPS_BASE_URL}/friend-requests/${requestId}/cancel`,
  );
  return response.data;
};

export const getBlockedUsers = async (): Promise<BlockedUserDto[]> => {
  const response = await apiClient.get<BlockedUserDto[]>(`${RELATIONSHIPS_BASE_URL}/blocks`);
  return response.data;
};

export const blockUser = async (
  blockedUserId: string,
): Promise<BlockedUserDto> => {
  const request: BlockUserRequest = { blockedUserId };
  const response = await apiClient.post<BlockedUserDto>(`${RELATIONSHIPS_BASE_URL}/blocks`, request);
  return response.data;
};

export const unblockUser = async (userId: string): Promise<void> => {
  await apiClient.delete(`${RELATIONSHIPS_BASE_URL}/blocks/${userId}`);
};

export const getFriendsPresence = async (): Promise<PresenceDto[]> => {
  const response = await apiClient.get<PresenceDto[]>(`${RELATIONSHIPS_BASE_URL}/presence/friends`);
  return response.data;
};

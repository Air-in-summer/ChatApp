import { createAuthClient } from './apiClient';
import type {
  BlockedUserDto,
  BlockUserRequest,
  CreateFriendRequestRequest,
  FriendDto,
  FriendRequestDto,
  PresenceDto,
} from '../types/userRelationships';

const RELATIONSHIPS_BASE_URL = '/api/v1/users/relationships';

export const getFriends = async (token: string): Promise<FriendDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<FriendDto[]>(`${RELATIONSHIPS_BASE_URL}/friends`);
  return response.data;
};

export const removeFriend = async (token: string, userId: string): Promise<void> => {
  const client = createAuthClient(token);
  await client.delete(`${RELATIONSHIPS_BASE_URL}/friends/${userId}`);
};

export const getIncomingFriendRequests = async (token: string): Promise<FriendRequestDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<FriendRequestDto[]>(`${RELATIONSHIPS_BASE_URL}/friend-requests/incoming`);
  return response.data;
};

export const getOutgoingFriendRequests = async (token: string): Promise<FriendRequestDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<FriendRequestDto[]>(`${RELATIONSHIPS_BASE_URL}/friend-requests/outgoing`);
  return response.data;
};

export const createFriendRequest = async (
  token: string,
  receiverId: string,
): Promise<FriendRequestDto> => {
  const client = createAuthClient(token);
  const request: CreateFriendRequestRequest = { receiverId };
  const response = await client.post<FriendRequestDto>(`${RELATIONSHIPS_BASE_URL}/friend-requests`, request);
  return response.data;
};

export const acceptFriendRequest = async (
  token: string,
  requestId: string,
): Promise<FriendRequestDto> => {
  const client = createAuthClient(token);
  const response = await client.post<FriendRequestDto>(
    `${RELATIONSHIPS_BASE_URL}/friend-requests/${requestId}/accept`,
  );
  return response.data;
};

export const declineFriendRequest = async (
  token: string,
  requestId: string,
): Promise<FriendRequestDto> => {
  const client = createAuthClient(token);
  const response = await client.post<FriendRequestDto>(
    `${RELATIONSHIPS_BASE_URL}/friend-requests/${requestId}/decline`,
  );
  return response.data;
};

export const cancelFriendRequest = async (
  token: string,
  requestId: string,
): Promise<FriendRequestDto> => {
  const client = createAuthClient(token);
  const response = await client.post<FriendRequestDto>(
    `${RELATIONSHIPS_BASE_URL}/friend-requests/${requestId}/cancel`,
  );
  return response.data;
};

export const getBlockedUsers = async (token: string): Promise<BlockedUserDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<BlockedUserDto[]>(`${RELATIONSHIPS_BASE_URL}/blocks`);
  return response.data;
};

export const blockUser = async (
  token: string,
  blockedUserId: string,
): Promise<BlockedUserDto> => {
  const client = createAuthClient(token);
  const request: BlockUserRequest = { blockedUserId };
  const response = await client.post<BlockedUserDto>(`${RELATIONSHIPS_BASE_URL}/blocks`, request);
  return response.data;
};

export const unblockUser = async (token: string, userId: string): Promise<void> => {
  const client = createAuthClient(token);
  await client.delete(`${RELATIONSHIPS_BASE_URL}/blocks/${userId}`);
};

export const getFriendsPresence = async (token: string): Promise<PresenceDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<PresenceDto[]>(`${RELATIONSHIPS_BASE_URL}/presence/friends`);
  return response.data;
};

export type FriendRequestStatus = 'Pending' | 'Accepted' | 'Declined' | 'Canceled';

export interface UserRelationshipProfileDto {
  id: string;
  username: string;
  displayName: string;
  avatarUrl?: string | null;
}

export interface FriendDto {
  user: UserRelationshipProfileDto;
  friendsSince: string;
}

export interface FriendRequestDto {
  id: string;
  requester: UserRelationshipProfileDto;
  receiver: UserRelationshipProfileDto;
  status: FriendRequestStatus;
  createdAt: string;
  respondedAt?: string | null;
  canceledAt?: string | null;
}

export interface BlockedUserDto {
  user: UserRelationshipProfileDto;
  blockedAt: string;
}

export interface PresenceDto {
  userId: string;
  isOnline: boolean;
  lastSeenAt?: string | null;
}

export interface CreateFriendRequestRequest {
  receiverId: string;
}

export interface BlockUserRequest {
  blockedUserId: string;
}

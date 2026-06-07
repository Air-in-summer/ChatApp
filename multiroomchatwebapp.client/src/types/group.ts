/**
 * Dữ liệu trả về cho một Server (Group)
 */
export interface GroupDto {
  id: string;
  name: string;
  description?: string;
  iconUrl?: string;
  inviteCode: string;
  ownerId: string;
  createdAt: string;
}

/**
 * Request tạo Server mới
 */
export interface CreateGroupRequest {
  name: string;
  description?: string;
}

export interface UpdateGroupRequest {
  name?: string;
  description?: string;
  iconUrl?: string;
}

/**
 * Các vai trò trong một Server
 */
export type GroupRole = 'Owner' | 'Admin' | 'Member';

/**
 * Dữ liệu thành viên trong Server
 */
export interface GroupMemberDto {
  profile: {
    id: string;
    username: string;
    displayName: string;
    avatarUrl?: string;
  };
  role: GroupRole;
  joinedAt?: string;
}
/**
 * Request tạo Channel mới trong Server
 */
export interface CreateGroupChannelRequest {
  name: string;
  type: 'Text' | 'Voice';
  isPrivate: boolean;
}

export interface UpdateRoomRequest {
  name?: string;
}

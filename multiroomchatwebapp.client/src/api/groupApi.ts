import { apiClient } from './apiClient';
import type { 
  GroupDto, 
  CreateGroupRequest, 
  UpdateGroupRequest,
  GroupRole,
  GroupMemberDto, 
  CreateGroupChannelRequest,
  UpdateRoomRequest,
} from '../types/group';
import type { RoomDto } from '../types/chat';

/**
 * Gọi API: GET /api/v1/groups/mine - Lấy danh sách Server mà user tham gia
 * 
 * @returns Promise<GroupDto[]> - Danh sách Server
 */
export const getMyGroups = async (): Promise<GroupDto[]> => {
  const response = await apiClient.get<GroupDto[]>('/api/v1/groups');
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups - Tạo một Server mới
 * 
 * @param request - Thông tin Server mới (Tên, mô tả)
 * @returns Promise<GroupDto> - Server vừa tạo
 */
export const createGroup = async (request: CreateGroupRequest): Promise<GroupDto> => {
  const response = await apiClient.post<GroupDto>('/api/v1/groups', request);
  return response.data;
};

export const updateGroup = async (
  groupId: string,
  request: UpdateGroupRequest
): Promise<GroupDto> => {
  const response = await apiClient.patch<GroupDto>(`/api/v1/groups/${groupId}`, request);
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups/join/{code} - Tham gia Server qua mã mời
 * 
 * @param code - Mã mời (6 ký tự)
 * @returns Promise<GroupDto> - Thông tin Server vừa gia nhập
 */
export const joinGroupByInviteCode = async (code: string): Promise<GroupDto> => {
  const response = await apiClient.post<GroupDto>(`/api/v1/groups/join/${code}`);
  return response.data;
};

/**
 * Gọi API: GET /api/v1/groups/{groupId}/rooms - Lấy danh sách Channel trong Server
 * 
 * @param groupId - ID của Server
 * @returns Promise<RoomDto[]> - Danh sách các Channel (Room)
 */
export const getGroupRooms = async (groupId: string): Promise<RoomDto[]> => {
  const response = await apiClient.get<RoomDto[]>(`/api/v1/groups/${groupId}/rooms`);
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/rooms - Tạo một Channel mới (Bước 15.1)
 * 
 * @param groupId - ID của Server
 * @param request - Thông tin Channel mới (Tên, Loại, Riêng tư)
 * @returns Promise<{ roomId: string }> - ID của Channel vừa tạo
 */
export const createGroupChannel = async (
  groupId: string, 
  request: CreateGroupChannelRequest
): Promise<{ roomId: string }> => {
  const response = await apiClient.post<{ roomId: string }>(`/api/v1/groups/${groupId}/rooms`, request);
  return response.data;
};

export const updateGroupRoom = async (
  groupId: string,
  roomId: string,
  request: UpdateRoomRequest
): Promise<RoomDto> => {
  const response = await apiClient.patch<RoomDto>(`/api/v1/groups/${groupId}/rooms/${roomId}`, request);
  return response.data;
};

export const deleteGroupRoom = async (
  groupId: string,
  roomId: string
): Promise<void> => {
  await apiClient.delete(`/api/v1/groups/${groupId}/rooms/${roomId}`);
};

/**
 * Gọi API: GET /api/v1/groups/{groupId}/members - Lấy danh sách thành viên Server
 * 
 * @param groupId - ID của Server
 * @returns Promise<GroupMemberDto[]> - Danh sách thành viên và vai trò
 */
export const getGroupMembers = async (groupId: string): Promise<GroupMemberDto[]> => {
  const response = await apiClient.get<GroupMemberDto[]>(`/api/v1/groups/${groupId}/members`);
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/rooms/{roomId}/members - Thêm thành viên vào phòng Private
 * 
 * @param groupId - ID của Server
 * @param roomId - ID của phòng chat
 * @param userIds - Danh sách ID của các thành viên cần thêm
 * @returns Promise<{ addedCount: number }> - Số lượng thành viên đã thêm thành công
 */
export const addMembersToRoom = async (
  groupId: string,
  roomId: string,
  userIds: string[]
): Promise<{ addedCount: number }> => {
  const response = await apiClient.post<{ addedCount: number }>(
    `/api/v1/groups/${groupId}/rooms/${roomId}/members`,
    { userIds }
  );
  return response.data;
};

/**
 * Gọi API: GET /api/v1/groups/{groupId}/rooms/{roomId}/members/ids - Lấy danh sách ID thành viên hiện tại của phòng Private
 * 
 * @param groupId - ID Server
 * @param roomId - ID Phòng
 * @returns Promise<string[]> - Danh sách User IDs
 */
export const getRoomMemberIds = async (groupId: string, roomId: string): Promise<string[]> => {
  const response = await apiClient.get<string[]>(`/api/v1/groups/${groupId}/rooms/${roomId}/members/ids`);
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/leave - Tự rời khỏi Server
 * 
 * @param groupId - ID của Server
 */
export const leaveGroup = async (groupId: string): Promise<void> => {
  await apiClient.post(`/api/v1/groups/${groupId}/leave`);
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/kick/{userId} - Đuổi thành viên (Yêu cầu quyền Owner/Admin)
 * 
 * @param groupId - ID của Server
 * @param userId - ID của thành viên bị đuổi
 */
export const kickMember = async (groupId: string, userId: string): Promise<void> => {
    await apiClient.post(`/api/v1/groups/${groupId}/kick/${userId}`);
};

/**
 * Gọi API: PATCH /api/v1/groups/{groupId}/members/{userId}/role - đổi vai trò Admin/Member.
 *
 * @param groupId - ID của Server
 * @param userId - ID của thành viên bị đổi vai trò
 * @param role - Vai trò mới, chỉ dùng Admin hoặc Member
 */
export const updateGroupMemberRole = async (
  groupId: string,
  userId: string,
  role: Exclude<GroupRole, 'Owner'>
): Promise<void> => {
  await apiClient.patch(`/api/v1/groups/${groupId}/members/${userId}/role`, role);
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/transfer-ownership - trao quyền Owner.
 *
 * @param groupId - ID của Server
 * @param newOwnerId - ID thành viên sẽ trở thành Owner mới
 */
export const transferGroupOwnership = async (
  groupId: string,
  newOwnerId: string
): Promise<void> => {
  await apiClient.post(`/api/v1/groups/${groupId}/transfer-ownership`, newOwnerId);
};

/**
 * Gọi API: DELETE /api/v1/groups/{groupId} - Giải tán Server (Chỉ Owner)
 * 
 * @param groupId - ID của Server
 */
export const deleteGroup = async (groupId: string): Promise<void> => {
  await apiClient.delete(`/api/v1/groups/${groupId}`);
};

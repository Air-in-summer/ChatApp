import { createAuthClient } from './apiClient';
import type { 
  GroupDto, 
  CreateGroupRequest, 
  GroupMemberDto, 
  CreateGroupChannelRequest 
} from '../types/group';
import type { RoomDto } from '../types/chat';

/**
 * Gọi API: GET /api/v1/groups/mine - Lấy danh sách Server mà user tham gia
 * 
 * @param token - AccessToken từ AuthContext
 * @returns Promise<GroupDto[]> - Danh sách Server
 */
export const getMyGroups = async (token: string): Promise<GroupDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<GroupDto[]>('/api/v1/groups');
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups - Tạo một Server mới
 * 
 * @param token - AccessToken từ AuthContext
 * @param request - Thông tin Server mới (Tên, mô tả)
 * @returns Promise<GroupDto> - Server vừa tạo
 */
export const createGroup = async (token: string, request: CreateGroupRequest): Promise<GroupDto> => {
  const client = createAuthClient(token);
  const response = await client.post<GroupDto>('/api/v1/groups', request);
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups/join/{code} - Tham gia Server qua mã mời
 * 
 * @param token - AccessToken từ AuthContext
 * @param code - Mã mời (6 ký tự)
 * @returns Promise<GroupDto> - Thông tin Server vừa gia nhập
 */
export const joinGroupByInviteCode = async (token: string, code: string): Promise<GroupDto> => {
  const client = createAuthClient(token);
  const response = await client.post<GroupDto>(`/api/v1/groups/join/${code}`);
  return response.data;
};

/**
 * Gọi API: GET /api/v1/groups/{groupId}/rooms - Lấy danh sách Channel trong Server
 * 
 * @param token - AccessToken từ AuthContext
 * @param groupId - ID của Server
 * @returns Promise<RoomDto[]> - Danh sách các Channel (Room)
 */
export const getGroupRooms = async (token: string, groupId: string): Promise<RoomDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<RoomDto[]>(`/api/v1/groups/${groupId}/rooms`);
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/rooms - Tạo một Channel mới (Bước 15.1)
 * 
 * @param token - AccessToken từ AuthContext
 * @param groupId - ID của Server
 * @param request - Thông tin Channel mới (Tên, Loại, Riêng tư)
 * @returns Promise<{ roomId: string }> - ID của Channel vừa tạo
 */
export const createGroupChannel = async (
  token: string, 
  groupId: string, 
  request: CreateGroupChannelRequest
): Promise<{ roomId: string }> => {
  const client = createAuthClient(token);
  const response = await client.post<{ roomId: string }>(`/api/v1/groups/${groupId}/rooms`, request);
  return response.data;
};

/**
 * Gọi API: GET /api/v1/groups/{groupId}/members - Lấy danh sách thành viên Server
 * 
 * @param token - AccessToken từ AuthContext
 * @param groupId - ID của Server
 * @returns Promise<GroupMemberDto[]> - Danh sách thành viên và vai trò
 */
export const getGroupMembers = async (token: string, groupId: string): Promise<GroupMemberDto[]> => {
  const client = createAuthClient(token);
  const response = await client.get<GroupMemberDto[]>(`/api/v1/groups/${groupId}/members`);
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/rooms/{roomId}/members - Thêm thành viên vào phòng Private
 * 
 * @param token - AccessToken từ AuthContext
 * @param groupId - ID của Server
 * @param roomId - ID của phòng chat
 * @param userIds - Danh sách ID của các thành viên cần thêm
 * @returns Promise<{ addedCount: number }> - Số lượng thành viên đã thêm thành công
 */
export const addMembersToRoom = async (
  token: string,
  groupId: string,
  roomId: string,
  userIds: string[]
): Promise<{ addedCount: number }> => {
  const client = createAuthClient(token);
  const response = await client.post<{ addedCount: number }>(
    `/api/v1/groups/${groupId}/rooms/${roomId}/members`,
    { userIds }
  );
  return response.data;
};

/**
 * Gọi API: GET /api/v1/groups/{groupId}/rooms/{roomId}/members/ids - Lấy danh sách ID thành viên hiện tại của phòng Private
 * 
 * @param token - AccessToken
 * @param groupId - ID Server
 * @param roomId - ID Phòng
 * @returns Promise<string[]> - Danh sách User IDs
 */
export const getRoomMemberIds = async (token: string, groupId: string, roomId: string): Promise<string[]> => {
  const client = createAuthClient(token);
  const response = await client.get<string[]>(`/api/v1/groups/${groupId}/rooms/${roomId}/members/ids`);
  return response.data;
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/leave - Tự rời khỏi Server
 * 
 * @param token - AccessToken từ AuthContext
 * @param groupId - ID của Server
 */
export const leaveGroup = async (token: string, groupId: string): Promise<void> => {
  const client = createAuthClient(token);
  await client.post(`/api/v1/groups/${groupId}/leave`);
};

/**
 * Gọi API: POST /api/v1/groups/{groupId}/kick/{userId} - Đuổi thành viên (Yêu cầu quyền Owner/Admin)
 * 
 * @param token - AccessToken từ AuthContext
 * @param groupId - ID của Server
 * @param userId - ID của thành viên bị đuổi
 */
export const kickMember = async (token: string, groupId: string, userId: string): Promise<void> => {
    const client = createAuthClient(token);
    await client.post(`/api/v1/groups/${groupId}/kick/${userId}`);
};

/**
 * Gọi API: DELETE /api/v1/groups/{groupId} - Giải tán Server (Chỉ Owner)
 * 
 * @param token - AccessToken từ AuthContext
 * @param groupId - ID của Server
 */
export const deleteGroup = async (token: string, groupId: string): Promise<void> => {
  const client = createAuthClient(token);
  await client.delete(`/api/v1/groups/${groupId}`);
};


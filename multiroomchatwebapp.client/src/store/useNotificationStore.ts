import { create } from 'zustand';

interface NotificationState {
  // Cờ báo hiệu GroupId nào vừa có thay đổi về Room và cần refetch danh sách
  roomRefetchGroupId: string | null;

  // Trạng thái khi User bị kick khỏi một Group
  kickedFromGroup: { groupId: string; groupName: string } | null;

  // Trạng thái khi một Group bị giải tán
  deletedGroup: { groupId: string; groupName: string } | null;

  // Actions
  triggerRoomRefetch: (groupId: string) => void;
  clearRoomRefetch: () => void;
  
  handleKicked: (groupId: string, groupName: string) => void;
  clearKicked: () => void;

  handleGroupDeleted: (groupId: string, groupName: string) => void;
  clearGroupDeleted: () => void;
}

/**
 * Store chuyên biệt để quản lý các tín hiệu Notification thời gian thực.
 * Giúp điều phối hành động giữa SignalR (useSignalR) và các Component UI.
 */
export const useNotificationStore = create<NotificationState>((set) => ({
  roomRefetchGroupId: null,
  kickedFromGroup: null,
  deletedGroup: null,

  triggerRoomRefetch: (groupId) => set({ roomRefetchGroupId: groupId }),
  clearRoomRefetch: () => set({ roomRefetchGroupId: null }),

  handleKicked: (groupId, groupName) => set({ kickedFromGroup: { groupId, groupName } }),
  clearKicked: () => set({ kickedFromGroup: null }),

  handleGroupDeleted: (groupId, groupName) => set({ deletedGroup: { groupId, groupName } }),
  clearGroupDeleted: () => set({ deletedGroup: null }),
}));

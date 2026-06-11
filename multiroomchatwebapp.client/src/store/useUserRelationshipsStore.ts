import { create } from 'zustand';
import {
  acceptFriendRequest,
  blockUser as blockUserApi,
  cancelFriendRequest,
  createFriendRequest,
  declineFriendRequest,
  getBlockedUsers,
  getFriends,
  getFriendsPresence,
  getIncomingFriendRequests,
  getOutgoingFriendRequests,
  removeFriend as removeFriendApi,
  unblockUser as unblockUserApi,
} from '../api/userRelationshipsApi';
import type {
  BlockedUserDto,
  FriendDto,
  FriendRequestDto,
  PresenceDto,
} from '../types/userRelationships';

type PresenceByUserId = Record<string, PresenceDto>;

interface UserRelationshipsState {
  friends: FriendDto[];
  incomingRequests: FriendRequestDto[];
  outgoingRequests: FriendRequestDto[];
  blockedUsers: BlockedUserDto[];
  presenceByUserId: PresenceByUserId;
  isLoadingFriends: boolean;
  isLoadingIncomingRequests: boolean;
  isLoadingOutgoingRequests: boolean;
  isLoadingBlockedUsers: boolean;
  isLoadingPresence: boolean;
  friendsError: string | null;
  incomingRequestsError: string | null;
  outgoingRequestsError: string | null;
  blockedUsersError: string | null;
  presenceError: string | null;
  loadFriends: () => Promise<void>;
  loadIncomingRequests: () => Promise<void>;
  loadOutgoingRequests: () => Promise<void>;
  loadBlockedUsers: () => Promise<void>;
  loadFriendsPresence: () => Promise<void>;
  loadAllRelationships: () => Promise<void>;
  sendFriendRequest: (receiverId: string) => Promise<FriendRequestDto>;
  acceptFriendRequest: (requestId: string) => Promise<FriendRequestDto>;
  declineFriendRequest: (requestId: string) => Promise<FriendRequestDto>;
  cancelFriendRequest: (requestId: string) => Promise<FriendRequestDto>;
  removeFriend: (userId: string) => Promise<void>;
  blockUser: (blockedUserId: string) => Promise<BlockedUserDto>;
  unblockUser: (userId: string) => Promise<void>;
  setUserOnline: (userId: string) => void;
  setUserOffline: (userId: string, lastSeenAt?: string | null) => void;
  setPresenceSnapshot: (presenceList: PresenceDto[]) => void;
  getFriendByUserId: (userId: string) => FriendDto | undefined;
  getIncomingRequestByUserId: (userId: string) => FriendRequestDto | undefined;
  getOutgoingRequestByUserId: (userId: string) => FriendRequestDto | undefined;
  isFriend: (userId: string) => boolean;
  isBlockedByCurrentUser: (userId: string) => boolean;
  getPresence: (userId: string) => PresenceDto | undefined;
}

const getErrorMessage = (error: unknown, fallback: string): string =>
  error instanceof Error && error.message ? error.message : fallback;

const upsertById = <T extends { id: string }>(items: T[], item: T): T[] => {
  const existingIndex = items.findIndex(existing => existing.id === item.id);
  if (existingIndex === -1) {
    return [item, ...items];
  }

  return items.map(existing => (existing.id === item.id ? item : existing));
};

const upsertBlockedUser = (items: BlockedUserDto[], item: BlockedUserDto): BlockedUserDto[] => {
  const existingIndex = items.findIndex(existing => existing.user.id === item.user.id);
  if (existingIndex === -1) {
    return [item, ...items];
  }

  return items.map(existing => (existing.user.id === item.user.id ? item : existing));
};

const toPresenceByUserId = (presenceList: PresenceDto[]): PresenceByUserId =>
  presenceList.reduce<PresenceByUserId>((acc, presence) => {
    acc[presence.userId] = presence;
    return acc;
  }, {});

const removePresenceByUserId = (
  presenceByUserId: PresenceByUserId,
  userId: string,
): PresenceByUserId => {
  const nextPresenceByUserId = { ...presenceByUserId };
  delete nextPresenceByUserId[userId];
  return nextPresenceByUserId;
};

const requestIncludesUser = (request: FriendRequestDto, userId: string): boolean =>
  request.requester.id === userId || request.receiver.id === userId;

export const useUserRelationshipsStore = create<UserRelationshipsState>((set, get) => ({
  friends: [],
  incomingRequests: [],
  outgoingRequests: [],
  blockedUsers: [],
  presenceByUserId: {},
  isLoadingFriends: false,
  isLoadingIncomingRequests: false,
  isLoadingOutgoingRequests: false,
  isLoadingBlockedUsers: false,
  isLoadingPresence: false,
  friendsError: null,
  incomingRequestsError: null,
  outgoingRequestsError: null,
  blockedUsersError: null,
  presenceError: null,

  loadFriends: async () => {
    set({ isLoadingFriends: true, friendsError: null });

    try {
      const friends = await getFriends();
      set({ friends });
    } catch (error) {
      set({ friendsError: getErrorMessage(error, 'Khong the tai danh sach ban be.') });
      throw error;
    } finally {
      set({ isLoadingFriends: false });
    }
  },

  loadIncomingRequests: async () => {
    set({ isLoadingIncomingRequests: true, incomingRequestsError: null });

    try {
      const incomingRequests = await getIncomingFriendRequests();
      set({ incomingRequests });
    } catch (error) {
      set({ incomingRequestsError: getErrorMessage(error, 'Khong the tai loi moi ket ban.') });
      throw error;
    } finally {
      set({ isLoadingIncomingRequests: false });
    }
  },

  loadOutgoingRequests: async () => {
    set({ isLoadingOutgoingRequests: true, outgoingRequestsError: null });

    try {
      const outgoingRequests = await getOutgoingFriendRequests();
      set({ outgoingRequests });
    } catch (error) {
      set({ outgoingRequestsError: getErrorMessage(error, 'Khong the tai loi moi da gui.') });
      throw error;
    } finally {
      set({ isLoadingOutgoingRequests: false });
    }
  },

  loadBlockedUsers: async () => {
    set({ isLoadingBlockedUsers: true, blockedUsersError: null });

    try {
      const blockedUsers = await getBlockedUsers();
      set({ blockedUsers });
    } catch (error) {
      set({ blockedUsersError: getErrorMessage(error, 'Khong the tai danh sach da chan.') });
      throw error;
    } finally {
      set({ isLoadingBlockedUsers: false });
    }
  },

  loadFriendsPresence: async () => {
    set({ isLoadingPresence: true, presenceError: null });

    try {
      const presenceList = await getFriendsPresence();
      set({ presenceByUserId: toPresenceByUserId(presenceList) });
    } catch (error) {
      set({ presenceError: getErrorMessage(error, 'Khong the tai trang thai online.') });
      throw error;
    } finally {
      set({ isLoadingPresence: false });
    }
  },

  loadAllRelationships: async () => {
    await Promise.all([
      get().loadFriends(),
      get().loadIncomingRequests(),
      get().loadOutgoingRequests(),
      get().loadBlockedUsers(),
      get().loadFriendsPresence(),
    ]);
  },

  sendFriendRequest: async (receiverId: string) => {
    const request = await createFriendRequest(receiverId);
    set(state => ({
      outgoingRequests: upsertById(state.outgoingRequests, request),
    }));
    return request;
  },

  acceptFriendRequest: async (requestId: string) => {
    const request = await acceptFriendRequest(requestId);
    set(state => ({
      incomingRequests: state.incomingRequests.filter(existing => existing.id !== requestId),
    }));

    await get().loadFriends();

    return request;
  },

  declineFriendRequest: async (requestId: string) => {
    const request = await declineFriendRequest(requestId);
    set(state => ({
      incomingRequests: state.incomingRequests.filter(existing => existing.id !== requestId),
    }));
    return request;
  },

  cancelFriendRequest: async (requestId: string) => {
    const request = await cancelFriendRequest(requestId);
    set(state => ({
      outgoingRequests: state.outgoingRequests.filter(existing => existing.id !== requestId),
    }));
    return request;
  },

  removeFriend: async (userId: string) => {
    await removeFriendApi(userId);
    set(state => ({
      friends: state.friends.filter(friend => friend.user.id !== userId),
      presenceByUserId: removePresenceByUserId(state.presenceByUserId, userId),
    }));
  },

  blockUser: async (blockedUserId: string) => {
    const blockedUser = await blockUserApi(blockedUserId);
    set(state => ({
      friends: state.friends.filter(friend => friend.user.id !== blockedUserId),
      incomingRequests: state.incomingRequests.filter(request => !requestIncludesUser(request, blockedUserId)),
      outgoingRequests: state.outgoingRequests.filter(request => !requestIncludesUser(request, blockedUserId)),
      blockedUsers: upsertBlockedUser(state.blockedUsers, blockedUser),
      presenceByUserId: removePresenceByUserId(state.presenceByUserId, blockedUserId),
    }));
    return blockedUser;
  },

  unblockUser: async (userId: string) => {
    await unblockUserApi(userId);
    set(state => ({
      blockedUsers: state.blockedUsers.filter(blockedUser => blockedUser.user.id !== userId),
    }));
  },

  setUserOnline: (userId: string) => {
    set(state => {
      const existingPresence = state.presenceByUserId[userId];
      return {
        presenceByUserId: {
          ...state.presenceByUserId,
          [userId]: {
            userId,
            isOnline: true,
            lastSeenAt: existingPresence?.lastSeenAt ?? null,
          },
        },
      };
    });
  },

  setUserOffline: (userId: string, lastSeenAt?: string | null) => {
    set(state => ({
      presenceByUserId: {
        ...state.presenceByUserId,
        [userId]: {
          userId,
          isOnline: false,
          lastSeenAt: lastSeenAt ?? new Date().toISOString(),
        },
      },
    }));
  },

  setPresenceSnapshot: (presenceList: PresenceDto[]) => {
    set({ presenceByUserId: toPresenceByUserId(presenceList) });
  },

  getFriendByUserId: (userId: string) =>
    get().friends.find(friend => friend.user.id === userId),

  getIncomingRequestByUserId: (userId: string) =>
    get().incomingRequests.find(request => request.requester.id === userId),

  getOutgoingRequestByUserId: (userId: string) =>
    get().outgoingRequests.find(request => request.receiver.id === userId),

  isFriend: (userId: string) =>
    get().friends.some(friend => friend.user.id === userId),

  isBlockedByCurrentUser: (userId: string) =>
    get().blockedUsers.some(blockedUser => blockedUser.user.id === userId),

  getPresence: (userId: string) => get().presenceByUserId[userId],
}));

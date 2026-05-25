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
  loadFriends: (token: string) => Promise<void>;
  loadIncomingRequests: (token: string) => Promise<void>;
  loadOutgoingRequests: (token: string) => Promise<void>;
  loadBlockedUsers: (token: string) => Promise<void>;
  loadFriendsPresence: (token: string) => Promise<void>;
  loadAllRelationships: (token: string) => Promise<void>;
  sendFriendRequest: (token: string, receiverId: string) => Promise<FriendRequestDto>;
  acceptFriendRequest: (token: string, requestId: string) => Promise<FriendRequestDto>;
  declineFriendRequest: (token: string, requestId: string) => Promise<FriendRequestDto>;
  cancelFriendRequest: (token: string, requestId: string) => Promise<FriendRequestDto>;
  removeFriend: (token: string, userId: string) => Promise<void>;
  blockUser: (token: string, blockedUserId: string) => Promise<BlockedUserDto>;
  unblockUser: (token: string, userId: string) => Promise<void>;
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

  loadFriends: async (token: string) => {
    set({ isLoadingFriends: true, friendsError: null });

    try {
      const friends = await getFriends(token);
      set({ friends });
    } catch (error) {
      set({ friendsError: getErrorMessage(error, 'Khong the tai danh sach ban be.') });
      throw error;
    } finally {
      set({ isLoadingFriends: false });
    }
  },

  loadIncomingRequests: async (token: string) => {
    set({ isLoadingIncomingRequests: true, incomingRequestsError: null });

    try {
      const incomingRequests = await getIncomingFriendRequests(token);
      set({ incomingRequests });
    } catch (error) {
      set({ incomingRequestsError: getErrorMessage(error, 'Khong the tai loi moi ket ban.') });
      throw error;
    } finally {
      set({ isLoadingIncomingRequests: false });
    }
  },

  loadOutgoingRequests: async (token: string) => {
    set({ isLoadingOutgoingRequests: true, outgoingRequestsError: null });

    try {
      const outgoingRequests = await getOutgoingFriendRequests(token);
      set({ outgoingRequests });
    } catch (error) {
      set({ outgoingRequestsError: getErrorMessage(error, 'Khong the tai loi moi da gui.') });
      throw error;
    } finally {
      set({ isLoadingOutgoingRequests: false });
    }
  },

  loadBlockedUsers: async (token: string) => {
    set({ isLoadingBlockedUsers: true, blockedUsersError: null });

    try {
      const blockedUsers = await getBlockedUsers(token);
      set({ blockedUsers });
    } catch (error) {
      set({ blockedUsersError: getErrorMessage(error, 'Khong the tai danh sach da chan.') });
      throw error;
    } finally {
      set({ isLoadingBlockedUsers: false });
    }
  },

  loadFriendsPresence: async (token: string) => {
    set({ isLoadingPresence: true, presenceError: null });

    try {
      const presenceList = await getFriendsPresence(token);
      set({ presenceByUserId: toPresenceByUserId(presenceList) });
    } catch (error) {
      set({ presenceError: getErrorMessage(error, 'Khong the tai trang thai online.') });
      throw error;
    } finally {
      set({ isLoadingPresence: false });
    }
  },

  loadAllRelationships: async (token: string) => {
    await Promise.all([
      get().loadFriends(token),
      get().loadIncomingRequests(token),
      get().loadOutgoingRequests(token),
      get().loadBlockedUsers(token),
      get().loadFriendsPresence(token),
    ]);
  },

  sendFriendRequest: async (token: string, receiverId: string) => {
    const request = await createFriendRequest(token, receiverId);
    set(state => ({
      outgoingRequests: upsertById(state.outgoingRequests, request),
    }));
    return request;
  },

  acceptFriendRequest: async (token: string, requestId: string) => {
    const request = await acceptFriendRequest(token, requestId);
    set(state => ({
      incomingRequests: state.incomingRequests.filter(existing => existing.id !== requestId),
    }));

    await get().loadFriends(token);

    return request;
  },

  declineFriendRequest: async (token: string, requestId: string) => {
    const request = await declineFriendRequest(token, requestId);
    set(state => ({
      incomingRequests: state.incomingRequests.filter(existing => existing.id !== requestId),
    }));
    return request;
  },

  cancelFriendRequest: async (token: string, requestId: string) => {
    const request = await cancelFriendRequest(token, requestId);
    set(state => ({
      outgoingRequests: state.outgoingRequests.filter(existing => existing.id !== requestId),
    }));
    return request;
  },

  removeFriend: async (token: string, userId: string) => {
    await removeFriendApi(token, userId);
    set(state => ({
      friends: state.friends.filter(friend => friend.user.id !== userId),
      presenceByUserId: removePresenceByUserId(state.presenceByUserId, userId),
    }));
  },

  blockUser: async (token: string, blockedUserId: string) => {
    const blockedUser = await blockUserApi(token, blockedUserId);
    set(state => ({
      friends: state.friends.filter(friend => friend.user.id !== blockedUserId),
      incomingRequests: state.incomingRequests.filter(request => !requestIncludesUser(request, blockedUserId)),
      outgoingRequests: state.outgoingRequests.filter(request => !requestIncludesUser(request, blockedUserId)),
      blockedUsers: upsertBlockedUser(state.blockedUsers, blockedUser),
      presenceByUserId: removePresenceByUserId(state.presenceByUserId, blockedUserId),
    }));
    return blockedUser;
  },

  unblockUser: async (token: string, userId: string) => {
    await unblockUserApi(token, userId);
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

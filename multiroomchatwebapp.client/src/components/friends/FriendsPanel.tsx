import { useEffect, useMemo, useRef, useState } from 'react';
import { createAuthClient } from '../../api/apiClient';
import { useAuth } from '../../context/AuthContext';
import { useUserRelationshipsStore } from '../../store/useUserRelationshipsStore';
import { UserActionMenu } from '../user/UserActionMenu';
import type { UserSearchResult } from '../../types/chat';
import type {
  BlockedUserDto,
  FriendDto,
  FriendRequestDto,
  UserRelationshipProfileDto,
} from '../../types/userRelationships';
import styles from './FriendsPanel.module.css';

type FriendsTab = 'friends' | 'pending' | 'incoming' | 'blocked';

const tabs: Array<{ id: FriendsTab; label: string }> = [
  { id: 'friends', label: 'Friends' },
  { id: 'pending', label: 'Pending' },
  { id: 'incoming', label: 'Incoming' },
  { id: 'blocked', label: 'Blocked' },
];

const formatDate = (value?: string | null): string => {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';

  return new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
  }).format(date);
};

const getInitial = (user: Pick<UserRelationshipProfileDto, 'displayName' | 'username'>): string =>
  (user.displayName || user.username || '?').trim().charAt(0).toUpperCase() || '?';

const ProfileAvatar = ({ user }: { user: UserRelationshipProfileDto | UserSearchResult }) => {
  const [hasError, setHasError] = useState(false);

  useEffect(() => {
    setHasError(false);
  }, [user.avatarUrl]);

  if (user.avatarUrl && !hasError) {
    return (
      <img
        className={styles.avatarImage}
        src={user.avatarUrl}
        alt=""
        referrerPolicy="no-referrer"
        onError={() => setHasError(true)}
      />
    );
  }

  return <span className={styles.avatarFallback}>{getInitial(user)}</span>;
};

const SectionState = ({ children }: { children: string }) => (
  <div className={styles.sectionState}>{children}</div>
);

export const FriendsPanel = () => {
  const { accessToken } = useAuth();
  const [activeTab, setActiveTab] = useState<FriendsTab>('friends');
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<UserSearchResult[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const searchRequestIdRef = useRef(0);

  const {
    friends,
    incomingRequests,
    outgoingRequests,
    blockedUsers,
    presenceByUserId,
    isLoadingFriends,
    isLoadingIncomingRequests,
    isLoadingOutgoingRequests,
    isLoadingBlockedUsers,
    friendsError,
    incomingRequestsError,
    outgoingRequestsError,
    blockedUsersError,
    loadFriends,
    loadIncomingRequests,
    loadOutgoingRequests,
    loadBlockedUsers,
    loadFriendsPresence,
    isBlockedByCurrentUser,
  } = useUserRelationshipsStore();

  useEffect(() => {
    if (!accessToken) return;

    void loadFriends(accessToken).catch(() => undefined);
    void loadIncomingRequests(accessToken).catch(() => undefined);
    void loadOutgoingRequests(accessToken).catch(() => undefined);
    void loadBlockedUsers(accessToken).catch(() => undefined);
    void loadFriendsPresence(accessToken).catch(() => undefined);
  }, [
    accessToken,
    loadFriends,
    loadIncomingRequests,
    loadOutgoingRequests,
    loadBlockedUsers,
    loadFriendsPresence,
  ]);

  useEffect(() => {
    const keyword = searchQuery.trim();
    searchRequestIdRef.current += 1;
    const requestId = searchRequestIdRef.current;

    if (!accessToken || keyword.length < 2) {
      setSearchResults([]);
      setIsSearching(false);
      setSearchError(null);
      return;
    }

    setIsSearching(true);
    setSearchError(null);

    const timer = window.setTimeout(async () => {
      try {
        const client = createAuthClient(accessToken);
        const response = await client.get<UserSearchResult[]>(
          `/api/v1/users/search?keyword=${encodeURIComponent(keyword)}`,
        );

        if (searchRequestIdRef.current !== requestId) return;

        setSearchResults(response.data.filter(user => !isBlockedByCurrentUser(user.id)));
      } catch {
        if (searchRequestIdRef.current !== requestId) return;
        setSearchResults([]);
        setSearchError('Khong the tim kiem nguoi dung.');
      } finally {
        if (searchRequestIdRef.current === requestId) {
          setIsSearching(false);
        }
      }
    }, 300);

    return () => window.clearTimeout(timer);
  }, [accessToken, isBlockedByCurrentUser, searchQuery]);

  const counts = useMemo(
    () => ({
      friends: friends.length,
      pending: outgoingRequests.length,
      incoming: incomingRequests.length,
      blocked: blockedUsers.length,
    }),
    [blockedUsers.length, friends.length, incomingRequests.length, outgoingRequests.length],
  );

  const renderPresence = (userId: string) => {
    const presence = presenceByUserId[userId];
    if (!presence) return null;

    if (presence.isOnline) {
      return (
        <span className={styles.presenceMeta}>
          <span className={styles.onlineDot} aria-hidden="true" />
          Online
        </span>
      );
    }

    if (!presence.lastSeenAt) return null;

    return (
      <span className={styles.presenceMeta}>
        <span className={styles.offlineDot} aria-hidden="true" />
        Last seen {formatDate(presence.lastSeenAt)}
      </span>
    );
  };

  const renderProfileRow = (
    user: UserRelationshipProfileDto | UserSearchResult,
    meta?: string,
    presenceUserId?: string,
    onBlocked?: (targetUserId: string) => void,
  ) => (
    <div className={styles.profileRow} key={`${user.id}:${meta ?? ''}`}>
      <div className={styles.avatar}>
        <ProfileAvatar user={user} />
      </div>
      <div className={styles.profileInfo}>
        <span className={styles.displayName}>{user.displayName}</span>
        {user.username && <span className={styles.username}>@{user.username}</span>}
        {meta && <span className={styles.meta}>{meta}</span>}
        {presenceUserId && renderPresence(presenceUserId)}
      </div>
      <UserActionMenu target={user} onBlocked={onBlocked} />
    </div>
  );

  const renderFriends = () => (
    <PanelSection isLoading={isLoadingFriends} error={friendsError} empty={friends.length === 0} emptyText="No friends yet.">
      {friends.map((friend: FriendDto) =>
        renderProfileRow(
          friend.user,
          friend.friendsSince ? `Friends since ${formatDate(friend.friendsSince)}` : undefined,
          friend.user.id,
        ),
      )}
    </PanelSection>
  );

  const renderPending = () => (
    <div className={styles.stackedContent}>
      <section className={styles.searchSection}>
        <label className={styles.searchLabel} htmlFor="friends-search">
          Search users
        </label>
        <input
          id="friends-search"
          className={styles.searchInput}
          type="text"
          value={searchQuery}
          onChange={event => setSearchQuery(event.target.value)}
          placeholder="Search by display name or username"
          autoComplete="off"
        />

        <div className={styles.searchResults}>
          {searchQuery.trim().length === 1 && (
            <SectionState>Type at least 2 characters to search.</SectionState>
          )}
          {isSearching && <SectionState>Searching...</SectionState>}
          {searchError && <SectionState>{searchError}</SectionState>}
          {!isSearching && !searchError && searchQuery.trim().length >= 2 && searchResults.length === 0 && (
            <SectionState>No users found.</SectionState>
          )}
          {!isSearching && searchResults.map(user =>
            renderProfileRow(user, undefined, undefined, targetUserId => {
              setSearchResults(results => results.filter(result => result.id !== targetUserId));
            }),
          )}
        </div>
      </section>

      <section className={styles.listSection}>
        <h3 className={styles.sectionTitle}>Sent requests</h3>
        <PanelSection
          isLoading={isLoadingOutgoingRequests}
          error={outgoingRequestsError}
          empty={outgoingRequests.length === 0}
          emptyText="No sent requests."
        >
          {outgoingRequests.map((request: FriendRequestDto) =>
            renderProfileRow(
              request.receiver,
              request.createdAt ? `Sent ${formatDate(request.createdAt)}` : undefined,
            ),
          )}
        </PanelSection>
      </section>
    </div>
  );

  const renderIncoming = () => (
    <PanelSection
      isLoading={isLoadingIncomingRequests}
      error={incomingRequestsError}
      empty={incomingRequests.length === 0}
      emptyText="No incoming requests."
    >
      {incomingRequests.map((request: FriendRequestDto) =>
        renderProfileRow(
          request.requester,
          request.createdAt ? `Received ${formatDate(request.createdAt)}` : undefined,
        ),
      )}
    </PanelSection>
  );

  const renderBlocked = () => (
    <PanelSection
      isLoading={isLoadingBlockedUsers}
      error={blockedUsersError}
      empty={blockedUsers.length === 0}
      emptyText="No blocked users."
    >
      {blockedUsers.map((blockedUser: BlockedUserDto) =>
        renderProfileRow(
          blockedUser.user,
          blockedUser.blockedAt ? `Blocked ${formatDate(blockedUser.blockedAt)}` : undefined,
        ),
      )}
    </PanelSection>
  );

  return (
    <section className={styles.container} aria-label="Friends">
      <header className={styles.header}>
        <div>
          <h2 className={styles.title}>Friends</h2>
          <p className={styles.subtitle}>Manage friend lists, requests, and blocked users.</p>
        </div>
      </header>

      <div className={styles.content}>
        <div className={styles.tabRow} role="tablist" aria-label="Friends sections">
          {tabs.map(tab => (
            <button
              key={tab.id}
              className={`${styles.tabButton} ${activeTab === tab.id ? styles.activeTab : ''}`}
              type="button"
              role="tab"
              aria-selected={activeTab === tab.id}
              onClick={() => setActiveTab(tab.id)}
            >
              <span>{tab.label}</span>
              <span className={styles.countBadge}>{counts[tab.id]}</span>
            </button>
          ))}
        </div>

        <div className={styles.panelBody} role="tabpanel">
          {activeTab === 'friends' && renderFriends()}
          {activeTab === 'pending' && renderPending()}
          {activeTab === 'incoming' && renderIncoming()}
          {activeTab === 'blocked' && renderBlocked()}
        </div>
      </div>
    </section>
  );
};

const PanelSection = ({
  children,
  isLoading,
  error,
  empty,
  emptyText,
}: {
  children: React.ReactNode;
  isLoading: boolean;
  error: string | null;
  empty: boolean;
  emptyText: string;
}) => {
  if (isLoading) return <SectionState>Loading...</SectionState>;
  if (error) return <SectionState>{error}</SectionState>;
  if (empty) return <SectionState>{emptyText}</SectionState>;

  return <div className={styles.rowList}>{children}</div>;
};

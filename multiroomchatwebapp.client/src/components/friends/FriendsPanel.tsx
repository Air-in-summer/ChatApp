import { useEffect, useMemo, useRef, useState } from 'react';
import { apiClient } from '../../api/apiClient';
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
  { id: 'friends', label: 'Bạn bè' },
  { id: 'pending', label: 'Đã gửi' },
  { id: 'incoming', label: 'Đã nhận' },
  { id: 'blocked', label: 'Đã chặn' },
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
  const { isAuthenticated } = useAuth();
  const [activeTab, setActiveTab] = useState<FriendsTab>('friends');
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
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
    if (!isAuthenticated) return;

    void loadFriends().catch(() => undefined);
    void loadIncomingRequests().catch(() => undefined);
    void loadOutgoingRequests().catch(() => undefined);
    void loadBlockedUsers().catch(() => undefined);
    void loadFriendsPresence().catch(() => undefined);
  }, [
    isAuthenticated,
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

    if (!isAuthenticated || keyword.length < 2) {
      setSearchResults([]);
      setIsSearching(false);
      setSearchError(null);
      return;
    }

    setIsSearching(true);
    setSearchError(null);

    const timer = window.setTimeout(async () => {
      try {
        const response = await apiClient.get<UserSearchResult[]>(
          `/api/v1/users/search?keyword=${encodeURIComponent(keyword)}`,
        );

        if (searchRequestIdRef.current !== requestId) return;

        setSearchResults(response.data.filter(user => !isBlockedByCurrentUser(user.id)));
      } catch {
        if (searchRequestIdRef.current !== requestId) return;
        setSearchResults([]);
        setSearchError('Không thể tìm kiếm người dùng.');
      } finally {
        if (searchRequestIdRef.current === requestId) {
          setIsSearching(false);
        }
      }
    }, 300);

    return () => window.clearTimeout(timer);
  }, [isAuthenticated, isBlockedByCurrentUser, searchQuery]);

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
          Đang online
        </span>
      );
    }

    if (!presence.lastSeenAt) return null;

    return (
      <span className={styles.presenceMeta}>
        <span className={styles.offlineDot} aria-hidden="true" />
        Hoạt động lần cuối {formatDate(presence.lastSeenAt)}
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
      <div className={styles.rowActions}>
        <UserActionMenu target={user} onBlocked={onBlocked} />
      </div>
    </div>
  );

  const renderFriends = () => (
    <PanelSection isLoading={isLoadingFriends} error={friendsError} empty={friends.length === 0} emptyText="Chưa có bạn bè.">
      {friends.map((friend: FriendDto) =>
        renderProfileRow(
          friend.user,
          friend.friendsSince ? `Bạn bè từ ${formatDate(friend.friendsSince)}` : undefined,
          friend.user.id,
        ),
      )}
    </PanelSection>
  );

  const renderPending = () => (
    <div className={styles.stackedContent}>
      <section className={styles.searchSection}>
        <label className={styles.searchLabel} htmlFor="friends-search">
          Tìm người dùng
        </label>
        <input
          id="friends-search"
          className={styles.searchInput}
          type="text"
          value={searchQuery}
          onChange={event => setSearchQuery(event.target.value)}
          placeholder="Tìm theo tên hiển thị hoặc username"
          autoComplete="off"
        />

        <div className={styles.searchResults}>
          {searchQuery.trim().length === 1 && (
            <SectionState>Nhập ít nhất 2 ký tự để tìm kiếm.</SectionState>
          )}
          {isSearching && <SectionState>Đang tìm kiếm...</SectionState>}
          {searchError && <SectionState>{searchError}</SectionState>}
          {!isSearching && !searchError && searchQuery.trim().length >= 2 && searchResults.length === 0 && (
            <SectionState>Không tìm thấy người dùng.</SectionState>
          )}
          {!isSearching && searchResults.map(user =>
            renderProfileRow(user, undefined, undefined, targetUserId => {
              setSearchResults(results => results.filter(result => result.id !== targetUserId));
            }),
          )}
        </div>
      </section>

      <section className={styles.listSection}>
        <h3 className={styles.sectionTitle}>Lời mời đã gửi</h3>
        <PanelSection
          isLoading={isLoadingOutgoingRequests}
          error={outgoingRequestsError}
          empty={outgoingRequests.length === 0}
          emptyText="Chưa có lời mời đã gửi."
        >
          {outgoingRequests.map((request: FriendRequestDto) =>
            renderProfileRow(
              request.receiver,
              request.createdAt ? `Đã gửi ${formatDate(request.createdAt)}` : undefined,
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
      emptyText="Chưa có lời mời đã nhận."
    >
      {incomingRequests.map((request: FriendRequestDto) =>
        renderProfileRow(
          request.requester,
          request.createdAt ? `Đã nhận ${formatDate(request.createdAt)}` : undefined,
        ),
      )}
    </PanelSection>
  );

  const renderBlocked = () => (
    <PanelSection
      isLoading={isLoadingBlockedUsers}
      error={blockedUsersError}
      empty={blockedUsers.length === 0}
      emptyText="Chưa chặn người dùng nào."
    >
      {blockedUsers.map((blockedUser: BlockedUserDto) =>
        renderProfileRow(
          blockedUser.user,
          blockedUser.blockedAt ? `Đã chặn ${formatDate(blockedUser.blockedAt)}` : undefined,
        ),
      )}
    </PanelSection>
  );

  return (
    <section className={styles.container} aria-label="Bạn bè">
      <header className={styles.header}>
        <div className={styles.headerInner}>
          <h2 className={styles.title}>Bạn bè</h2>
          <p className={styles.subtitle}>Quản lý danh sách bạn bè, lời mời và người dùng đã chặn.</p>
        </div>
      </header>

      <div className={styles.content}>
        <div className={styles.tabRow} role="tablist" aria-label="Các mục bạn bè">
          {tabs.map((tab, index) => (
            <button
              ref={(element) => {
                tabRefs.current[index] = element;
              }}
              key={tab.id}
              id={`friends-tab-${tab.id}`}
              className={`${styles.tabButton} ${activeTab === tab.id ? styles.activeTab : ''}`}
              type="button"
              role="tab"
              aria-selected={activeTab === tab.id}
              aria-controls={`friends-panel-${tab.id}`}
              tabIndex={activeTab === tab.id ? 0 : -1}
              onClick={() => setActiveTab(tab.id)}
              onKeyDown={(event) => {
                if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
                event.preventDefault();

                const currentIndex = tabs.findIndex(currentTab => currentTab.id === activeTab);
                const nextIndex = event.key === 'Home'
                  ? 0
                  : event.key === 'End'
                    ? tabs.length - 1
                    : event.key === 'ArrowRight'
                      ? (currentIndex + 1) % tabs.length
                      : (currentIndex - 1 + tabs.length) % tabs.length;

                setActiveTab(tabs[nextIndex].id);
                window.requestAnimationFrame(() => tabRefs.current[nextIndex]?.focus());
              }}
            >
              <span>{tab.label}</span>
              <span className={styles.countBadge}>{counts[tab.id]}</span>
            </button>
          ))}
        </div>

        <div
          id={`friends-panel-${activeTab}`}
          className={styles.panelBody}
          role="tabpanel"
          aria-labelledby={`friends-tab-${activeTab}`}
          tabIndex={0}
        >
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
  if (isLoading) return <SectionState>Đang tải...</SectionState>;
  if (error) return <SectionState>{error}</SectionState>;
  if (empty) return <SectionState>{emptyText}</SectionState>;

  return <div className={styles.rowList}>{children}</div>;
};

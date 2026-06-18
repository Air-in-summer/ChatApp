import { useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { useAuth } from '../../context/AuthContext';
import { useUserRelationshipsStore } from '../../store/useUserRelationshipsStore';
import { ConfirmDialog } from '../ui/ConfirmDialog/ConfirmDialog';
import styles from './UserActionMenu.module.css';

export interface UserActionTarget {
  id: string;
  username?: string | null;
  displayName: string;
  avatarUrl?: string | null;
}

export interface UserActionMenuExtraAction {
  key: string;
  label: string;
  loadingLabel?: string;
  successMessage?: string;
  variant?: 'default' | 'danger';
  disabled?: boolean;
  onSelect: () => Promise<boolean | void> | boolean | void;
}

interface UserActionMenuProps {
  target: UserActionTarget;
  align?: 'left' | 'right';
  hideMessageAction?: boolean;
  onMessage?: (target: UserActionTarget) => void;
  onBlocked?: (targetUserId: string) => void;
  blockWarningMessage?: string;
  extraActions?: UserActionMenuExtraAction[];
}

const RELATIONSHIP_ACTION_ERROR = 'Có lỗi xảy ra, vui lòng thử lại sau.';

type PendingConfirmAction =
  | { type: 'block'; message: string }
  | { type: 'remove-friend'; userId: string; displayName: string };

export const UserActionMenu = ({
  target,
  align = 'right',
  hideMessageAction = false,
  onMessage,
  onBlocked,
  blockWarningMessage,
  extraActions = [],
}: UserActionMenuProps) => {
  const { isAuthenticated } = useAuth();
  const [isOpen, setIsOpen] = useState(false);
  const [actionInFlightByKey, setActionInFlightByKey] = useState<Record<string, boolean>>({});
  const [pendingConfirmAction, setPendingConfirmAction] = useState<PendingConfirmAction | null>(null);
  const actionInFlightRef = useRef<Record<string, boolean>>({});

  const {
    getFriendByUserId,
    getIncomingRequestByUserId,
    getOutgoingRequestByUserId,
    isBlockedByCurrentUser,
    sendFriendRequest,
    cancelFriendRequest,
    acceptFriendRequest,
    declineFriendRequest,
    removeFriend,
    blockUser,
    unblockUser,
  } = useUserRelationshipsStore();

  const friend = getFriendByUserId(target.id);
  const incomingRequest = getIncomingRequestByUserId(target.id);
  const outgoingRequest = getOutgoingRequestByUserId(target.id);
  const isBlocked = isBlockedByCurrentUser(target.id);

  const runAction = async (
    actionKey: string,
    action: () => Promise<void>,
    successMessage: string,
  ) => {
    if (!isAuthenticated || actionInFlightRef.current[actionKey]) return;

    actionInFlightRef.current[actionKey] = true;
    setActionInFlightByKey(state => ({ ...state, [actionKey]: true }));

    try {
      await action();
      toast.success(successMessage);
      setIsOpen(false);
    } catch {
      toast.error(RELATIONSHIP_ACTION_ERROR);
    } finally {
      setActionInFlightByKey(state => {
        const nextState = { ...state };
        delete nextState[actionKey];
        return nextState;
      });
      delete actionInFlightRef.current[actionKey];
    }
  };

  const isActionInFlight = (actionKey: string): boolean => Boolean(actionInFlightByKey[actionKey]);

  const handleExtraAction = (extraAction: UserActionMenuExtraAction) => {
    const actionKey = `extra:${extraAction.key}`;
    if (!isAuthenticated || actionInFlightRef.current[actionKey]) return;

    actionInFlightRef.current[actionKey] = true;
    setActionInFlightByKey(state => ({ ...state, [actionKey]: true }));

    void (async () => {
      try {
        const shouldNotify = await extraAction.onSelect();
        if (shouldNotify === false) return;

        toast.success(extraAction.successMessage ?? 'Đã cập nhật.');
        setIsOpen(false);
      } catch {
        toast.error(RELATIONSHIP_ACTION_ERROR);
      } finally {
        setActionInFlightByKey(state => {
          const nextState = { ...state };
          delete nextState[actionKey];
          return nextState;
        });
        delete actionInFlightRef.current[actionKey];
      }
    })();
  };

  const handleBlock = () => {
    const message = blockWarningMessage
      ? `Chặn ${target.displayName}?\n\n${blockWarningMessage}`
      : `Chặn ${target.displayName}?`;
    setPendingConfirmAction({ type: 'block', message });
  };

  const runBlockAction = () => {
    void runAction(
      `block:${target.id}`,
      async () => {
        await blockUser(target.id);
        onBlocked?.(target.id);
      },
      'Đã chặn người dùng.',
    );
    setPendingConfirmAction(null);
  };

  const handleRemoveFriend = () => {
    if (!friend) return;
    setPendingConfirmAction({
      type: 'remove-friend',
      userId: friend.user.id,
      displayName: friend.user.displayName,
    });
  };

  const runRemoveFriendAction = (userId: string) => {
    void runAction(
      `remove:${userId}`,
      async () => {
        await removeFriend(userId);
      },
      'Đã xóa bạn bè.',
    );
    setPendingConfirmAction(null);
  };

  const handleConfirmPendingAction = () => {
    if (!pendingConfirmAction) return;

    if (pendingConfirmAction.type === 'block') {
      runBlockAction();
      return;
    }

    runRemoveFriendAction(pendingConfirmAction.userId);
  };

  const renderRelationshipActions = () => {
    if (isBlocked) {
      return (
        <button
          className={styles.menuItem}
          type="button"
          disabled={!isAuthenticated || isActionInFlight(`unblock:${target.id}`)}
          onClick={() => void runAction(
            `unblock:${target.id}`,
            async () => {
              await unblockUser(target.id);
            },
            'Đã bỏ chặn người dùng.',
          )}
        >
          {isActionInFlight(`unblock:${target.id}`) ? 'Đang bỏ chặn...' : 'Bỏ chặn'}
        </button>
      );
    }

    if (friend) {
      return (
        <>
          <button
            className={styles.menuItem}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`remove:${friend.user.id}`)}
            onClick={handleRemoveFriend}
          >
            {isActionInFlight(`remove:${friend.user.id}`) ? 'Đang xóa...' : 'Xóa bạn bè'}
          </button>
          <button
            className={`${styles.menuItem} ${styles.dangerItem}`}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`block:${target.id}`)}
            onClick={handleBlock}
          >
            {isActionInFlight(`block:${target.id}`) ? 'Đang chặn...' : 'Chặn'}
          </button>
        </>
      );
    }

    if (outgoingRequest) {
      return (
        <>
          <button
            className={styles.menuItem}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`cancel:${outgoingRequest.id}`)}
            onClick={() => void runAction(
              `cancel:${outgoingRequest.id}`,
              async () => {
                await cancelFriendRequest(outgoingRequest.id);
              },
              'Đã hủy lời mời kết bạn.',
            )}
          >
            {isActionInFlight(`cancel:${outgoingRequest.id}`) ? 'Đang hủy...' : 'Hủy lời mời'}
          </button>
          <button
            className={`${styles.menuItem} ${styles.dangerItem}`}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`block:${target.id}`)}
            onClick={handleBlock}
          >
            {isActionInFlight(`block:${target.id}`) ? 'Đang chặn...' : 'Chặn'}
          </button>
        </>
      );
    }

    if (incomingRequest) {
      return (
        <>
          <button
            className={styles.menuItem}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`accept:${incomingRequest.id}`)}
            onClick={() => void runAction(
              `accept:${incomingRequest.id}`,
              async () => {
                await acceptFriendRequest(incomingRequest.id);
              },
              'Đã chấp nhận lời mời kết bạn.',
            )}
          >
            {isActionInFlight(`accept:${incomingRequest.id}`) ? 'Đang chấp nhận...' : 'Chấp nhận'}
          </button>
          <button
            className={styles.menuItem}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`decline:${incomingRequest.id}`)}
            onClick={() => void runAction(
              `decline:${incomingRequest.id}`,
              async () => {
                await declineFriendRequest(incomingRequest.id);
              },
              'Đã từ chối lời mời kết bạn.',
            )}
          >
            {isActionInFlight(`decline:${incomingRequest.id}`) ? 'Đang từ chối...' : 'Từ chối'}
          </button>
          <button
            className={`${styles.menuItem} ${styles.dangerItem}`}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`block:${target.id}`)}
            onClick={handleBlock}
          >
            {isActionInFlight(`block:${target.id}`) ? 'Đang chặn...' : 'Chặn'}
          </button>
        </>
      );
    }

    return (
      <>
        <button
          className={styles.menuItem}
          type="button"
          disabled={!isAuthenticated || isActionInFlight(`send:${target.id}`)}
          onClick={() => void runAction(
            `send:${target.id}`,
            async () => {
              await sendFriendRequest(target.id);
            },
            'Đã gửi lời mời kết bạn.',
          )}
        >
          {isActionInFlight(`send:${target.id}`) ? 'Đang gửi...' : 'Thêm bạn'}
        </button>
        <button
          className={`${styles.menuItem} ${styles.dangerItem}`}
          type="button"
          disabled={!isAuthenticated || isActionInFlight(`block:${target.id}`)}
          onClick={handleBlock}
        >
          {isActionInFlight(`block:${target.id}`) ? 'Đang chặn...' : 'Chặn'}
        </button>
      </>
    );
  };

  const pendingConfirmLoading = pendingConfirmAction?.type === 'block'
    ? isActionInFlight(`block:${target.id}`)
    : pendingConfirmAction?.type === 'remove-friend'
      ? isActionInFlight(`remove:${pendingConfirmAction.userId}`)
      : false;
  const pendingConfirmTitle = pendingConfirmAction?.type === 'block'
    ? 'Chặn người dùng'
    : 'Xóa bạn bè';
  const pendingConfirmMessage = pendingConfirmAction?.type === 'block'
    ? pendingConfirmAction.message
    : pendingConfirmAction
      ? `Xóa ${pendingConfirmAction.displayName} khỏi danh sách bạn bè?`
      : '';
  const pendingConfirmLabel = pendingConfirmAction?.type === 'block'
    ? 'Chặn'
    : 'Xóa bạn bè';

  return (
    <>
      <div className={styles.menuRoot}>
        <button
          className={styles.trigger}
          type="button"
          onClick={() => setIsOpen(open => !open)}
          aria-haspopup="menu"
          aria-expanded={isOpen}
          title="Thao tác với người dùng"
        >
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <circle cx="12" cy="12" r="1" />
            <circle cx="19" cy="12" r="1" />
            <circle cx="5" cy="12" r="1" />
          </svg>
        </button>

        {isOpen && (
          <div className={`${styles.menu} ${align === 'left' ? styles.alignLeft : styles.alignRight}`} role="menu">
            {!hideMessageAction && onMessage && !isBlocked && (
              <button className={styles.menuItem} type="button" onClick={() => onMessage(target)}>
                Nhắn tin
              </button>
            )}
            {renderRelationshipActions()}
            {extraActions.map(extraAction => (
              <button
                key={extraAction.key}
                className={`${styles.menuItem} ${extraAction.variant === 'danger' ? styles.dangerItem : ''}`}
                type="button"
                disabled={
                  !isAuthenticated
                  || extraAction.disabled
                  || isActionInFlight(`extra:${extraAction.key}`)
                }
                onClick={() => handleExtraAction(extraAction)}
              >
                {isActionInFlight(`extra:${extraAction.key}`)
                  ? extraAction.loadingLabel ?? 'Đang xử lý...'
                  : extraAction.label}
              </button>
            ))}
          </div>
        )}
      </div>

      <ConfirmDialog
        open={Boolean(pendingConfirmAction)}
        title={pendingConfirmTitle}
        message={pendingConfirmMessage}
        confirmLabel={pendingConfirmLabel}
        cancelLabel="Hủy"
        variant="danger"
        loading={pendingConfirmLoading}
        onCancel={() => {
          if (pendingConfirmLoading) return;
          setPendingConfirmAction(null);
        }}
        onConfirm={handleConfirmPendingAction}
      />
    </>
  );
};

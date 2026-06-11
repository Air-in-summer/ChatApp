import { useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { useAuth } from '../../context/AuthContext';
import { useUserRelationshipsStore } from '../../store/useUserRelationshipsStore';
import styles from './UserActionMenu.module.css';

export interface UserActionTarget {
  id: string;
  username?: string | null;
  displayName: string;
  avatarUrl?: string | null;
}

interface UserActionMenuProps {
  target: UserActionTarget;
  align?: 'left' | 'right';
  hideMessageAction?: boolean;
  onMessage?: (target: UserActionTarget) => void;
  onBlocked?: (targetUserId: string) => void;
  blockWarningMessage?: string;
}

const RELATIONSHIP_ACTION_ERROR = 'Có lỗi xảy ra, vui lòng thử lại sau.';

export const UserActionMenu = ({
  target,
  align = 'right',
  hideMessageAction = false,
  onMessage,
  onBlocked,
  blockWarningMessage,
}: UserActionMenuProps) => {
  const { isAuthenticated } = useAuth();
  const [isOpen, setIsOpen] = useState(false);
  const [actionInFlightByKey, setActionInFlightByKey] = useState<Record<string, boolean>>({});
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

  const handleBlock = () => {
    const message = blockWarningMessage
      ? `Block ${target.displayName}?\n\n${blockWarningMessage}`
      : `Block ${target.displayName}?`;
    if (!window.confirm(message)) return;

    void runAction(
      `block:${target.id}`,
      async () => {
        await blockUser(target.id);
        onBlocked?.(target.id);
      },
      'User blocked.',
    );
  };

  const handleRemoveFriend = () => {
    if (!friend) return;
    if (!window.confirm(`Remove ${friend.user.displayName} from friends?`)) return;

    void runAction(
      `remove:${friend.user.id}`,
      async () => {
        await removeFriend(friend.user.id);
      },
      'Friend removed.',
    );
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
            'User unblocked.',
          )}
        >
          {isActionInFlight(`unblock:${target.id}`) ? 'Unblocking...' : 'Unblock'}
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
            {isActionInFlight(`remove:${friend.user.id}`) ? 'Removing...' : 'Remove friend'}
          </button>
          <button
            className={`${styles.menuItem} ${styles.dangerItem}`}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`block:${target.id}`)}
            onClick={handleBlock}
          >
            {isActionInFlight(`block:${target.id}`) ? 'Blocking...' : 'Block'}
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
              'Friend request canceled.',
            )}
          >
            {isActionInFlight(`cancel:${outgoingRequest.id}`) ? 'Canceling...' : 'Cancel request'}
          </button>
          <button
            className={`${styles.menuItem} ${styles.dangerItem}`}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`block:${target.id}`)}
            onClick={handleBlock}
          >
            {isActionInFlight(`block:${target.id}`) ? 'Blocking...' : 'Block'}
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
              'Friend request accepted.',
            )}
          >
            {isActionInFlight(`accept:${incomingRequest.id}`) ? 'Accepting...' : 'Accept'}
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
              'Friend request declined.',
            )}
          >
            {isActionInFlight(`decline:${incomingRequest.id}`) ? 'Declining...' : 'Decline'}
          </button>
          <button
            className={`${styles.menuItem} ${styles.dangerItem}`}
            type="button"
            disabled={!isAuthenticated || isActionInFlight(`block:${target.id}`)}
            onClick={handleBlock}
          >
            {isActionInFlight(`block:${target.id}`) ? 'Blocking...' : 'Block'}
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
            'Friend request sent.',
          )}
        >
          {isActionInFlight(`send:${target.id}`) ? 'Sending...' : 'Add friend'}
        </button>
        <button
          className={`${styles.menuItem} ${styles.dangerItem}`}
          type="button"
          disabled={!isAuthenticated || isActionInFlight(`block:${target.id}`)}
          onClick={handleBlock}
        >
          {isActionInFlight(`block:${target.id}`) ? 'Blocking...' : 'Block'}
        </button>
      </>
    );
  };

  return (
    <div className={styles.menuRoot}>
      <button
        className={styles.trigger}
        type="button"
        onClick={() => setIsOpen(open => !open)}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        title="User actions"
      >
        <span aria-hidden="true">...</span>
      </button>

      {isOpen && (
        <div className={`${styles.menu} ${align === 'left' ? styles.alignLeft : styles.alignRight}`} role="menu">
          {!hideMessageAction && onMessage && !isBlocked && (
            <button className={styles.menuItem} type="button" onClick={() => onMessage(target)}>
              Message
            </button>
          )}
          {renderRelationshipActions()}
        </div>
      )}
    </div>
  );
};

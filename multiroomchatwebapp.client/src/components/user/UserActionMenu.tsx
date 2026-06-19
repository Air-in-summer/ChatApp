import { useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { useAuth } from '../../context/AuthContext';
import { useUserRelationshipsStore } from '../../store/useUserRelationshipsStore';
import { ActionMenu, type ActionMenuItem } from '../ui/ActionMenu/ActionMenu';
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

  const buildRelationshipItems = (): ActionMenuItem[] => {
    if (isBlocked) {
      return [{
        id: `unblock:${target.id}`,
        label: isActionInFlight(`unblock:${target.id}`) ? 'Đang bỏ chặn...' : 'Bỏ chặn',
        disabled: !isAuthenticated,
        loading: isActionInFlight(`unblock:${target.id}`),
        onSelect: () => runAction(
            `unblock:${target.id}`,
            async () => {
              await unblockUser(target.id);
            },
            'Đã bỏ chặn người dùng.',
        ),
      }];
    }

    if (friend) {
      return [
        {
          id: `remove:${friend.user.id}`,
          label: isActionInFlight(`remove:${friend.user.id}`) ? 'Đang xóa...' : 'Xóa bạn bè',
          disabled: !isAuthenticated,
          loading: isActionInFlight(`remove:${friend.user.id}`),
          onSelect: handleRemoveFriend,
        },
        {
          id: `block:${target.id}`,
          label: isActionInFlight(`block:${target.id}`) ? 'Đang chặn...' : 'Chặn',
          variant: 'danger',
          disabled: !isAuthenticated,
          loading: isActionInFlight(`block:${target.id}`),
          onSelect: handleBlock,
        },
      ];
    }

    if (outgoingRequest) {
      return [
        {
          id: `cancel:${outgoingRequest.id}`,
          label: isActionInFlight(`cancel:${outgoingRequest.id}`) ? 'Đang hủy...' : 'Hủy lời mời',
          disabled: !isAuthenticated,
          loading: isActionInFlight(`cancel:${outgoingRequest.id}`),
          onSelect: () => runAction(
              `cancel:${outgoingRequest.id}`,
              async () => {
                await cancelFriendRequest(outgoingRequest.id);
              },
              'Đã hủy lời mời kết bạn.',
          ),
        },
        {
          id: `block:${target.id}`,
          label: isActionInFlight(`block:${target.id}`) ? 'Đang chặn...' : 'Chặn',
          variant: 'danger',
          disabled: !isAuthenticated,
          loading: isActionInFlight(`block:${target.id}`),
          onSelect: handleBlock,
        },
      ];
    }

    if (incomingRequest) {
      return [
        {
          id: `accept:${incomingRequest.id}`,
          label: isActionInFlight(`accept:${incomingRequest.id}`) ? 'Đang chấp nhận...' : 'Chấp nhận',
          disabled: !isAuthenticated,
          loading: isActionInFlight(`accept:${incomingRequest.id}`),
          onSelect: () => runAction(
              `accept:${incomingRequest.id}`,
              async () => {
                await acceptFriendRequest(incomingRequest.id);
              },
              'Đã chấp nhận lời mời kết bạn.',
          ),
        },
        {
          id: `decline:${incomingRequest.id}`,
          label: isActionInFlight(`decline:${incomingRequest.id}`) ? 'Đang từ chối...' : 'Từ chối',
          disabled: !isAuthenticated,
          loading: isActionInFlight(`decline:${incomingRequest.id}`),
          onSelect: () => runAction(
              `decline:${incomingRequest.id}`,
              async () => {
                await declineFriendRequest(incomingRequest.id);
              },
              'Đã từ chối lời mời kết bạn.',
          ),
        },
        {
          id: `block:${target.id}`,
          label: isActionInFlight(`block:${target.id}`) ? 'Đang chặn...' : 'Chặn',
          variant: 'danger',
          disabled: !isAuthenticated,
          loading: isActionInFlight(`block:${target.id}`),
          onSelect: handleBlock,
        },
      ];
    }

    return [
      {
        id: `send:${target.id}`,
        label: isActionInFlight(`send:${target.id}`) ? 'Đang gửi...' : 'Thêm bạn',
        disabled: !isAuthenticated,
        loading: isActionInFlight(`send:${target.id}`),
        onSelect: () => runAction(
            `send:${target.id}`,
            async () => {
              await sendFriendRequest(target.id);
            },
            'Đã gửi lời mời kết bạn.',
        ),
      },
      {
        id: `block:${target.id}`,
        label: isActionInFlight(`block:${target.id}`) ? 'Đang chặn...' : 'Chặn',
        variant: 'danger',
        disabled: !isAuthenticated,
        loading: isActionInFlight(`block:${target.id}`),
        onSelect: handleBlock,
      },
    ];
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
  const menuItems: ActionMenuItem[] = [
    ...(!hideMessageAction && onMessage && !isBlocked
      ? [{
          id: `message:${target.id}`,
          label: 'Nhắn tin',
          onSelect: () => onMessage(target),
        }]
      : []),
    ...buildRelationshipItems(),
    ...extraActions.map((extraAction): ActionMenuItem => ({
      id: `extra:${extraAction.key}`,
      label: isActionInFlight(`extra:${extraAction.key}`)
        ? extraAction.loadingLabel ?? 'Đang xử lý...'
        : extraAction.label,
      variant: extraAction.variant,
      disabled: !isAuthenticated || extraAction.disabled,
      loading: isActionInFlight(`extra:${extraAction.key}`),
      onSelect: () => handleExtraAction(extraAction),
    })),
  ];

  return (
    <>
      <ActionMenu
        triggerLabel={`Thao tác với ${target.displayName}`}
        items={menuItems}
        align={align === 'left' ? 'start' : 'end'}
        className={styles.menuRoot}
        menuClassName={styles.menu}
      />

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

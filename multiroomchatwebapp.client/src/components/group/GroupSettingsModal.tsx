import { useState, useEffect, useMemo, useRef, type ChangeEvent, type FormEvent } from 'react';
import toast from 'react-hot-toast';
import { useAuth } from '../../context/AuthContext';
import {
  deleteGroup,
  getGroupMembers,
  kickMember,
  leaveGroup,
  transferGroupOwnership,
  updateGroup,
  updateGroupMemberRole,
} from '../../api/groupApi';
import { uploadGroupIcon } from '../../api/mediaApi';
import { useUserRelationshipsStore } from '../../store/useUserRelationshipsStore';
import { ConfirmDialog } from '../ui/ConfirmDialog/ConfirmDialog';
import { UserActionMenu, type UserActionMenuExtraAction } from '../user/UserActionMenu';
import type { GroupDto, GroupMemberDto, GroupRole } from '../../types/group';
import styles from './GroupSettingsModal.module.css';

interface GroupSettingsModalProps {
  /** Thông tin nhóm hiện tại */
  group: GroupDto;
  /** Đóng modal */
  onClose: () => void;
  /** Callback gọi khi user rời nhóm hoặc giải tán thành công */
  onLeaveSuccess?: () => void;
  onGroupUpdated?: (group: GroupDto) => void;
}

type GroupSettingsConfirmAction =
  | { type: 'kick'; userId: string; displayName: string }
  | { type: 'leave-group' }
  | { type: 'delete-group' }
  | { type: 'promote-admin'; userId: string; displayName: string }
  | { type: 'demote-member'; userId: string; displayName: string }
  | { type: 'transfer-owner'; userId: string; displayName: string };

/**
 * Modal cấu hình và quản trị nhóm.
 * Hỗ trợ phân tách các Tab chức năng (Tổng quan, Thành viên, Bảo mật).
 * 
 * @param group - Dữ liệu nhóm
 * @param onClose - Hàm đóng modal
 * 
 * @remarks
 * Bước 14.3: Tab Tổng quan và hiển thị Mã mời (Invite Code).
 * Bước 14.4: Tab Thành viên - Hiển thị danh sách thành viên.
 * Bước 14.5: Chức năng Trục xuất (Kick) thành viên - Phân quyền Owner/Admin.
 * Bước 14.6: Chức năng Rời Nhóm.
 * Bước 14.7: Chức năng giải tán nhóm - Chỉ dành cho Owner.
 */
export const GroupSettingsModal = ({ group, onClose, onLeaveSuccess, onGroupUpdated }: GroupSettingsModalProps) => {
  const { isAuthenticated, user } = useAuth();
  const [activeTab, setActiveTab] = useState<'overview' | 'members'>('overview');
  const settingsTabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const [members, setMembers] = useState<GroupMemberDto[]>([]);
  const [isLoadingMembers, setIsLoadingMembers] = useState(false);
  const [copied, setCopied] = useState(false);
  const inviteUrl = `${window.location.origin}/join/${group.inviteCode}`;
  const [editName, setEditName] = useState(group.name);
  const [editDescription, setEditDescription] = useState(group.description || '');
  const [editIconUrl, setEditIconUrl] = useState(group.iconUrl || '');
  const [selectedIconFile, setSelectedIconFile] = useState<File | null>(null);
  const [localIconPreviewUrl, setLocalIconPreviewUrl] = useState<string | null>(null);
  const [hasIconPreviewError, setHasIconPreviewError] = useState(false);
  const [overviewNameError, setOverviewNameError] = useState<string | null>(null);
  const [isSavingOverview, setIsSavingOverview] = useState(false);
  const [pendingConfirmAction, setPendingConfirmAction] = useState<GroupSettingsConfirmAction | null>(null);
  const [isConfirmingAction, setIsConfirmingAction] = useState(false);
  const friends = useUserRelationshipsStore(state => state.friends);
  const blockedUsers = useUserRelationshipsStore(state => state.blockedUsers);
  const presenceByUserId = useUserRelationshipsStore(state => state.presenceByUserId);
  const loadFriends = useUserRelationshipsStore(state => state.loadFriends);
  const loadBlockedUsers = useUserRelationshipsStore(state => state.loadBlockedUsers);
  const loadFriendsPresence = useUserRelationshipsStore(state => state.loadFriendsPresence);

  // [Luồng 14.4: Tải danh sách thành viên]
  const fetchMembers = async () => {
    if (!isAuthenticated) return;
    setIsLoadingMembers(true);
    try {
      const data = await getGroupMembers(group.id);
      setMembers(data);
    } catch (error) {
      console.error('Failed to fetch members:', error);
    } finally {
      setIsLoadingMembers(false);
    }
  };

  useEffect(() => {
    if (isAuthenticated) {
      fetchMembers();
      void loadFriends().catch(() => undefined);
      void loadBlockedUsers().catch(() => undefined);
      void loadFriendsPresence().catch(() => undefined);
    }
  }, [isAuthenticated, group.id, loadFriends, loadBlockedUsers, loadFriendsPresence]);

  useEffect(() => {
    setEditName(group.name);
    setEditDescription(group.description || '');
    setEditIconUrl(group.iconUrl || '');
    setSelectedIconFile(null);
    setLocalIconPreviewUrl(null);
    setHasIconPreviewError(false);
    setOverviewNameError(null);
  }, [group.id, group.name, group.description, group.iconUrl]);

  useEffect(() => {
    if (!selectedIconFile) {
      setLocalIconPreviewUrl(null);
      return;
    }

    const previewUrl = URL.createObjectURL(selectedIconFile);
    setLocalIconPreviewUrl(previewUrl);
    setHasIconPreviewError(false);

    return () => URL.revokeObjectURL(previewUrl);
  }, [selectedIconFile]);

  const iconPreviewUrl = localIconPreviewUrl ?? editIconUrl;

  /** Xác định vai trò của người dùng hiện tại trong nhóm */
  const currentUserRole = useMemo(() => {
    if (!user) return 'Member';
    // [Ưu tiên]: Check Owner nhanh qua ownerId có sẵn trong GroupDto
    if (user.userId === group.ownerId) return 'Owner';
    
    const me = members.find(m => m.profile.id === user.userId);
    return me?.role || 'Member';
  }, [members, user, group.ownerId]);

  const handleSaveOverview = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!isAuthenticated || currentUserRole !== 'Owner') return;

    const normalizedName = editName.trim();
    if (!normalizedName) {
      setOverviewNameError('Tên nhóm không được bỏ trống.');
      return;
    }

    setOverviewNameError(null);
    setIsSavingOverview(true);
    try {
      let nextIconUrl = editIconUrl;
      if (selectedIconFile) {
        const iconGroup = await uploadGroupIcon(group.id, selectedIconFile);
        nextIconUrl = iconGroup.iconUrl || '';
        setEditIconUrl(nextIconUrl);
        setSelectedIconFile(null);
      }

      const updatedGroup = await updateGroup(group.id, {
        name: normalizedName,
        description: editDescription,
        iconUrl: nextIconUrl,
      });
      onGroupUpdated?.(updatedGroup);
      toast.success('Đã cập nhật thông tin nhóm.');
    } catch (error: any) {
      const errorMsg = error.response?.data?.detail || 'Không thể cập nhật thông tin nhóm.';
      toast.error(errorMsg);
      console.error('Update group failed:', error);
    } finally {
      setIsSavingOverview(false);
    }
  };

  const handleIconFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0] ?? null;
    setSelectedIconFile(file);
    event.target.value = '';
  };

  /** Xử lý copy mã mời vào clipboard */
  const handleCopyInvite = () => {
    navigator.clipboard.writeText(inviteUrl);
    setCopied(true);
    // Reset trạng thái nút sau 2 giây
    setTimeout(() => setCopied(false), 2000);
  };

  /** [Luồng 14.5]: Trục xuất thành viên */
  const handleKick = (targetUserId: string, targetName: string) => {
    if (!isAuthenticated) return;
    setPendingConfirmAction({
      type: 'kick',
      userId: targetUserId,
      displayName: targetName || 'thành viên này',
    });
  };

  /** [Luồng 14.6]: Rời khỏi nhóm */
  const handleLeaveGroup = () => {
    if (!isAuthenticated) return;

    // Ràng buộc Owner không được rời nhóm (phải transfer hoặc delete)
    if (currentUserRole === 'Owner') {
      toast.error('Chủ sở hữu không thể rời nhóm. Vui lòng chuyển nhượng quyền sở hữu hoặc giải tán nhóm.');
      return;
    }

    setPendingConfirmAction({ type: 'leave-group' });
  };

  /** [Luồng 14.7]: Giải tán nhóm */
  const handleDeleteGroup = () => {
    if (!isAuthenticated) return;
    setPendingConfirmAction({ type: 'delete-group' });
  };

  const handleConfirmGroupSettingsAction = async () => {
    if (!pendingConfirmAction || !isAuthenticated || isConfirmingAction) return;

    setIsConfirmingAction(true);
    try {
      switch (pendingConfirmAction.type) {
        case 'kick':
          await kickMember(group.id, pendingConfirmAction.userId);
          await fetchMembers();
          toast.success(`Đã trục xuất ${pendingConfirmAction.displayName}.`);
          break;

        case 'leave-group':
          await leaveGroup(group.id);
          toast.success('Đã rời khỏi nhóm.');
          onClose();
          onLeaveSuccess?.();
          break;

        case 'delete-group':
          await deleteGroup(group.id);
          toast.success('Đã giải tán nhóm.');
          onClose();
          onLeaveSuccess?.();
          break;

        case 'promote-admin':
          await updateGroupMemberRole(group.id, pendingConfirmAction.userId, 'Admin');
          updateMemberRoleInState(pendingConfirmAction.userId, 'Admin');
          toast.success('Đã bổ nhiệm Admin.');
          break;

        case 'demote-member':
          await updateGroupMemberRole(group.id, pendingConfirmAction.userId, 'Member');
          updateMemberRoleInState(pendingConfirmAction.userId, 'Member');
          toast.success('Đã hạ xuống Member.');
          break;

        case 'transfer-owner':
          await transferGroupOwnership(group.id, pendingConfirmAction.userId);
          setMembers(currentMembers => currentMembers.map(currentMember => {
            if (currentMember.profile.id === pendingConfirmAction.userId) {
              return { ...currentMember, role: 'Owner' };
            }

            if (currentMember.profile.id === user?.userId) {
              return { ...currentMember, role: 'Admin' };
            }

            return currentMember;
          }));
          onGroupUpdated?.({ ...group, ownerId: pendingConfirmAction.userId });
          toast.success('Đã trao quyền Owner.');
          break;
      }

      setPendingConfirmAction(null);
    } catch (error: any) {
      const fallbackMessageByType: Record<GroupSettingsConfirmAction['type'], string> = {
        kick: 'Không thể trục xuất thành viên. Vui lòng thử lại sau.',
        'leave-group': 'Không thể rời khỏi nhóm. Vui lòng thử lại sau.',
        'delete-group': 'Không thể giải tán nhóm. Vui lòng thử lại sau.',
        'promote-admin': 'Không thể bổ nhiệm Admin. Vui lòng thử lại sau.',
        'demote-member': 'Không thể hạ xuống Member. Vui lòng thử lại sau.',
        'transfer-owner': 'Không thể trao quyền Owner. Vui lòng thử lại sau.',
      };
      const errorMsg = error.response?.data?.detail || fallbackMessageByType[pendingConfirmAction.type];
      toast.error(errorMsg);
      console.error('Group settings action failed:', error);
    } finally {
      setIsConfirmingAction(false);
    }
  };

  /** Trả về class CSS tương ứng với vai trò */
  const getRoleBadgeClass = (role: string) => {
    switch (role) {
      case 'Owner': return styles.roleOwner;
      case 'Admin': return styles.roleAdmin;
      default: return styles.roleMember;
    }
  };

  /** Kiểm tra xem User hiện tại có quyền kick mục tiêu hay không */
  const canKickUser = (targetMember: GroupMemberDto) => {
    if (!user || targetMember.profile.id === user.userId) return false;
    
    if (currentUserRole === 'Owner') return true;
    if (currentUserRole === 'Admin' && targetMember.role === 'Member') return true;
    
    return false;
  };

  const updateMemberRoleInState = (targetUserId: string, nextRole: GroupRole) => {
    setMembers(currentMembers => currentMembers.map(member => (
      member.profile.id === targetUserId
        ? { ...member, role: nextRole }
        : member
    )));
  };

  const getGroupAdminActions = (member: GroupMemberDto): UserActionMenuExtraAction[] => {
    if (currentUserRole !== 'Owner' || !user || member.profile.id === user.userId) {
      return [];
    }

    const displayName = member.profile.displayName || 'thành viên này';
    const actions: UserActionMenuExtraAction[] = [];

    if (member.role === 'Member') {
      actions.push({
        key: `promote-admin:${member.profile.id}`,
        label: 'Bổ nhiệm Admin',
        loadingLabel: 'Đang bổ nhiệm...',
        successMessage: 'Đã bổ nhiệm Admin.',
        onSelect: () => {
          setPendingConfirmAction({
            type: 'promote-admin',
            userId: member.profile.id,
            displayName,
          });
          return false;
        },
      });
    }

    if (member.role === 'Admin') {
      actions.push({
        key: `demote-member:${member.profile.id}`,
        label: 'Hạ xuống Member',
        loadingLabel: 'Đang hạ cấp...',
        successMessage: 'Đã hạ xuống Member.',
        onSelect: () => {
          setPendingConfirmAction({
            type: 'demote-member',
            userId: member.profile.id,
            displayName,
          });
          return false;
        },
      });
    }

    actions.push({
      key: `transfer-owner:${member.profile.id}`,
      label: 'Trao quyền Owner',
      loadingLabel: 'Đang trao quyền...',
      successMessage: 'Đã trao quyền Owner.',
      variant: 'danger',
      onSelect: () => {
        setPendingConfirmAction({
          type: 'transfer-owner',
          userId: member.profile.id,
          displayName,
        });
        return false;
      },
    });

    return actions;
  };

  const canSeeMemberPresence = (targetUserId: string) => {
    if (targetUserId === user?.userId) return false;
    if (!friends.some(friend => friend.user.id === targetUserId)) return false;
    if (blockedUsers.some(blockedUser => blockedUser.user.id === targetUserId)) return false;
    return true;
  };

  const renderPresence = (targetUserId: string) => {
    const placeholder = (
      <div className={`${styles.memberPresence} ${styles.memberPresencePlaceholder}`} aria-hidden="true">
        <span className={styles.offlineDot} />
        Offline
      </div>
    );

    if (!canSeeMemberPresence(targetUserId)) return placeholder;

    const presence = presenceByUserId[targetUserId];
    if (!presence) return placeholder;

    return (
      <div className={styles.memberPresence}>
        <span className={presence.isOnline ? styles.onlineDot : styles.offlineDot} aria-hidden="true" />
        {presence.isOnline ? 'Online' : 'Offline'}
      </div>
    );
  };

  const confirmDialogConfig = (() => {
    if (!pendingConfirmAction) {
      return {
        title: '',
        message: '',
        confirmLabel: 'Xác nhận',
        variant: 'default' as const,
      };
    }

    switch (pendingConfirmAction.type) {
      case 'kick':
        return {
          title: 'Trục xuất thành viên',
          message: `Trục xuất "${pendingConfirmAction.displayName}" khỏi nhóm?`,
          confirmLabel: 'Trục xuất',
          variant: 'danger' as const,
        };

      case 'leave-group':
        return {
          title: 'Rời khỏi nhóm',
          message: `Rời khỏi nhóm "${group.name}"? Bạn sẽ không còn thấy các phòng và tin nhắn mới trong nhóm này.`,
          confirmLabel: 'Rời khỏi nhóm',
          variant: 'danger' as const,
        };

      case 'delete-group':
        return {
          title: 'Giải tán nhóm',
          message: (
            <>
              <p>Giải tán nhóm "{group.name}"?</p>
              <p>Hành động này không thể hoàn tác.</p>
            </>
          ),
          confirmLabel: 'Giải tán nhóm',
          variant: 'danger' as const,
        };

      case 'promote-admin':
        return {
          title: 'Bổ nhiệm Admin',
          message: `Bổ nhiệm ${pendingConfirmAction.displayName} làm Admin?`,
          confirmLabel: 'Bổ nhiệm',
          variant: 'default' as const,
        };

      case 'demote-member':
        return {
          title: 'Hạ xuống Member',
          message: `Hạ ${pendingConfirmAction.displayName} xuống Member?`,
          confirmLabel: 'Hạ cấp',
          variant: 'default' as const,
        };

      case 'transfer-owner':
        return {
          title: 'Trao quyền Owner',
          message: (
            <>
              <p>Trao quyền Owner cho {pendingConfirmAction.displayName}?</p>
              <p>Bạn sẽ tự động xuống Admin sau khi trao quyền.</p>
            </>
          ),
          confirmLabel: 'Trao quyền',
          variant: 'danger' as const,
        };
    }
  })();

  return (
    <>
    <div className={styles.overlay} onClick={onClose}>
      {/* Ngăn sự kiện click lan tỏa ra overlay làm đóng modal nhầm */}
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        
        {/* Sidebar điều hướng Tab */}
        <div className={styles.sidebar} role="tablist" aria-label="Mục cài đặt nhóm">
          <div className={styles.sidebarTitle}>{group.name}</div>
          <button 
            ref={(element) => {
              settingsTabRefs.current[0] = element;
            }}
            id="group-settings-tab-overview"
            type="button"
            role="tab"
            className={`${styles.tabItem} ${activeTab === 'overview' ? styles.active : ''}`}
            aria-selected={activeTab === 'overview'}
            aria-controls="group-settings-panel-overview"
            tabIndex={activeTab === 'overview' ? 0 : -1}
            onClick={() => setActiveTab('overview')}
            onKeyDown={(event) => {
              if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
              event.preventDefault();
              const nextIndex = event.key === 'ArrowDown' || event.key === 'End' ? 1 : 0;
              setActiveTab(nextIndex === 0 ? 'overview' : 'members');
              window.requestAnimationFrame(() => settingsTabRefs.current[nextIndex]?.focus());
            }}
          >
            Tổng quan
          </button>
          <button 
            ref={(element) => {
              settingsTabRefs.current[1] = element;
            }}
            id="group-settings-tab-members"
            type="button"
            role="tab"
            className={`${styles.tabItem} ${activeTab === 'members' ? styles.active : ''}`}
            aria-selected={activeTab === 'members'}
            aria-controls="group-settings-panel-members"
            tabIndex={activeTab === 'members' ? 0 : -1}
            onClick={() => setActiveTab('members')}
            onKeyDown={(event) => {
              if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
              event.preventDefault();
              const nextIndex = event.key === 'ArrowUp' || event.key === 'Home' ? 0 : 1;
              setActiveTab(nextIndex === 0 ? 'overview' : 'members');
              window.requestAnimationFrame(() => settingsTabRefs.current[nextIndex]?.focus());
            }}
          >
            Thành viên
          </button>

          <div className={styles.flexGrow}></div>

          {/* Nút giải tán nhóm (Bước 14.7 - Chỉ Owner) */}
          {currentUserRole === 'Owner' && (
            <button className={styles.deleteBtn} onClick={handleDeleteGroup}>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M3 6h18" />
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                <line x1="10" y1="11" x2="10" y2="17" />
                <line x1="14" y1="11" x2="14" y2="17" />
              </svg>
              Giải tán nhóm
            </button>
          )}

          {/* Nút rời nhóm (Bước 14.6) */}
          <button className={styles.leaveBtn} onClick={handleLeaveGroup}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <polyline points="16 17 21 12 16 7" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
            Rời khỏi nhóm
          </button>
        </div>

        {/* Nội dung chính dựa trên Tab đang chọn */}
        <div className={styles.contentArea}>
          <button className={styles.closeBtn} onClick={onClose} title="Đóng">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>

          {activeTab === 'overview' && (
            <div
              id="group-settings-panel-overview"
              className={styles.section}
              role="tabpanel"
              aria-labelledby="group-settings-tab-overview"
            >
              <h2 className={styles.sectionTitle}>Tổng quan nhóm</h2>
              
              <div className={styles.groupInfo}>
                <label className={`${styles.icon} ${currentUserRole === 'Owner' ? styles.editableIcon : ''}`}>
                  {iconPreviewUrl && !hasIconPreviewError ? (
                    <img
                      src={iconPreviewUrl}
                      alt={group.name}
                      onError={() => setHasIconPreviewError(true)}
                    />
                  ) : (
                    group.name.substring(0, 2).toUpperCase()
                  )}
                  {currentUserRole === 'Owner' && (
                    <>
                      <span className={styles.iconEditOverlay}>Chỉnh sửa ảnh</span>
                      <input
                        type="file"
                        accept=".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp"
                        onChange={handleIconFileChange}
                      />
                    </>
                  )}
                </label>
                <div className={styles.groupDetails}>
                  <h3>{group.name}</h3>
                  <p>{group.description || 'Không có mô tả.'}</p>
                </div>
              </div>

              {currentUserRole === 'Owner' && (
                <form className={styles.editForm} onSubmit={handleSaveOverview}>
                  <label className={styles.formField}>
                    <span>Tên nhóm</span>
                    <input
                      value={editName}
                      maxLength={100}
                      onChange={(event) => {
                        setEditName(event.target.value);
                        if (overviewNameError) setOverviewNameError(null);
                      }}
                    />
                    {overviewNameError && (
                      <span className={styles.formError}>{overviewNameError}</span>
                    )}
                  </label>
                  <label className={styles.formField}>
                    <span>Mô tả</span>
                    <textarea
                      value={editDescription}
                      maxLength={255}
                      rows={3}
                      onChange={(event) => setEditDescription(event.target.value)}
                    />
                  </label>
                  <button
                    type="submit"
                    className={styles.saveBtn}
                    disabled={isSavingOverview}
                  >
                    {isSavingOverview ? 'Đang lưu...' : 'Lưu thay đổi'}
                  </button>
                </form>
              )}

              <div style={{ marginTop: 'var(--spacing-xl)' }}>
                <div className={styles.inviteCard}>
                  <div className={styles.inviteHeader}>
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                      <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
                      <circle cx="9" cy="7" r="4" />
                      <line x1="19" y1="8" x2="19" y2="14" />
                      <line x1="16" y1="11" x2="22" y2="11" />
                    </svg>
                    Mời bạn bè tham gia
                  </div>
                  <p className={styles.inviteDesc}>
                    Chia sẻ mã mời này với người khác để họ có thể tham gia vào nhóm của bạn.
                  </p>
                  <div className={styles.inviteBox}>
                    <div className={styles.inviteCode}>{inviteUrl}</div>
                    <button className={styles.copyBtn} onClick={handleCopyInvite}>
                      {copied ? 'Đã chép!' : 'Sao chép'}
                    </button>
                  </div>
                </div>
              </div>
            </div>
          )}

          {activeTab === 'members' && (
            <div
              id="group-settings-panel-members"
              className={styles.section}
              role="tabpanel"
              aria-labelledby="group-settings-tab-members"
            >
              <h2 className={styles.sectionTitle}>Quản lý thành viên ({members.length})</h2>
              
              {isLoadingMembers ? (
                <div className={styles.loading}>Đang tải danh sách...</div>
              ) : (
                <div className={styles.memberList}>
                  {members.map((member, index) => (
                    <div key={`${member.profile.id}-${index}`} className={styles.memberItem}>
                      <div className={styles.memberAvatar}>
                        {member.profile.avatarUrl ? (
                          <img src={member.profile.avatarUrl} alt={member.profile.displayName} />
                        ) : (
                          (member.profile.displayName || '?')[0].toUpperCase()
                        )}
                      </div>
                      <div className={styles.memberInfo}>
                        <div className={styles.memberDisplayName}>
                          {member.profile.displayName || 'Vô danh'}
                          {user?.userId === member.profile.id && ' (Bạn)'}
                        </div>
                        <div className={`${styles.memberRoleTag} ${getRoleBadgeClass(member.role)}`}>
                          {member.role}
                        </div>
                        {renderPresence(member.profile.id)}
                      </div>

                      {/* Nút Kick - Chỉ hiện nếu có quyền */}
                      <div className={styles.memberActions}>
                      {user?.userId !== member.profile.id && (
                        <UserActionMenu
                          target={member.profile}
                          blockWarningMessage="Bạn vẫn có thể dùng chung không gian nhóm với người này. Tin nhắn nhóm chưa được ẩn trong giai đoạn này."
                          extraActions={getGroupAdminActions(member)}
                        />
                      )}

                      <div className={styles.memberKickSlot}>
                      {canKickUser(member) && (
                        <button 
                          className={styles.kickBtn}
                          onClick={() => handleKick(member.profile.id, member.profile.displayName)}
                          title="Trục xuất khỏi nhóm"
                        >
                          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
                            <circle cx="9" cy="7" r="4" />
                            <line x1="18" y1="8" x2="23" y2="13" />
                            <line x1="23" y1="8" x2="18" y2="13" />
                          </svg>
                        </button>
                      )}
                      </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
    <ConfirmDialog
      open={Boolean(pendingConfirmAction)}
      title={confirmDialogConfig.title}
      message={confirmDialogConfig.message}
      confirmLabel={confirmDialogConfig.confirmLabel}
      cancelLabel="Hủy"
      variant={confirmDialogConfig.variant}
      loading={isConfirmingAction}
      onCancel={() => {
        if (isConfirmingAction) return;
        setPendingConfirmAction(null);
      }}
      onConfirm={handleConfirmGroupSettingsAction}
    />
    </>
  );
};

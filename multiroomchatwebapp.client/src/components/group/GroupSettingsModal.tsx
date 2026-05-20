import { useState, useEffect, useMemo } from 'react';
import { useAuth } from '../../context/AuthContext';
import { deleteGroup, getGroupMembers, kickMember, leaveGroup } from '../../api/groupApi';
import type { GroupDto, GroupMemberDto } from '../../types/group';
import styles from './GroupSettingsModal.module.css';

interface GroupSettingsModalProps {
  /** Thông tin Server hiện tại */
  group: GroupDto;
  /** Đóng modal */
  onClose: () => void;
  /** Callback gọi khi user rời nhóm hoặc giải tán thành công */
  onLeaveSuccess?: () => void;
}

/**
 * Modal cấu hình và quản trị Server.
 * Hỗ trợ phân tách các Tab chức năng (Tổng quan, Thành viên, Bảo mật).
 * 
 * @param group - Dữ liệu Server
 * @param onClose - Hàm đóng modal
 * 
 * @remarks
 * Bước 14.3: Tab Tổng quan và hiển thị Mã mời (Invite Code).
 * Bước 14.4: Tab Thành viên - Hiển thị danh sách thành viên.
 * Bước 14.5: Chức năng Trục xuất (Kick) thành viên - Phân quyền Owner/Admin.
 * Bước 14.6: Chức năng Rời Nhóm.
 * Bước 14.7: Chức năng Giải tán (Xóa) Server - Chỉ dành cho Owner.
 */
export const GroupSettingsModal = ({ group, onClose, onLeaveSuccess }: GroupSettingsModalProps) => {
  const { accessToken, user } = useAuth();
  const [activeTab, setActiveTab] = useState<'overview' | 'members'>('overview');
  const [members, setMembers] = useState<GroupMemberDto[]>([]);
  const [isLoadingMembers, setIsLoadingMembers] = useState(false);
  const [copied, setCopied] = useState(false);

  // [Luồng 14.4: Tải danh sách thành viên]
  const fetchMembers = async () => {
    if (!accessToken) return;
    setIsLoadingMembers(true);
    try {
      const data = await getGroupMembers(accessToken, group.id);
      setMembers(data);
    } catch (error) {
      console.error('Failed to fetch members:', error);
    } finally {
      setIsLoadingMembers(false);
    }
  };

  useEffect(() => {
    if (accessToken) {
      fetchMembers();
    }
  }, [accessToken, group.id]);

  /** Xác định vai trò của người dùng hiện tại trong Server */
  const currentUserRole = useMemo(() => {
    if (!user) return 'Member';
    // [Ưu tiên]: Check Owner nhanh qua ownerId có sẵn trong GroupDto
    if (user.userId === group.ownerId) return 'Owner';
    
    const me = members.find(m => m.profile.id === user.userId);
    return me?.role || 'Member';
  }, [members, user, group.ownerId]);

  /** Xử lý copy mã mời vào clipboard */
  const handleCopyInvite = () => {
    navigator.clipboard.writeText(group.inviteCode);
    setCopied(true);
    // Reset trạng thái nút sau 2 giây
    setTimeout(() => setCopied(false), 2000);
  };

  /** [Luồng 14.5]: Trục xuất thành viên */
  const handleKick = async (targetUserId: string, targetName: string) => {
    if (!accessToken) return;
    if (!window.confirm(`Bạn có chắc chắn muốn trục xuất "${targetName}" khỏi Server không?`)) return;

    try {
      await kickMember(accessToken, group.id, targetUserId);
      // Tải lại danh sách sau khi kick thành công
      await fetchMembers();
    } catch (error) {
      alert('Không thể trục xuất thành viên. Vui lòng thử lại sau.');
      console.error('Kick failed:', error);
    }
  };

  /** [Luồng 14.6]: Rời khỏi Server */
  const handleLeaveGroup = async () => {
    if (!accessToken) return;

    // Ràng buộc Owner không được rời nhóm (phải transfer hoặc delete)
    if (currentUserRole === 'Owner') {
      alert('Chủ sở hữu không thể rời Server. Vui lòng chuyển nhượng quyền sở hữu hoặc giải tán Server.');
      return;
    }

    if (!window.confirm(`Bạn có chắc chắn muốn rời khỏi Server "${group.name}"?`)) return;

    try {
      await leaveGroup(accessToken, group.id);
      onClose();
      if (onLeaveSuccess) onLeaveSuccess();
    } catch (error: any) {
      const errorMsg = error.response?.data?.detail || 'Không thể rời khỏi Server. Vui lòng thử lại sau.';
      alert(errorMsg);
      console.error('Leave failed:', error);
    }
  };

  /** [Luồng 14.7]: Giải tán Server */
  const handleDeleteGroup = async () => {
    if (!accessToken) return;

    if (!window.confirm(`CẢNH BÁO: Bạn có chắc chắn muốn GIẢI TÁN Server "${group.name}" không? Hành động này không thể hoàn tác.`)) return;
    if (!window.confirm(`XÁC NHẬN CUỐI CÙNG: Toàn bộ dữ liệu Server sẽ bị xóa mềm. Bạn vẫn muốn tiếp tục?`)) return;

    try {
      await deleteGroup(accessToken, group.id);
      onClose();
      if (onLeaveSuccess) onLeaveSuccess();
    } catch (error: any) {
      const errorMsg = error.response?.data?.detail || 'Không thể giải tán Server. Vui lòng thử lại sau.';
      alert(errorMsg);
      console.error('Delete failed:', error);
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

  return (
    <div className={styles.overlay} onClick={onClose}>
      {/* Ngăn sự kiện click lan tỏa ra overlay làm đóng modal nhầm */}
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        
        {/* Sidebar điều hướng Tab */}
        <div className={styles.sidebar}>
          <div className={styles.sidebarTitle}>{group.name}</div>
          <button 
            className={`${styles.tabItem} ${activeTab === 'overview' ? styles.active : ''}`}
            onClick={() => setActiveTab('overview')}
          >
            Tổng quan
          </button>
          <button 
            className={`${styles.tabItem} ${activeTab === 'members' ? styles.active : ''}`}
            onClick={() => setActiveTab('members')}
          >
            Thành viên
          </button>

          <div className={styles.flexGrow}></div>

          {/* Nút Giải tán Server (Bước 14.7 - Chỉ Owner) */}
          {currentUserRole === 'Owner' && (
            <button className={styles.deleteBtn} onClick={handleDeleteGroup}>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M3 6h18" />
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                <line x1="10" y1="11" x2="10" y2="17" />
                <line x1="14" y1="11" x2="14" y2="17" />
              </svg>
              Giải tán Server
            </button>
          )}

          {/* Nút Rời Nhóm (Bước 14.6) */}
          <button className={styles.leaveBtn} onClick={handleLeaveGroup}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <polyline points="16 17 21 12 16 7" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
            Rời khỏi Server
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
            <div className={styles.section}>
              <h2 className={styles.sectionTitle}>Tổng quan máy chủ</h2>
              
              <div className={styles.groupInfo}>
                <div className={styles.icon}>
                  {group.iconUrl ? (
                    <img src={group.iconUrl} alt={group.name} />
                  ) : (
                    group.name.substring(0, 2).toUpperCase()
                  )}
                </div>
                <div className={styles.groupDetails}>
                  <h3>{group.name}</h3>
                  <p>{group.description || 'Không có mô tả.'}</p>
                </div>
              </div>

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
                    Chia sẻ mã mời này với người khác để họ có thể tham gia vào Server của bạn.
                  </p>
                  <div className={styles.inviteBox}>
                    <div className={styles.inviteCode}>{group.inviteCode}</div>
                    <button className={styles.copyBtn} onClick={handleCopyInvite}>
                      {copied ? 'Đã chép!' : 'Sao chép'}
                    </button>
                  </div>
                </div>
              </div>
            </div>
          )}

          {activeTab === 'members' && (
            <div className={styles.section}>
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
                      </div>

                      {/* Nút Kick - Chỉ hiện nếu có quyền */}
                      {canKickUser(member) && (
                        <button 
                          className={styles.kickBtn}
                          onClick={() => handleKick(member.profile.id, member.profile.displayName)}
                          title="Trục xuất khỏi Server"
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
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

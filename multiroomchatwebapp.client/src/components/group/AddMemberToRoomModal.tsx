import { useState, useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { getGroupMembers, getRoomMemberIds, addMembersToRoom } from '../../api/groupApi';
import type { GroupMemberDto } from '../../types/group';
import styles from './AddMemberToRoomModal.module.css';

interface AddMemberToRoomModalProps {
  groupId: string;
  roomId: string;
  roomName: string;
  onClose: () => void;
  onSuccess: (addedCount: number) => void;
}

/**
 * [Bước 7] Modal cho phép Owner/Admin thêm thành viên vào phòng Private.
 * Điểm mấu chốt: Modal này sẽ gọi đồng thời 2 API để "lọc bỏ" những thành viên đã có trong phòng, 
 * đảm bảo người dùng chỉ nhìn thấy những người có thể add (Trải nghiệm người dùng mượt mà).
 */
export const AddMemberToRoomModal = ({
  groupId,
  roomId,
  roomName,
  onClose,
  onSuccess
}: AddMemberToRoomModalProps) => {
  const { isAuthenticated } = useAuth();
  const [members, setMembers] = useState<GroupMemberDto[]>([]);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [searchQuery, setSearchQuery] = useState('');
  
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchData = async () => {
      if (!isAuthenticated) return;
      try {
        // Fetch đồng thời cả danh sách Group Member VÀ danh sách ID đã có trong phòng (Từ Redis O(1))
        const [allMembers, existingIds] = await Promise.all([
          getGroupMembers(groupId),
          getRoomMemberIds(groupId, roomId)
        ]);

        const existingSet = new Set(existingIds);
        
        // LỌC Ở UI: Chỉ giữ lại những người chưa tham gia phòng
        const availableMembers = allMembers.filter(m => !existingSet.has(m.profile.id));
        setMembers(availableMembers);
      } catch (err: any) {
        console.error("Failed to fetch members:", err);
        setError('Không thể tải danh sách thành viên. Bạn có phải là Admin/Owner không?');
      } finally {
        setIsLoading(false);
      }
    };

    fetchData();
  }, [isAuthenticated, groupId, roomId]);

  const toggleSelect = (userId: string) => {
    const newSet = new Set(selectedIds);
    if (newSet.has(userId)) {
      newSet.delete(userId);
    } else {
      newSet.add(userId);
    }
    setSelectedIds(newSet);
  };

  const handleSubmit = async () => {
    if (!isAuthenticated || selectedIds.size === 0) return;
    setIsSubmitting(true);
    setError(null);
    try {
      const response = await addMembersToRoom(groupId, roomId, Array.from(selectedIds));
      onSuccess(response.addedCount);
    } catch (err: any) {
      console.error("Add members error:", err);
      setError(err.response?.data?.detail || 'Có lỗi xảy ra khi thêm thành viên.');
      setIsSubmitting(false);
    }
  };

  // Tính năng Search ngay trong Modal
  const filteredMembers = members.filter(m => 
    m.profile.displayName.toLowerCase().includes(searchQuery.toLowerCase()) ||
    m.profile.username.toLowerCase().includes(searchQuery.toLowerCase())
  );

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={e => e.stopPropagation()}>
        <button className={styles.closeBtn} onClick={onClose} title="Đóng">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>

        <header className={styles.header}>
          <h2 className={styles.title}>Thêm Thành viên</h2>
          <p className={styles.subtitle}>vào phòng #{roomName}</p>
        </header>

        <div className={styles.searchContainer}>
          <label className={styles.visuallyHidden} htmlFor="add-member-search">
            Tìm kiếm thành viên
          </label>
          <input
            id="add-member-search"
            type="text"
            className={styles.searchInput}
            placeholder="Tìm kiếm thành viên..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            disabled={isLoading || members.length === 0}
            autoFocus
          />
        </div>

        {error && <div className={styles.errorMsg}>{error}</div>}

        <div className={styles.memberList}>
          {isLoading ? (
            <div className={styles.emptyState}>Đang tải danh sách...</div>
          ) : members.length === 0 ? (
            <div className={styles.emptyState}>Tất cả thành viên Server đều đã ở trong phòng này.</div>
          ) : filteredMembers.length === 0 ? (
            <div className={styles.emptyState}>Không tìm thấy thành viên nào phù hợp.</div>
          ) : (
            filteredMembers.map(member => (
              <label
                key={member.profile.id} 
                className={styles.memberItem}
              >
                <input
                  className={styles.visuallyHidden}
                  type="checkbox"
                  checked={selectedIds.has(member.profile.id)}
                  onChange={() => toggleSelect(member.profile.id)}
                  disabled={isSubmitting}
                />
                <div className={styles.avatar}>
                  {member.profile.displayName.charAt(0).toUpperCase()}
                </div>
                <div className={styles.memberInfo}>
                  <span className={styles.memberName}>{member.profile.displayName}</span>
                </div>
                <div className={styles.checkboxContainer}>
                  <div className={`${styles.checkbox} ${selectedIds.has(member.profile.id) ? styles.checked : ''}`}>
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3">
                      <polyline points="20 6 9 17 4 12"></polyline>
                    </svg>
                  </div>
                </div>
              </label>
            ))
          )}
        </div>

        <div className={styles.footer}>
          <button className={styles.cancelBtn} onClick={onClose} disabled={isSubmitting}>
            Hủy
          </button>
          <button 
            className={styles.submitBtn} 
            onClick={handleSubmit} 
            disabled={isSubmitting || selectedIds.size === 0 || isLoading}
          >
            {isSubmitting ? (
              <><div className={styles.spinner}></div>Đang xử lý...</>
            ) : (
              `Thêm ${selectedIds.size > 0 ? selectedIds.size : ''} thành viên`
            )}
          </button>
        </div>
      </div>
    </div>
  );
};

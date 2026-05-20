import { useState, useEffect } from 'react';
import toast from 'react-hot-toast';
import { useAuth } from '../../context/AuthContext';
import { createGroup, getMyGroups } from '../../api/groupApi';
import type { GroupDto } from '../../types/group';
import { CreateGroupModal } from './CreateGroupModal';
import { useNotificationStore } from '../../store/useNotificationStore';
import styles from './GroupListPanel.module.css';

interface GroupListPanelProps {
  /** Callback khi user chọn một Server từ danh sách */
  onSelectGroup: (group: GroupDto) => void;
}

/**
 * Component hiển thị danh sách các Server (Nhóm) mà người dùng đã tham gia.
 * Giao diện được thiết kế theo phong cách thẻ lưới (MS Teams style).
 * 
 * @param onSelectGroup - Hàm xử lý khi click vào một Server
 * 
 * @remarks
 * Luồng xử lý:
 * 1. Lấy accessToken từ AuthContext để xác thực API.
 * 2. Sử dụng useEffect để tự động gọi API getMyGroups khi component mount.
 * 3. [Bước 13.3]: Quản lý trạng thái hiển thị của CreateGroupModal.
 * 4. [Bước 13.4]: Thực hiện gọi API tạo Server mới và xử lý thông báo.
 * 5. Hiển thị trạng thái Loading trong khi chờ dữ liệu.
 * 6. Nếu không có dữ liệu, hiển thị giao diện Empty State hướng dẫn user.
 * 7. Nếu có dữ liệu, render danh sách dưới dạng Grid Card.
 */
export const GroupListPanel = ({ onSelectGroup }: GroupListPanelProps) => {
  const { accessToken } = useAuth();
  const [groups, setGroups] = useState<GroupDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isModalOpen, setIsModalOpen] = useState(false);

  // [Lỗi #14]: Lấy tín hiệu bị kick hoặc group bị giải tán từ notification store
  const kickedFromGroup = useNotificationStore(s => s.kickedFromGroup);
  const deletedGroup = useNotificationStore(s => s.deletedGroup);

  /**
   * [Lỗi #14 Fix]: Lắng nghe sự kiện bị kick khỏi nhóm.
   * Khi Store nhận tín hiệu kick, ta lọc bỏ nhóm đó khỏi state cục bộ ngay lập tức.
   */
  useEffect(() => {
    if (kickedFromGroup) {
      setGroups(prev => prev.filter(g => g.id !== kickedFromGroup.groupId));
    }
  }, [kickedFromGroup]);

  /**
   * [Lỗi #14 Fix]: Tương tự khi nhóm bị giải tán.
   */
  useEffect(() => {
    if (deletedGroup) {
      setGroups(prev => prev.filter(g => g.id !== deletedGroup.groupId));
    }
  }, [deletedGroup]);

  // [Luồng xử lý: Tải danh sách nhóm]
  useEffect(() => {
    // Chỉ gọi API nếu đã có token
    if (!accessToken) return;

    const fetchGroups = async () => {
      try {
        const data = await getMyGroups(accessToken);
        setGroups(data);
      } catch (error) {
        // Log lỗi nhưng vẫn tắt loading để tránh treo UI
        console.error('Failed to fetch groups:', error);
      } finally {
        setIsLoading(false);
      }
    };

    fetchGroups();
  }, [accessToken]);

  /**
   * [Bước 13.4, 13.5]: Xử lý tạo nhóm mới thông qua API.
   * Cập nhật danh sách hiển thị và điều hướng vào Server mới.
   */
  const handleCreateSubmit = async (name: string, description?: string) => {
    if (!accessToken) return;

    try {
      const newGroup = await createGroup(accessToken, { name, description });
      
      // Thành công: Thông báo và đóng modal
      toast.success(`Đã tạo Server "${name}" thành công!`);
      setIsModalOpen(false);

      // [Bước 13.5]: Cập nhật state danh sách và nhảy vào Server mới
      setGroups(prev => [newGroup, ...prev]);
      onSelectGroup(newGroup);
      
    } catch (error: any) {
      const errorMsg = error.response?.data?.detail || 'Không thể tạo Server. Vui lòng thử lại sau.';
      toast.error(errorMsg);
      // Throw error để Modal biết và tắt trạng thái Loading bên trong nó
      throw error;
    }
  };

  return (
    <div className={styles.container}>
      {/* [Bước 13.3]: Render CreateGroupModal khi state isModalOpen là true */}
      {isModalOpen && (
        <CreateGroupModal 
          onClose={() => setIsModalOpen(false)} 
          onSubmit={handleCreateSubmit}
        />
      )}

      {/* Header: Chứa tiêu đề và nút chức năng chính */}
      <div className={styles.header}>
        <h2 className={styles.title}>Nhóm của tôi</h2>
        <button className={styles.createBtn} onClick={() => setIsModalOpen(true)}>
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <line x1="12" y1="5" x2="12" y2="19" />
            <line x1="5" y1="12" x2="19" y2="12" />
          </svg>
          Tạo Nhóm
        </button>
      </div>

      {/* Nội dung chính: Danh sách Server hoặc trạng thái đặc biệt */}
      <div className={styles.content}>
        {isLoading ? (
          <div className={styles.emptyState}>Đang tải...</div>
        ) : groups.length === 0 ? (
          // Trạng thái trống: Hiện icon minh họa và hướng dẫn
          <div className={styles.emptyState}>
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
              <circle cx="9" cy="7" r="4" />
              <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
              <path d="M16 3.13a4 4 0 0 1 0 7.75" />
            </svg>
            <h3>Chưa có nhóm nào</h3>
            <p>Bạn chưa tham gia nhóm nào. Hãy tạo một nhóm mới hoặc tham gia bằng liên kết mời.</p>
          </div>
        ) : (
          // Hiển thị danh sách dạng lưới
          <div className={styles.grid}>
            {groups.map((group) => (
              <div 
                key={group.id} 
                className={styles.card}
                onClick={() => onSelectGroup(group)}
              >
                <div className={styles.icon}>
                  {/* Ưu tiên iconUrl nếu có, nếu không thì lấy 2 chữ cái đầu của tên */}
                  {group.iconUrl ? (
                    <img src={group.iconUrl} alt={group.name} className={styles.iconImage} />
                  ) : (
                    group.name.substring(0, 2).toUpperCase()
                  )}
                </div>
                <div className={styles.info}>
                  <h3 className={styles.name}>{group.name}</h3>
                  {group.description && <p className={styles.desc}>{group.description}</p>}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};


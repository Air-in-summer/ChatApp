import { useState } from 'react';
import styles from './CreateGroupModal.module.css';

interface CreateGroupModalProps {
  /** Đóng modal */
  onClose: () => void;
  /** 
   * Callback khi người dùng nhấn Submit.
   * Xử lý API sẽ được thực hiện ở component cha hoặc service.
   */
  onSubmit: (name: string, description?: string) => Promise<void>;
}

/**
 * [Bước 13.1, 13.2]: Component Modal dùng để tạo Server (Nhóm) mới.
 * Cung cấp giao diện nhập tên và mô tả Server với hiệu ứng glassmorphism.
 * 
 * @param onClose - Hàm đóng modal
 * @param onSubmit - Hàm xử lý khi gửi form
 * 
 * @remarks
 * Luồng xử lý:
 * 1. [Bước 13.2]: Hiển thị form với 2 trường: Tên (bắt buộc, max 100 ký tự) và Mô tả (tùy chọn, max 255 ký tự).
 * 2. Validate dữ liệu đầu vào (Tên không được để trống).
 * 3. [Bước 13.2]: Hiển thị trạng thái loading (Spinner) khi đang gọi API qua callback onSubmit.
 * 4. Xử lý lỗi và đóng modal khi thành công (thông qua callback).
 */
export const CreateGroupModal = ({ onClose, onSubmit }: CreateGroupModalProps) => {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // [Hệ số giới hạn - Bước 13.2]
  const MAX_NAME_LENGTH = 100;
  const MAX_DESC_LENGTH = 255;

  /**
   * Xử lý gửi form tạo Server
   */
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    // Validate cơ bản
    if (!name.trim()) {
      setError('Tên Server không được để trống');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await onSubmit(name.trim(), description.trim() || undefined);
      // Lưu ý: Việc đóng modal nên do component cha quyết định sau khi xử lý thành công
    } catch (err: any) {
      setError(err.message || 'Đã có lỗi xảy ra khi tạo Server. Vui lòng thử lại.');
      setIsSubmitting(false);
    }
  };

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        {/* Nút đóng góc trên bên phải */}
        <button className={styles.closeBtn} onClick={onClose} title="Đóng">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>

        <header className={styles.header}>
          <h2 className={styles.title}>Tạo Server của bạn</h2>
          <p className={styles.subtitle}>
            Server là nơi bạn và bạn bè cùng trò chuyện. Hãy tạo một cái và bắt đầu cuộc vui!
          </p>
        </header>

        <form className={styles.form} onSubmit={handleSubmit}>
          {/* Tên Server (Bắt buộc) - [Bước 13.2] */}
          <div className={styles.formGroup}>
            <div className={styles.labelWrapper}>
              <label className={styles.label} htmlFor="group-name">
                Tên Server <span style={{ color: 'var(--status-danger)' }}>*</span>
              </label>
              <span className={styles.charCounter}>
                {name.length}/{MAX_NAME_LENGTH}
              </span>
            </div>
            <input
              id="group-name"
              type="text"
              className={styles.input}
              placeholder="Nhập tên server của bạn..."
              value={name}
              maxLength={MAX_NAME_LENGTH}
              onChange={(e) => {
                setName(e.target.value);
                if (error) setError(null);
              }}
              autoFocus
              disabled={isSubmitting}
            />
          </div>

          {/* Mô tả Server (Tùy chọn) - [Bước 13.2] */}
          <div className={styles.formGroup}>
            <div className={styles.labelWrapper}>
              <label className={styles.label} htmlFor="group-desc">Mô tả</label>
              <span className={styles.charCounter}>
                {description.length}/{MAX_DESC_LENGTH}
              </span>
            </div>
            <textarea
              id="group-desc"
              className={styles.textarea}
              placeholder="Một vài dòng giới thiệu về server này..."
              value={description}
              maxLength={MAX_DESC_LENGTH}
              onChange={(e) => setDescription(e.target.value)}
              disabled={isSubmitting}
            />
          </div>

          {/* Hiển thị lỗi nếu có */}
          {error && (
            <div style={{ color: 'var(--status-danger)', fontSize: '0.85rem', marginBottom: 'var(--spacing-md)' }}>
              {error}
            </div>
          )}

          <div className={styles.footer}>
            <button 
              type="button" 
              className={styles.cancelBtn} 
              onClick={onClose}
              disabled={isSubmitting}
            >
              Hủy
            </button>
            <button 
              type="submit" 
              className={styles.submitBtn} 
              disabled={isSubmitting || !name.trim()}
            >
              {isSubmitting ? (
                <>
                  <div className={styles.spinner}></div>
                  Đang tạo...
                </>
              ) : (
                'Tạo Server'
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

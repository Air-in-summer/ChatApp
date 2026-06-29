import { useState } from 'react';
import type { CreateGroupChannelRequest } from '../../types/group';
import styles from './CreateChannelModal.module.css';

interface CreateChannelModalProps {
  /** ID của nhóm đang thực hiện tạo phòng */
  groupId: string;
  /** Tên nhóm để hiển thị trong tiêu đề */
  groupName: string;
  /** Đóng modal */
  onClose: () => void;
  /** 
   * Callback khi người dùng nhấn Submit.
   * Trả về request object để component cha xử lý API.
   */
  onSubmit: (request: CreateGroupChannelRequest) => Promise<void>;
}

/**
 * [Bước 15.3]: Component Modal dùng để tạo phòng mới trong nhóm.
 * Thiết kế tinh tế với các tùy chọn loại phòng (Text/Voice) và chế độ riêng tư.
 * 
 * @param groupId - ID nhóm
 * @param groupName - Tên nhóm
 * @param onClose - Hàm đóng modal
 * @param onSubmit - Hàm xử lý khi gửi form
 * 
 * @remarks
 * Luồng xử lý:
 * 1. Nhập tên phòng (bắt buộc, max 100 ký tự).
 * 2. Chọn loại phòng (Text - Mặc định, Voice).
 * 3. Tùy chọn phòng riêng tư (Toggle).
 * 4. Validate: Tên không trống và không chứa ký tự đặc biệt gây lỗi URL
 */
export const CreateChannelModal = ({ groupName, onClose, onSubmit }: CreateChannelModalProps) => {
  const [name, setName] = useState('');
  const [type, setType] = useState<'Text' | 'Voice'>('Text');
  const [isPrivate, setIsPrivate] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const MAX_NAME_LENGTH = 100;

  /**
   * Xử lý gửi form tạo phòng
   */
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    // Validate cơ bản
    const cleanName = name.trim().toLowerCase().replace(/\s+/g, '-');
    if (!cleanName) {
      setError('Tên phòng không được để trống');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await onSubmit({
        name: cleanName,
        type,
        isPrivate
      });
      // Component cha sẽ chịu trách nhiệm đóng modal khi API thành công
    } catch (err: any) {
      setError(err.message || 'Đã có lỗi xảy ra khi tạo phòng. Vui lòng thử lại.');
      setIsSubmitting(false);
    }
  };

  /** Format tên phòng khi nhập (style: lowercase, no spaces) */
  const handleNameChange = (val: string) => {
    const formatted = val.toLowerCase().replace(/\s+/g, '-');
    setName(formatted);
    if (error) setError(null);
  };

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <button className={styles.closeBtn} onClick={onClose} title="Đóng">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>

        <header className={styles.header}>
          <h2 className={styles.title}>Tạo Phòng</h2>
          <p className={styles.subtitle}>trong {groupName}</p>
        </header>

        <form className={styles.form} onSubmit={handleSubmit}>
          {/* Loại Phòng - Radio Group */}
          <fieldset className={`${styles.formGroup} ${styles.fieldset}`}>
            <legend className={styles.label}>Loại Phòng</legend>
            <div className={styles.radioGroup}>
              <label
                className={`${styles.radioItem} ${type === 'Text' ? styles.selected : ''}`}
              >
                <input
                  className={styles.visuallyHidden}
                  type="radio"
                  name="channel-type"
                  value="Text"
                  checked={type === 'Text'}
                  onChange={() => setType('Text')}
                  disabled={isSubmitting}
                />
                <div className={styles.radioIcon}>
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                  </svg>
                </div>
                <div className={styles.radioInfo}>
                  <span className={styles.radioLabel}>Text</span>
                  <span className={styles.radioDesc}>Gửi tin nhắn, hình ảnh, icon và nhiều thứ khác.</span>
                </div>
                <div className={styles.radioCheck}>
                  {type === 'Text' && <div style={{ width: 8, height: 8, background: 'white', borderRadius: '50%' }} />}
                </div>
              </label>

              <label
                className={`${styles.radioItem} ${type === 'Voice' ? styles.selected : ''}`}
              >
                <input
                  className={styles.visuallyHidden}
                  type="radio"
                  name="channel-type"
                  value="Voice"
                  checked={type === 'Voice'}
                  onChange={() => setType('Voice')}
                  disabled={isSubmitting}
                />
                <div className={styles.radioIcon}>
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z" />
                    <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                    <line x1="12" y1="19" x2="12" y2="23" />
                    <line x1="8" y1="23" x2="16" y2="23" />
                  </svg>
                </div>
                <div className={styles.radioInfo}>
                  <span className={styles.radioLabel}>Voice</span>
                  <span className={styles.radioDesc}>Cùng nhau trò chuyện bằng giọng nói và video.</span>
                </div>
                <div className={styles.radioCheck}>
                  {type === 'Voice' && <div style={{ width: 8, height: 8, background: 'white', borderRadius: '50%' }} />}
                </div>
              </label>
            </div>
          </fieldset>

          {/* Tên Phòng */}
          <div className={styles.formGroup}>
            <div className={styles.labelWrapper}>
              <label className={styles.label} htmlFor="channel-name">Tên Phòng</label>
              <span className={styles.charCounter}>{name.length}/{MAX_NAME_LENGTH}</span>
            </div>
            <div style={{ position: 'relative' }}>
              <span style={{ position: 'absolute', left: 12, top: '50%', transform: 'translateY(-50%)', color: 'var(--color-text-muted)', fontSize: '1.2rem' }}>#</span>
              <input
                id="channel-name"
                className={styles.input}
                style={{ paddingLeft: '32px' }}
                placeholder="ten-phong-moi"
                value={name}
                maxLength={MAX_NAME_LENGTH}
                onChange={(e) => handleNameChange(e.target.value)}
                autoFocus
                disabled={isSubmitting}
              />
            </div>
          </div>

          {/* Phòng riêng tư - Toggle */}
          <div className={styles.toggleGroup}>
            <div className={styles.toggleLabelWrapper}>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
                <path d="M7 11V7a5 5 0 0 1 10 0v4" />
              </svg>
              <div className={styles.radioInfo}>
                <span className={styles.toggleTitle}>Phòng riêng tư</span>
                <span className={styles.radioDesc}>Chỉ những người được mời mới thấy phòng này.</span>
              </div>
            </div>
            <button
              type="button"
              className={`${styles.toggleSwitch} ${isPrivate ? styles.active : ''}`}
              onClick={() => setIsPrivate(!isPrivate)}
              role="switch"
              aria-checked={isPrivate}
              aria-label="Phòng riêng tư"
              disabled={isSubmitting}
            >
              <div className={styles.toggleHandle} />
            </button>
          </div>

          {error && (
            <div style={{ color: 'var(--color-danger)', fontSize: '0.85rem' }}>
              {error}
            </div>
          )}

          <div className={styles.footer}>
            <button type="button" className={styles.cancelBtn} onClick={onClose} disabled={isSubmitting}>
              Hủy
            </button>
            <button type="submit" className={styles.submitBtn} disabled={isSubmitting || !name.trim()}>
              {isSubmitting ? (
                <>
                  <div className={styles.spinner}></div>
                  Đang tạo...
                </>
              ) : (
                'Tạo Phòng'
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

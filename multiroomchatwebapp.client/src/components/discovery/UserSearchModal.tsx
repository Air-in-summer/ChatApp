import { useState, useEffect, useRef } from 'react';
import { useAuth } from '../../context/AuthContext';
import { apiClient } from '../../api/apiClient';
import type { UserSearchResponse, UserSearchResult } from '../../types/chat';
import styles from './UserSearchModal.module.css';

const USER_SEARCH_PAGE_SIZE = 10;

const getPaginationItems = (currentPage: number, totalPages: number): Array<number | 'ellipsis'> => {
  if (totalPages <= 7) {
    return Array.from({ length: totalPages }, (_, index) => index + 1);
  }

  const pages = new Set([1, totalPages, currentPage - 1, currentPage, currentPage + 1]);
  const normalizedPages = Array.from(pages)
    .filter(page => page >= 1 && page <= totalPages)
    .sort((left, right) => left - right);

  return normalizedPages.flatMap((page, index) => {
    const previousPage = normalizedPages[index - 1];
    if (!previousPage || page - previousPage === 1) {
      return [page];
    }

    return ['ellipsis' as const, page];
  });
};

interface UserSearchModalProps {
  onClose: () => void;
  onSelectUser: (user: UserSearchResult) => void;
}

/**
 * Modal tìm kiếm người dùng - Hiển thị đè lên giữa màn hình.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Modal mở → focus vào ô input ngay lập tức.
 * 2. Khi user gõ ≥ 2 ký tự → debounce 300ms → gọi API /api/v1/users/search.
 * 3. Hiển thị kết quả tìm kiếm.
 * 4. User click vào một kết quả → callback onSelectUser → Modal tự đóng.
 * 5. Nhấn Escape hoặc click vào overlay → đóng Modal.
 *
 * Side effects:
 * - Khóa scroll của body khi Modal đang mở.
 */
export const UserSearchModal = ({ onClose, onSelectUser }: UserSearchModalProps) => {
  const { isAuthenticated } = useAuth();
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<UserSearchResult[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [isSearching, setIsSearching] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const searchRequestIdRef = useRef(0);

  // Focus vào input khi Modal mở
  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  // Đóng Modal khi nhấn Escape
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  // Debounce search - gọi API sau 300ms idle
  useEffect(() => {
    searchRequestIdRef.current += 1;
    const requestId = searchRequestIdRef.current;

    if (!query.trim() || query.length < 2) {
      setResults([]);
      setTotalPages(0);
      setIsSearching(false);
      return;
    }

    if (!isAuthenticated) return;

    const timer = setTimeout(async () => {
      setIsSearching(true);
      try {
        const response = await apiClient.get<UserSearchResponse>(
          `/api/v1/users/search?keyword=${encodeURIComponent(query)}&page=${page}&pageSize=${USER_SEARCH_PAGE_SIZE}`
        );

        if (searchRequestIdRef.current !== requestId) return;

        setResults(response.data.items);
        setTotalPages(response.data.totalPages);
      } catch {
        if (searchRequestIdRef.current !== requestId) return;

        setResults([]);
        setTotalPages(0);
      } finally {
        if (searchRequestIdRef.current === requestId) {
          setIsSearching(false);
        }
      }
    }, 300);

    return () => clearTimeout(timer); // Cleanup debounce
  }, [query, page, isAuthenticated]);

  const renderPagination = () => {
    if (totalPages <= 1 || query.trim().length < 2) return null;

    return (
      <nav className={styles.pagination} aria-label="Phân trang kết quả tìm kiếm">
        <button
          type="button"
          className={styles.pageButton}
          onClick={() => setPage(currentPage => Math.max(1, currentPage - 1))}
          disabled={page === 1 || isSearching}
        >
          ‹
        </button>
        {getPaginationItems(page, totalPages).map((item, index) =>
          item === 'ellipsis' ? (
            <span key={`ellipsis-${index}`} className={styles.paginationEllipsis}>…</span>
          ) : (
            <button
              key={item}
              type="button"
              className={`${styles.pageButton} ${item === page ? styles.activePageButton : ''}`}
              aria-current={item === page ? 'page' : undefined}
              onClick={() => setPage(item)}
              disabled={isSearching}
            >
              {item}
            </button>
          ),
        )}
        <button
          type="button"
          className={styles.pageButton}
          onClick={() => setPage(currentPage => Math.min(totalPages, currentPage + 1))}
          disabled={page >= totalPages || isSearching}
        >
          ›
        </button>
      </nav>
    );
  };

  return (
    <>
      {/* Overlay - click để đóng */}
      <div className={styles.overlay} onClick={onClose} />

      {/* Modal nội dung */}
      <div className={styles.modal} role="dialog" aria-modal="true" aria-label="Tìm kiếm người dùng">
        <div className={styles.searchWrapper}>
          {/* Icon search */}
          <svg className={styles.searchIcon} width="16" height="16" viewBox="0 0 24 24"
            fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="11" cy="11" r="8" />
            <line x1="21" y1="21" x2="16.65" y2="16.65" />
          </svg>

          <input
            ref={inputRef}
            className={styles.searchInput}
            type="text"
            placeholder="Tìm theo tên hoặc username..."
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setPage(1);
            }}
            autoComplete="off"
          />

          {/* Nút đóng */}
          <button className={styles.closeBtn} onClick={onClose} title="Đóng (Esc)">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none"
              stroke="currentColor" strokeWidth="2">
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </div>

        {/* Kết quả tìm kiếm */}
        <div className={styles.resultList}>
          {isSearching && (
            <div className={styles.stateMsg}>Đang tìm kiếm...</div>
          )}

          {!isSearching && query.length >= 2 && results.length === 0 && (
            <div className={styles.stateMsg}>Không tìm thấy người dùng nào.</div>
          )}

          {!isSearching && query.length < 2 && query.length > 0 && (
            <div className={styles.stateMsg}>Nhập ít nhất 2 ký tự để tìm kiếm.</div>
          )}

          {results.map(user => (
            <button
              key={user.id}
              className={styles.resultItem}
              onClick={() => onSelectUser(user)}
            >
              {/* Avatar chữ cái đầu */}
              <div className={styles.avatar}>
                {user.displayName[0].toUpperCase()}
              </div>
              <div className={styles.userInfo}>
                <span className={styles.displayName}>{user.displayName}</span>
                <span className={styles.username}>@{user.username}</span>
              </div>
            </button>
          ))}
          {!isSearching && renderPagination()}
        </div>
      </div>
    </>
  );
};

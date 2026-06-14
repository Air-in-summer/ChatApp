const DEFAULT_ALLOWED_PROTOCOLS = new Set(['http:', 'https:', 'blob:']);

/**
 * Chuẩn hóa URL trước khi gán vào href/src động.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Loại bỏ giá trị rỗng.
 * 2. Parse bằng URL API để tránh kiểm tra chuỗi thủ công.
 * 3. Chỉ cho phép các scheme an toàn cho media/link của app.
 */
export const getSafeResourceUrl = (
  value?: string | null,
  allowedProtocols: ReadonlySet<string> = DEFAULT_ALLOWED_PROTOCOLS,
): string | null => {
  if (!value) return null;

  try {
    const parsed = new URL(value, window.location.origin);
    return allowedProtocols.has(parsed.protocol) ? parsed.href : null;
  } catch {
    return null;
  }
};

const DEFAULT_RETURN_URL = '/';

/**
 * Chuẩn hóa returnUrl để chỉ cho phép điều hướng nội bộ trong SPA.
 *
 * @param value - Giá trị returnUrl đọc từ route state hoặc query string.
 * @returns Đường dẫn nội bộ an toàn, fallback về `/` nếu input không hợp lệ.
 *
 * @remarks
 * Luồng xử lý:
 * 1. Bỏ qua giá trị rỗng.
 * 2. Chỉ chấp nhận path bắt đầu bằng `/`.
 * 3. Từ chối protocol-relative URL (`//evil.com`) và backslash để tránh open redirect.
 */
export const sanitizeInternalReturnUrl = (value?: string | null): string => {
  if (!value) return DEFAULT_RETURN_URL;

  const trimmed = value.trim();
  if (!trimmed || !trimmed.startsWith('/')) {
    return DEFAULT_RETURN_URL;
  }

  if (trimmed.startsWith('//') || trimmed.includes('\\')) {
    return DEFAULT_RETURN_URL;
  }

  return trimmed;
};

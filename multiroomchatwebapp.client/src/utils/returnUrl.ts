const DEFAULT_RETURN_URL = '/';

/**
 * Chuan hoa returnUrl de chi cho phep dieu huong noi bo trong SPA.
 *
 * @param value - Gia tri returnUrl doc tu route state hoac query string.
 * @returns Duong dan noi bo an toan, fallback ve `/` neu input khong hop le.
 *
 * @remarks
 * Luong xu ly:
 * 1. Bo qua gia tri rong.
 * 2. Chi chap nhan path bat dau bang `/`.
 * 3. Tu choi protocol-relative URL (`//evil.com`) va backslash de tranh open redirect.
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

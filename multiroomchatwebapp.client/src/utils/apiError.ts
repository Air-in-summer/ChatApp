type ApiErrorResponse = {
  message?: unknown;
  detail?: unknown;
  error?: unknown;
  title?: unknown;
  errors?: unknown;
};

const normalizeMessage = (value: unknown): string | null => {
  if (typeof value !== 'string') return null;

  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
};

const getValidationErrorMessage = (errors: unknown): string | null => {
  if (Array.isArray(errors)) {
    return errors.map(normalizeMessage).find(Boolean) ?? null;
  }

  if (errors && typeof errors === 'object') {
    for (const value of Object.values(errors as Record<string, unknown>)) {
      if (Array.isArray(value)) {
        const message = value.map(normalizeMessage).find(Boolean);
        if (message) return message;
      }

      const message = normalizeMessage(value);
      if (message) return message;
    }
  }

  return null;
};

export const getApiErrorMessage = (error: unknown, fallback: string): string => {
  const data = (error as { response?: { data?: unknown } })?.response?.data;

  if (typeof data === 'string') {
    return normalizeMessage(data) ?? fallback;
  }

  if (data && typeof data === 'object') {
    const response = data as ApiErrorResponse;

    return normalizeMessage(response.message)
      ?? normalizeMessage(response.detail)
      ?? normalizeMessage(response.error)
      ?? normalizeMessage(response.title)
      ?? getValidationErrorMessage(response.errors)
      ?? fallback;
  }

  return normalizeMessage((error as { message?: unknown })?.message) ?? fallback;
};

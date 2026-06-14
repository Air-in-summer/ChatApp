import axios, {
  type AxiosError,
  type InternalAxiosRequestConfig,
} from 'axios';

declare module 'axios' {
  export interface AxiosRequestConfig {
    _skipBffUnauthorized?: boolean;
  }

  export interface InternalAxiosRequestConfig {
    _skipBffUnauthorized?: boolean;
  }
}

export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '')
  .trim()
  .replace(/\/+$/, '');

export const buildApiUrl = (path: string): string => {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  return `${API_BASE_URL}${normalizedPath}`;
};

type BffRetryConfig = InternalAxiosRequestConfig & {
  _csrfRetry?: boolean;
};

interface CsrfTokenResponse {
  token: string;
  headerName: string;
}

interface ApiErrorResponse {
  code?: string;
}

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const DEFAULT_CSRF_HEADER = 'X-CSRF-TOKEN';

let handleBffUnauthorized: (() => void) | null = null;
let csrfToken: string | null = null;
let csrfHeaderName = DEFAULT_CSRF_HEADER;
let csrfRequest: Promise<string> | null = null;

const csrfClient = axios.create({
  baseURL: API_BASE_URL,
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
});

const isUnsafeMethod = (method?: string): boolean =>
  UNSAFE_METHODS.has((method ?? '').toUpperCase());

const isCsrfFailure = (error: AxiosError<ApiErrorResponse>): boolean =>
  error.response?.status === 419 ||
  (
    error.response?.status === 403 &&
    error.response.data?.code === 'csrf_validation_failed'
  );

const loadCsrfToken = async (forceRefresh = false): Promise<string> => {
  if (!forceRefresh && csrfToken) {
    return csrfToken;
  }

  if (!forceRefresh && csrfRequest) {
    return csrfRequest;
  }

  const request = csrfClient
    .get<CsrfTokenResponse>('/api/auth/csrf')
    .then(({ data }) => {
      csrfToken = data.token;
      csrfHeaderName = data.headerName || DEFAULT_CSRF_HEADER;
      return data.token;
    })
    .finally(() => {
      if (csrfRequest === request) {
        csrfRequest = null;
      }
    });

  csrfRequest = request;
  return request;
};

/**
 * Xoa request token da cache khi identity/session thay doi.
 * Antiforgery cookie phia server se duoc cap lai o lan lay token tiep theo.
 */
export const resetBffCsrfToken = (): void => {
  csrfToken = null;
  csrfRequest = null;
  csrfHeaderName = DEFAULT_CSRF_HEADER;
};

/**
 * Noi BFF client voi auth state. Khi gap 401, client clear auth state thay vi refresh app token.
 */
export const configureBffUnauthorizedHandler = (
  handler: (() => void) | null
): void => {
  handleBffUnauthorized = handler;
};

/**
 * Client mac dinh cho BFF: browser gui session cookie, khong gui app access token.
 */
export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
});

apiClient.interceptors.request.use(async (config) => {
  if (!isUnsafeMethod(config.method)) {
    return config;
  }

  const token = await loadCsrfToken();
  config.headers.set(csrfHeaderName, token);
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ApiErrorResponse>) => {
    const originalRequest = error.config as BffRetryConfig | undefined;

    if (
      originalRequest &&
      isUnsafeMethod(originalRequest.method) &&
      isCsrfFailure(error) &&
      !originalRequest._csrfRetry
    ) {
      originalRequest._csrfRetry = true;
      const token = await loadCsrfToken(true);
      originalRequest.headers.set(csrfHeaderName, token);
      return apiClient(originalRequest);
    }

    if (error.response?.status === 401 && !originalRequest?._skipBffUnauthorized) {
      handleBffUnauthorized?.();
    }

    return Promise.reject(error);
  }
);

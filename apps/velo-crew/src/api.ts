export type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number };
export type Order = {
  id: string; status: string; paymentStatus: string; paymentMethod: string; createdAt: string; expiresAt: string;
  total: number; subtotal: number; deliveryFee: number; currency: string;
  address: { recipientName: string; phoneNumber: string; city: string; area: string; street: string; building: string; floor?: string; apartment?: string; instructions?: string };
  items: { variantId: string; productName: string; variantName: string; sku: string; quantity: number; unitPrice: number; lineTotal: number }[];
  history: { status: string; reason: string | null; createdAt: string }[];
};
export type Tokens = { accessToken: string; refreshToken: string; expiresIn: number };
export type Staff = { userId: string; permissions: string[] };
export class ApiError extends Error {
  constructor(public status: number, public code: string) {
    super(status === 401 ? 'Your session has expired. Please sign in again.'
      : status === 403 ? 'Staff permission is required. Ask your operator to check your access.'
      : status === 409 ? 'This order changed. Refresh the queue and try again.'
      : status === 429 ? 'Too many attempts. Please wait before trying again.'
      : `${code.replaceAll('_', ' ')}. Please check the details and try again.`);
  }
}
export function createApi(getToken: () => string | undefined, onExpired: () => void, transport: typeof fetch = fetch) {
  return async function request<T>(path: string, method = 'GET', body?: unknown, token?: string, signal?: AbortSignal): Promise<T> {
    let response: Response;
    try {
      const bearer = token ?? getToken();
      response = await transport(`/api/v1${path}`, {
        method, signal, cache: 'no-store', credentials: 'omit', redirect: 'error',
        headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...(bearer ? { Authorization: `Bearer ${bearer}` } : {}) },
        body: body === undefined ? undefined : JSON.stringify(body)
      });
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error;
      throw new Error('Unable to reach the Velo API. Check your connection and try again.');
    }
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      if (response.status === 401 && !token) onExpired();
      throw new ApiError(response.status, problem.code ?? problem.title ?? `request_failed_${response.status}`);
    }
    if (response.status === 204) return undefined as T;
    if (!response.headers.get('Content-Type')?.includes('application/json')) throw new Error('Unexpected API response. Check the API proxy configuration.');
    return response.json() as Promise<T>;
  };
}
export const money = (value: number, currency = 'EGP') => new Intl.NumberFormat('en-EG', { style: 'currency', currency }).format(value);

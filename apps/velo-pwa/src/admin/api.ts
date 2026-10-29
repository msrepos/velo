export type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number };
export type Category = { id: string; name: string; isActive: boolean; version: string };
export type Variant = Category & { sku: string; price: number; barcode: string | null; currency: string };
export type Product = Category & { categoryId: string; description: string | null; imageUrl: string | null; variants: Variant[] };
export type UserRole = 'Customer' | 'Admin' | 'Driver' | 'Crew';
export type User = { id: string; name: string | null; email: string | null; phoneNumber: string; isActive: boolean; phoneNumberConfirmed: boolean; version: string; role: UserRole };
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
  constructor(public status: number, public code: string, public retryAfter?: string | null) { super(errorMessage(status, code, retryAfter)); }
}
function errorMessage(status: number, code: string, retryAfter?: string | null) {
  if (status === 401) return 'Your session has expired. Please sign in again.';
  if (code === 'staff_access_not_granted') return 'This account has no staff access. Ask an operator to grant the required permissions.';
  if (status === 403) return 'Your account does not have permission for this action. Ask an operator to check your staff access.';
  if (code.includes('version_conflict') || code === 'user_update_conflict') return 'This record changed while you were editing it. Close this panel, refresh the list, and try again.';
  if (code === 'duplicate_catalog_value') return 'That category name, SKU, or barcode is already in use.';
  if (status === 429) return `Too many attempts. ${retryAfter ? `Try again in ${retryAfter} seconds.` : 'Please wait before trying again.'}`;
  if (code === 'cannot_deactivate_self') return 'You cannot deactivate your own account.';
  if (code === 'cannot_change_own_role') return 'Another administrator must change your role.';
  if (code === 'phone_already_registered' || code === 'user_registration_conflict') return 'An account with this phone number already exists. Search for the account and edit it instead.';
  if (code === 'user_has_active_dispatch') return 'End this user’s active shift before changing their role or deactivating them.';
  if (code === 'user_has_active_delivery') return 'Finish, cancel, or return this user’s active delivery before changing their role.';
  if (code === 'dispatch_resource_busy') return 'This minivan or team member is already on an active shift. Refresh availability and choose another team.';
  if (code === 'driver_has_active_dispatch' || code === 'crew_has_active_dispatch') return 'End this member’s active shift before deactivating their profile.';
  if (code === 'driver_has_active_delivery') return 'This driver has an unfinished delivery. Finish, cancel, or return it before changing their shift.';
  if (code === 'different_team_members_required') return 'Choose two different people for the driver and crew roles.';
  if (code === 'verified_crew_user_required' || code === 'verified_driver_user_required') return 'Choose an active account with a verified phone number.';
  if (code === 'active_mobile_warehouse_required') return 'Choose an active minivan registered as a Mobile warehouse.';
  return `${code.replaceAll('_', ' ')}. Please check the details and try again.`;
}
export function createApi(getToken: () => string | undefined, onExpired: () => void, transport: typeof fetch = fetch) {
  return async function request<T>(path: string, method = 'GET', body?: unknown, token?: string, signal?: AbortSignal): Promise<T> {
    let response: Response;
    try {
      response = await transport(`/api/v1${path}`, {
        method, signal, cache: 'no-store', credentials: 'omit', redirect: 'error',
        headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...((token ?? getToken()) ? { Authorization: `Bearer ${token ?? getToken()}` } : {}) },
        body: body === undefined ? undefined : JSON.stringify(body)
      });
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error;
      throw new Error('Unable to reach the Velo API. Check your connection and try again.');
    }
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      if (response.status === 401 && !token) onExpired();
      throw new ApiError(response.status, problem.code ?? problem.title ?? `request_failed_${response.status}`, response.headers.get('Retry-After'));
    }
    if (response.status === 204) return undefined as T;
    if (!response.headers.get('Content-Type')?.includes('application/json')) throw new Error('The API returned an unexpected response. Check the API proxy configuration.');
    return response.json() as Promise<T>;
  };
}
export const orderStatuses = ['Pending', 'Confirmed', 'Preparing', 'ReadyForDispatch', 'OutForDelivery', 'Delivered', 'DeliveryFailed', 'Returned', 'Cancelled', 'Expired'];
export const label = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');
export const money = (value: number, currency = 'EGP') => new Intl.NumberFormat('en-EG', { style: 'currency', currency }).format(value);

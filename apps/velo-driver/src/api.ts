export type Tokens = { accessToken: string; refreshToken: string; expiresIn: number };
export type Shift = { id: string; warehouseId: string; vanCode: string; vanName: string; driverName: string; crewMemberName: string };
export type Address = { recipientName: string; phoneNumber: string; city: string; area: string; street: string; building: string; floor?: string; apartment?: string; instructions?: string; latitude: number; longitude: number };
export type DriverOrder = { id: string; status: string; total: number; currency: string; paymentMethod: string; paymentStatus: string; address: Address; items: { variantId: string; productName: string; variantName: string; quantity: number }[] };
export type Journey = { orderId: string; status: string; travelStartedAt: string | null; arrivedAt: string | null };
export type Work = { order: DriverOrder; journey: Journey };
export type Api = ReturnType<typeof createApi>;

export class ApiError extends Error {
  constructor(public status: number, public code: string, public retryAfter: number | null = null) {
    super(code === 'active_driver_required' ? 'الحساب ده مش مسجل كسواق نشط. راجع الإدارة.'
      : code === 'active_van_dispatch_required' ? 'محتاج شيفت نشط من صفحة Dispatching.'
      : code === 'invalid_delivery_code' ? 'كود الاستلام مش صحيح. راجعه مع العميل.'
      : code === 'delivery_code_locked' ? 'محاولات الكود خلصت. اطلب من العميل يجدد الكود من حسابه.'
      : code === 'delivery_code_expired' ? 'كود الاستلام انتهت صلاحيته. اطلب من العميل يجدد الكود.'
      : code === 'cash_amount_mismatch' ? 'المبلغ لازم يساوي إجمالي الطلب بالضبط.'
      : code === 'travel_not_started' ? 'ابدأ الرحلة الأول قبل تأكيد الوصول.'
      : status === 401 ? 'الجلسة انتهت. سجل دخول تاني.'
      : status === 403 ? 'حسابك مش مسموح له بالإجراء ده. راجع الإدارة.'
      : status === 409 || status === 404 ? 'حالة الطلب اتغيرت. حدّث البيانات وحاول تاني.'
      : status === 429 ? 'محاولات كتير. استنى شوية وحاول تاني.'
      : 'تعذر إتمام الطلب. راجع البيانات وحاول تاني.');
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
      throw new Error('مش قادرين نتصل بـ Velo. راجع الإنترنت وحاول تاني.');
    }
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      if (response.status === 401 && !token) onExpired();
      const retry = response.headers.get('Retry-After');
      throw new ApiError(response.status, problem.code ?? problem.title ?? 'request_failed', retry && /^\d+$/.test(retry) ? Number(retry) : null);
    }
    if (response.status === 204) return undefined as T;
    if (!response.headers.get('Content-Type')?.includes('application/json')) throw new Error('رد غير متوقع من السيرفر. حاول تاني.');
    return response.json() as Promise<T>;
  };
}

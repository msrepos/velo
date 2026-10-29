import { describe, expect, it, vi } from 'vitest';
import { createApi } from './api';

describe('crew API session isolation', () => {
  it('sends only its bearer session on the crew origin and never caches data', async () => {
    const transport = vi.fn().mockResolvedValue(Response.json({ status: 'Preparing' }));
    const api = createApi(() => 'crew-token', vi.fn(), transport);
    await api('/admin/orders/order/prepare', 'POST');
    expect(transport).toHaveBeenCalledWith('/api/v1/admin/orders/order/prepare', expect.objectContaining({
      method: 'POST', cache: 'no-store', credentials: 'omit', redirect: 'error',
      headers: { Authorization: 'Bearer crew-token' }
    }));
  });
  it('expires the current crew session on 401 without expiring a session for failed login', async () => {
    const expired = vi.fn();
    const transport = vi.fn().mockImplementation(() => Promise.resolve(Response.json({}, { status: 401 })));
    const api = createApi(() => 'crew-token', expired, transport);
    await expect(api('/admin/orders')).rejects.toThrow('session has expired');
    expect(expired).toHaveBeenCalledTimes(1);
    await expect(api('/auth/staff/me', 'GET', undefined, 'new-token')).rejects.toThrow();
    expect(expired).toHaveBeenCalledTimes(1);
  });
  it('rejects an HTML fallback instead of treating it as order data', async () => {
    const transport = vi.fn().mockResolvedValue(new Response('<html></html>', { headers: { 'Content-Type': 'text/html' } }));
    const api = createApi(() => undefined, vi.fn(), transport);
    await expect(api('/admin/orders')).rejects.toThrow('proxy configuration');
  });
});

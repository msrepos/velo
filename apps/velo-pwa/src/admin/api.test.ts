import { describe, expect, it, vi } from 'vitest';
import { ApiError, createApi } from './api';

describe('admin API boundary', () => {
  it('keeps staff requests on the app origin, authenticated, and uncached', async () => {
    const transport = vi.fn().mockResolvedValue(Response.json({ id: 'product' }));
    const request = createApi(() => 'staff-token', vi.fn(), transport);
    expect(await request('/admin/catalog/products/p1', 'PUT', { version: 'v1', isActive: false })).toEqual({ id: 'product' });
    expect(transport).toHaveBeenCalledWith('/api/v1/admin/catalog/products/p1', expect.objectContaining({
      method: 'PUT', cache: 'no-store', credentials: 'omit', redirect: 'error',
      headers: { Authorization: 'Bearer staff-token', 'Content-Type': 'application/json' },
      body: JSON.stringify({ version: 'v1', isActive: false })
    }));
  });
  it('clears an expired staff session but does not clear one for a failed login attempt', async () => {
    const expired = vi.fn();
    const transport = vi.fn().mockImplementation(() => Promise.resolve(Response.json({}, { status: 401 })));
    const request = createApi(() => 'staff-token', expired, transport);
    await expect(request('/admin/users')).rejects.toThrow('Your session has expired');
    expect(expired).toHaveBeenCalledTimes(1);
    await expect(request('/auth/staff/me', 'GET', undefined, 'login-token')).rejects.toBeInstanceOf(ApiError);
    expect(expired).toHaveBeenCalledTimes(1);
  });
  it('explains stale edits and refuses the SPA fallback masquerading as an API response', async () => {
    const transport = vi.fn().mockResolvedValueOnce(Response.json({ code: 'catalog_version_conflict' }, { status: 409 }))
      .mockResolvedValueOnce(new Response('<html>Storefront</html>', { headers: { 'Content-Type': 'text/html' } }));
    const request = createApi(() => undefined, vi.fn(), transport);
    await expect(request('/admin/catalog/categories/id', 'PUT', { version: 'old' })).rejects.toThrow('record changed');
    await expect(request('/admin/catalog/products')).rejects.toThrow('proxy configuration');
  });
  it('handles no-content logout and surfaces retry limits', async () => {
    const transport = vi.fn().mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(Response.json({}, { status: 429, headers: { 'Retry-After': '60' } }));
    const request = createApi(() => undefined, vi.fn(), transport);
    await expect(request('/auth/logout', 'POST')).resolves.toBeUndefined();
    await expect(request('/auth/otp/request', 'POST', {})).rejects.toThrow('60 seconds');
  });
});

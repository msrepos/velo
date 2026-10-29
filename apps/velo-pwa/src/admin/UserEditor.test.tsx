import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { createApi, type User } from './api';
import { UserEditor, userRoles } from './UserEditor';

const props = { api: createApi(() => undefined, vi.fn()), currentUser: 'admin', close: vi.fn(), saved: vi.fn() };
describe('user registration and role editing', () => {
  it('offers all four roles and a phone input for a new account', () => {
    const html = renderToStaticMarkup(<UserEditor {...props} user="new" />);
    for (const role of userRoles) expect(html).toContain(`>${role}</option>`);
    expect(html).toContain('Create user');
    expect(html).toContain('unverified phone');
    expect(html).not.toContain('readOnly');
    expect(html).toContain('type="tel"');
  });
  it('locks the sign-in phone and prevents self-demotion', () => {
    const user: User = { id: 'admin', name: 'Admin', email: 'admin@example.com', phoneNumber: '+201000000001', role: 'Admin', isActive: true, phoneNumberConfirmed: true, version: '1' };
    const html = renderToStaticMarkup(<UserEditor {...props} user={user} />);
    expect(html).toContain('readOnly');
    expect(html).toContain('Another administrator must change your own role');
    expect(html).toMatch(/<select[^>]*disabled/);
    expect(html).toContain('Save changes');
  });
});

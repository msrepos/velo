import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { AdminWorkspace } from './Admin';
import { createApi } from './api';

vi.mock('virtual:pwa-register/react', () => ({ useRegisterSW: () => ({ needRefresh: [false], updateServiceWorker: vi.fn() }) }));

const staff = { userId: 'staff', permissions: ['catalog.manage', 'orders.manage', 'users.manage'] };
const props = {
  staff, identity: { name: 'Velo Admin User', phoneNumber: '+201000000000' }, mobile: false,
  setMobile: vi.fn(), navigate: vi.fn(), logout: vi.fn(), api: createApi(() => undefined, vi.fn())
};

describe('admin workspace rendering', () => {
  it.each(['overview', 'products', 'categories', 'orders', 'users'] as const)('renders the navigation and %s content without advertising selectors', section => {
    const html = renderToStaticMarkup(<AdminWorkspace {...props} section={section} />);
    expect(html).toContain('aria-label="Admin navigation"');
    expect(html).toContain('class="velo-admin-sidebar ');
    expect(html).toContain('id="admin-main" class="velo-admin-main"');
    for (const route of ['products', 'categories', 'orders', 'users']) expect(html).toContain(`href="/admin/${route}"`);
    expect(html).toContain(`aria-current="page"`);
    const activePath = section === 'overview' ? '/admin' : `/admin/${section}`;
    expect(html).toContain(`href="${activePath}" class="active" aria-current="page"`);
    expect(html).toContain(section === 'overview' ? 'A clear view of your day.' : `All ${section}`);
    expect(html).not.toMatch(/class="ad-/);
    expect(html).toContain('aria-controls="admin-navigation" aria-expanded="false"');
  });
  it('renders the open mobile navigation state and close control', () => {
    const html = renderToStaticMarkup(<AdminWorkspace {...props} section="products" mobile />);
    expect(html).toContain('class="velo-admin-sidebar open"');
    expect(html).toContain('aria-label="Close navigation"');
    expect(html).toContain('aria-expanded="true"');
  });
  it('shows a permission explanation instead of protected management controls', () => {
    const html = renderToStaticMarkup(<AdminWorkspace {...props} section="products" staff={{ userId: 'limited', permissions: ['orders.manage'] }} />);
    expect(html).toContain('Staff permission required');
    expect(html).toContain('catalog.manage');
    expect(html).not.toContain('Add product');
    expect(html).not.toContain('All products');
  });
  it.each(['drivers', 'crew', 'minivans', 'dispatching'] as const)('protects the %s fleet page with delivery permission', section => {
    const html = renderToStaticMarkup(<AdminWorkspace {...props} section={section} />);
    expect(html).toContain('Staff permission required');
    expect(html).toContain('delivery.manage');
    expect(html).toContain(`href="/admin/${section}" class="active"`);
  });
  it.each(['drivers', 'crew', 'minivans', 'dispatching'] as const)('opens the %s fleet page for operations staff', section => {
    const html = renderToStaticMarkup(<AdminWorkspace {...props} staff={{ ...staff, permissions: ['delivery.manage', 'inventory.manage'] }} section={section} />);
    expect(html).not.toContain('Staff permission required');
    expect(html).toContain(section === 'dispatching' ? 'Dispatch history' : `All ${section}`);
    expect(html).toContain('Add ');
  });
});

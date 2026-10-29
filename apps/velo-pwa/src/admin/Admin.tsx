import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useRegisterSW } from 'virtual:pwa-register/react';
import { ArrowDownLeft, ArrowRight, ArrowUpRight, Box, Check, ChevronLeft, ChevronRight, CircleHelp, ExternalLink, FolderTree, LayoutDashboard, LogOut, Menu, Package, Pencil, Plus, RefreshCw, Search, ShieldCheck, ShoppingBag, Users, X } from 'lucide-react';
import { createApi, label, money, orderStatuses, type Category, type Order, type Page, type Product, type Staff, type Tokens, type User, type Variant } from './api';
import './admin.css';
import { FleetPage } from './Fleet';
import { UserEditor } from './UserEditor';

type Api = ReturnType<typeof createApi>;
type Section = 'overview' | 'products' | 'categories' | 'orders' | 'users' | 'drivers' | 'minivans' | 'crew' | 'dispatching';
const sections = [
  { id: 'overview', name: 'Overview', icon: LayoutDashboard, permission: '' },
  { id: 'products', name: 'Products', icon: Box, permission: 'catalog.manage' },
  { id: 'categories', name: 'Categories', icon: FolderTree, permission: 'catalog.manage' },
  { id: 'orders', name: 'Orders', icon: ShoppingBag, permission: 'orders.manage' },
  { id: 'users', name: 'Users', icon: Users, permission: 'users.manage' },
  { id: 'drivers', name: 'Drivers', icon: Users, permission: 'delivery.manage' },
  { id: 'minivans', name: 'Minivans', icon: Box, permission: 'delivery.manage' },
  { id: 'crew', name: 'Crew', icon: Users, permission: 'delivery.manage' },
  { id: 'dispatching', name: 'Dispatching', icon: ArrowRight, permission: 'delivery.manage' }
] as const;
const getSection = (): Section => sections.find(s => s.id === location.pathname.split('/')[2])?.id ?? 'overview';
const titles: Record<Section, [string, string]> = {
  overview: ['A clear view of your day.', 'Your catalog, customers, and orders. All in one place.'],
  products: ['Products', 'Keep your shelves fresh and your product details up to date.'],
  categories: ['Categories', 'A little organization makes everything easier to find.'],
  orders: ['Orders', 'Follow every order from a new request to a doorstep delivery.'],
  users: ['Users', 'Manage the people who make your neighborhood go round.'],
  drivers: ['Drivers', 'Register and manage the people behind the wheel.'],
  minivans: ['Minivans', 'Your mobile warehouses, ready for the neighborhood.'],
  crew: ['Crew', 'Register the people who prepare each order on board.'],
  dispatching: ['Dispatching', 'Pair a minivan, a driver, and a crew member for a shift.']
};
const date = (value: string) => new Date(value).toLocaleString('en-GB', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
const message = (error: unknown) => error instanceof Error ? error.message : 'Something went wrong. Please try again.';
function Badge({ children, tone = '' }: { children: ReactNode; tone?: string }) { return <span className={`velo-admin-badge ${tone}`}>{children}</span>; }
function Status({ value }: { value: string }) { return <Badge tone={['Active', 'Delivered', 'Confirmed', 'Paid'].includes(value) ? 'green' : ['Pending', 'Preparing', 'ReadyForDispatch'].includes(value) ? 'amber' : ''}>{label(value)}</Badge>; }
function Empty({ title, children }: { title: string; children?: ReactNode }) { return <div className="velo-admin-empty"><Package size={34} strokeWidth={1.3} /><h3>{title}</h3><p>{children ?? 'Your records will appear here when they are available.'}</p></div>; }
function Alert({ children }: { children: ReactNode }) { return <div className="velo-admin-alert" role="alert">{children}</div>; }

export default function Admin() {
  const [session, setSession] = useState<Tokens | null>(null);
  const sessionRef = useRef<Tokens | null>(null);
  const [staff, setStaff] = useState<Staff | null>(null);
  const [identity, setIdentity] = useState<{ name?: string; phoneNumber: string } | null>(null);
  const [section, setSection] = useState<Section>(getSection);
  const [mobile, setMobile] = useState(false);
  const [authMessage, setAuthMessage] = useState('');
  const [api] = useState(() => createApi(() => sessionRef.current?.accessToken, () => {
    sessionRef.current = null; setSession(null); setStaff(null); setIdentity(null); setAuthMessage('Your session has expired. Sign in to continue.');
  }));
  useEffect(() => { document.title = 'Velo Admin · Your neighborhood, managed'; }, []);
  useEffect(() => { const pop = () => { setSection(getSection()); setMobile(false); }; window.addEventListener('popstate', pop); return () => window.removeEventListener('popstate', pop); }, []);
  useEffect(() => {
    if (!mobile) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { setMobile(false); document.getElementById('admin-navigation-toggle')?.focus(); } };
    window.addEventListener('keydown', escape);
    return () => { document.body.style.overflow = previousOverflow; window.removeEventListener('keydown', escape); };
  }, [mobile]);
  useEffect(() => {
    if (!session) return;
    const timeout = setTimeout(async () => {
      try {
        const tokens = await api<Tokens>('/auth/refresh', 'POST', { refreshToken: session.refreshToken });
        if (sessionRef.current !== session) return;
        sessionRef.current = tokens; setSession(tokens);
      } catch (error) {
        if (sessionRef.current !== session) return;
        sessionRef.current = null; setSession(null); setStaff(null); setIdentity(null); setAuthMessage(message(error));
      }
    }, Math.max(1000, (session.expiresIn - 60) * 1000));
    return () => clearTimeout(timeout);
  }, [session, api]);
  const navigate = (next: Section) => { history.pushState(null, '', next === 'overview' ? '/admin' : `/admin/${next}`); setSection(next); setMobile(false); };
  const signedIn = async (tokens: Tokens) => {
    const [access, user] = await Promise.all([api<Staff>('/auth/staff/me', 'GET', undefined, tokens.accessToken), api<{ name?: string; phoneNumber: string }>('/auth/me', 'GET', undefined, tokens.accessToken)]);
    sessionRef.current = tokens; setStaff(access); setIdentity(user); setSession(tokens); setAuthMessage('');
  };
  const logout = async () => {
    const token = sessionRef.current?.accessToken;
    sessionRef.current = null; setSession(null); setStaff(null); setIdentity(null); setAuthMessage('');
    if (token) await api('/auth/logout', 'POST', undefined, token).catch(() => setAuthMessage('Signed out on this device. The server could not be reached to revoke the session.'));
  };
  if (!session || !staff) return <Login api={api} onSignedIn={signedIn} initialMessage={authMessage} />;
  return <AdminWorkspace staff={staff} identity={identity} section={section} mobile={mobile} setMobile={setMobile} navigate={navigate} logout={logout} api={api} />;
}

export function AdminWorkspace({ staff, identity, section, mobile, setMobile, navigate, logout, api }: {
  staff: Staff; identity: { name?: string; phoneNumber: string } | null; section: Section;
  mobile: boolean; setMobile(open: boolean): void; navigate(section: Section): void; logout(): void; api: Api;
}) {
  const current = sections.find(s => s.id === section)!;
  const permitted = !current.permission || staff.permissions.includes(current.permission);
  return <div className="admin-app">
    <a className="velo-admin-skip" href="#admin-main">Skip to content</a>
    {mobile && <button className="velo-admin-scrim" aria-label="Close navigation" onClick={() => setMobile(false)} />}
    <aside id="admin-navigation" aria-label="Workspace sidebar" className={`velo-admin-sidebar ${mobile ? 'open' : ''}`}>
      <a href="/admin" className="velo-admin-brand" onClick={e => { e.preventDefault(); navigate('overview'); }}><img src="/logo.png" alt="Velo" /><span>ADMIN</span></a>
      <div className="velo-admin-workspace"><span className="velo-admin-workspace-icon"><ShoppingBag size={19} /></span><div><strong>Velo workspace</strong><small>Madinaty, Egypt</small></div><Badge>EG</Badge></div>
      <p className="velo-admin-nav-label">WORKSPACE</p>
      <nav aria-label="Admin navigation">{sections.map(({ id, name, icon: Icon }) => <a key={id} href={id === 'overview' ? '/admin' : `/admin/${id}`} className={section === id ? 'active' : ''} aria-current={section === id ? 'page' : undefined} onClick={e => { if (!e.ctrlKey && !e.metaKey) { e.preventDefault(); navigate(id); } }}><Icon size={19} /><span>{name}</span>{section === id && <span className="velo-admin-nav-dot" />}</a>)}</nav>
      <div className="velo-admin-sidebar-bottom"><div className="velo-admin-help"><ShieldCheck size={23} /><strong>A little more control.</strong><p>Staff access keeps your neighborhood in good hands.</p><button onClick={() => navigate('users')}>Manage your people <ArrowRight size={14} /></button></div><a href="/" target="_blank" rel="noreferrer"><ExternalLink size={17} /> Open storefront <ArrowUpRight size={15} /></a><button onClick={logout}><LogOut size={17} /> Sign out</button></div>
    </aside>
    <div className="velo-admin-shell"><header className="velo-admin-topbar"><button id="admin-navigation-toggle" className="velo-admin-icon velo-admin-menu" onClick={() => setMobile(!mobile)} aria-label={mobile ? 'Close navigation' : 'Open navigation'} aria-controls="admin-navigation" aria-expanded={mobile}><Menu size={22} /></button><div className="velo-admin-breadcrumb"><a href="/admin" onClick={e => { e.preventDefault(); navigate('overview'); }}>Workspace</a><ChevronRight size={14} /><strong>{current.name}</strong></div><div className="velo-admin-top-right"><span className="velo-admin-live"><i /> Staff session active</span><span className="velo-admin-avatar">{(identity?.name ?? 'S').slice(0, 1).toUpperCase()}</span><span className="velo-admin-account"><strong>{identity?.name ?? 'Staff member'}</strong><small>Staff workspace</small></span><button className="velo-admin-icon" onClick={logout} aria-label="Sign out of admin"><LogOut size={16} /></button></div></header>
      <main id="admin-main" className="velo-admin-main" tabIndex={-1}>
        <div className="velo-admin-page-heading"><div><div className="velo-admin-eyebrow">YOUR NEIGHBORHOOD, MANAGED</div><h1>{titles[section][0]}{section !== 'overview' && <span>.</span>}</h1><p>{titles[section][1]}</p></div>{section === 'overview' && <span className="velo-admin-date">{new Date().toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'long', year: 'numeric' })}</span>}</div>
        <AdminUpdate />
        {!permitted ? <Empty title="Staff permission required">You need <strong>{current.permission}</strong> to manage this section. Ask an operator to grant access, then sign in again.</Empty> : section === 'overview' ? <Overview api={api} staff={staff} navigate={navigate} /> : section === 'drivers' || section === 'minivans' || section === 'crew' || section === 'dispatching' ? <FleetPage key={section} api={api} section={section} staff={staff} /> : <Records key={section} api={api} section={section} staff={staff} />}
        <footer className="velo-admin-footer"><span>Velo admin <span>·</span> A little more local.</span><span>Prices in EGP</span></footer>
      </main>
    </div>
  </div>;
}

function AdminUpdate() {
  const { needRefresh: [needRefresh], updateServiceWorker } = useRegisterSW();
  return needRefresh ? <div className="velo-admin-update" role="status"><span>A new admin version is ready. Save your changes before updating.</span><button className="velo-admin-secondary" onClick={() => updateServiceWorker(true)}>Update & reload <RefreshCw size={14} /></button></div> : null;
}

function Login({ api, onSignedIn, initialMessage }: { api: Api; onSignedIn(tokens: Tokens): Promise<void>; initialMessage: string }) {
  const [phone, setPhone] = useState('');
  const [code, setCode] = useState('');
  const [challenge, setChallenge] = useState<{ challengeId: string; expiresIn: number; resendAfter: number; developmentCode?: string } | null>(null);
  const [error, setError] = useState(initialMessage);
  const [busy, setBusy] = useState(false);
  const lock = useRef(false);
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return; lock.current = true; setBusy(true); setError('');
    let customer: Tokens | null = null;
    let staffTokens: Tokens | null = null;
    try {
      if (!challenge) setChallenge(await api('/auth/otp/request', 'POST', { phoneNumber: phone }));
      else {
        customer = await api<Tokens>('/auth/otp/verify', 'POST', { challengeId: challenge.challengeId, code });
        staffTokens = await api<Tokens>('/auth/staff/exchange', 'POST', undefined, customer.accessToken);
        await onSignedIn(staffTokens);
      }
    } catch (err) {
      setError(message(err));
      if (customer) { setChallenge(null); setCode(''); }
      if (staffTokens) await api('/auth/logout', 'POST', undefined, staffTokens.accessToken).catch(() => {});
    } finally {
      if (customer) await api('/auth/logout', 'POST', undefined, customer.accessToken).catch(() => {});
      lock.current = false; setBusy(false);
    }
  }
  return <div className="admin-app velo-admin-login"><section className="velo-admin-login-story"><a href="/"><img src="/logo.png" alt="Velo storefront" /></a><Badge>THE STAFF WORKSPACE</Badge><h1>Your neighborhood.<br />In good hands<span>.</span></h1><p>Good things happen behind the scenes.<br />A fresh catalog. A packed order. A happier customer.</p><div className="velo-admin-story-grid"><div><Box /><strong>Fresh shelves</strong><span>Products & categories</span></div><div><ShoppingBag /><strong>Every delivery</strong><span>Orders & operations</span></div><div><Users /><strong>Your people</strong><span>Customers & accounts</span></div></div><span className="velo-admin-story-foot">A LITTLE MORE LOCAL. A LITTLE MORE VELO.</span></section><section className="velo-admin-login-form"><div className="velo-admin-login-card"><span className="velo-admin-login-icon"><ShieldCheck size={28} /></span><div className="velo-admin-eyebrow">WELCOME TO VELO ADMIN</div><h2>{challenge ? 'Check your phone.' : 'Let’s get to work.'}</h2><p>{challenge ? `Enter the verification code for ${phone}.` : 'Sign in with your staff phone number to manage your workspace.'}</p>{error && <Alert>{error}</Alert>}<form onSubmit={submit}><label>{challenge ? 'Verification code' : 'Phone number'}{challenge ? <input autoFocus required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={code} onChange={e => setCode(e.target.value)} placeholder="6-digit code" /> : <input autoFocus required type="tel" autoComplete="tel" placeholder="+20 10 1234 5678" value={phone} onChange={e => setPhone(e.target.value)} maxLength={20} />}</label>{challenge?.developmentCode && <div className="velo-admin-dev-code">Local development code: <strong>{challenge.developmentCode}</strong><small>Simulated SMS is enabled on this server.</small></div>}<button className="velo-admin-primary" disabled={busy}>{busy ? 'Please wait…' : challenge ? 'Verify & sign in' : 'Send verification code'}<ArrowRight size={17} /></button>{challenge && <button type="button" className="velo-admin-text-button" disabled={busy} onClick={() => { setChallenge(null); setCode(''); setError(''); }}>Use a different number / request a new code</button>}</form><div className="velo-admin-login-note"><ShieldCheck size={17} /><span>For authorized Velo staff. Your operator must grant staff permissions before you can sign in.</span></div><a className="velo-admin-back" href="/"><ArrowDownLeft size={16} /> Back to the neighborhood</a></div><span className="velo-admin-login-copyright">Velo · Your day, delivered.</span></section></div>;
}

function Overview({ api, staff, navigate }: { api: Api; staff: Staff; navigate(section: Section): void }) {
  const [data, setData] = useState<{ products?: number; categories?: number; orders?: number; users?: number; pending?: number; recent: Order[] } | null>(null);
  const [error, setError] = useState('');
  const [revision, refresh] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); setError(''); setData(null);
    async function load() {
      try {
        const result: NonNullable<typeof data> = { recent: [] };
        await Promise.all(sections.filter(s => s.permission && staff.permissions.includes(s.permission)).map(async s => {
          const page = await api<Page<Order>>(s.id === 'products' || s.id === 'categories' ? `/admin/catalog/${s.id}?pageSize=1` : `/admin/${s.id}?pageSize=${s.id === 'orders' ? 5 : 1}`, 'GET', undefined, undefined, controller.signal);
          result[s.id as 'products' | 'categories' | 'orders' | 'users'] = page.totalCount;
          if (s.id === 'orders') { result.recent = page.items; result.pending = (await api<Page<Order>>('/admin/orders?status=Pending&pageSize=1', 'GET', undefined, undefined, controller.signal)).totalCount; }
        }));
        if (!controller.signal.aborted) setData(result);
      } catch (err) { if (!controller.signal.aborted) setError(message(err)); }
    }
    void load(); return () => controller.abort();
  }, [api, staff, revision]);
  return <>{error && <Alert>{error} <button className="velo-admin-text-button" onClick={() => refresh(r => r + 1)}>Try again</button></Alert>}<div className="velo-admin-stats">{sections.filter(s => s.permission && staff.permissions.includes(s.permission)).map(({ id, name, icon: Icon }) => <button className="velo-admin-stat" key={id} onClick={() => navigate(id)}><span className={`velo-admin-stat-icon ${id}`}><Icon size={21} /></span><span className="velo-admin-stat-label">Total {name.toLowerCase()}<ArrowUpRight size={16} /></span><strong>{data ? data[id as 'products' | 'categories' | 'orders' | 'users'] ?? '—' : error ? '—' : '…'}</strong><small>View {name.toLowerCase()} <ArrowRight size={12} /></small></button>)}</div>
    <section className="velo-admin-welcome"><div><Badge>THE EVERYDAY, TAKEN CARE OF</Badge><h2>Good days start<br />with good operations.</h2><p>A well-stocked shelf is just the beginning.<br />Make every neighborhood delivery count.</p><button className="velo-admin-primary" onClick={() => navigate(staff.permissions.includes('catalog.manage') ? 'products' : staff.permissions.includes('orders.manage') ? 'orders' : 'users')}>Go to your workspace <ArrowRight size={16} /></button></div><div className="velo-admin-welcome-art" aria-hidden="true"><div className="velo-admin-art-ring" /><ShoppingBag size={115} strokeWidth={1} /><span><Check size={25} /></span><i>✳</i></div></section>
    <div className="velo-admin-overview-bottom"><section className="velo-admin-panel"><div className="velo-admin-panel-heading"><div><h2>Recent orders</h2><p>The latest from your neighborhood.</p></div><button className="velo-admin-text-button" onClick={() => navigate('orders')}>View all <ArrowUpRight size={15} /></button></div>{!staff.permissions.includes('orders.manage') ? <Empty title="Order access required">Ask for orders.manage to see the queue.</Empty> : !data ? <Empty title={error ? 'Orders could not be loaded' : 'Loading your orders…'} /> : !data.recent.length ? <Empty title="Ready for the first order">New customer orders will arrive here.</Empty> : <div className="velo-admin-table-scroll"><table><thead><tr><th>Order / customer</th><th>Status</th><th>Total</th></tr></thead><tbody>{data.recent.map(o => <tr key={o.id}><td><button className="velo-admin-row-link" onClick={() => navigate('orders')}>#{o.id.slice(0, 8).toUpperCase()}</button><small>{o.address.recipientName}</small></td><td><Status value={o.status} /></td><td className="velo-admin-nowrap">{money(o.total, o.currency)}</td></tr>)}</tbody></table></div>}</section><section className="velo-admin-panel velo-admin-day-panel"><span className="velo-admin-stat-icon orders"><ShoppingBag size={21} /></span><h2>A little attention<br />goes a long way.</h2><p>{data?.pending !== undefined ? `${data.pending} pending ${data.pending === 1 ? 'order needs' : 'orders need'} confirmation before the reservation expires.` : 'Keep an eye on the order queue so every delivery gets a good start.'}</p><button className="velo-admin-secondary" onClick={() => navigate('orders')}>Open order queue <ArrowRight size={15} /></button><div className="velo-admin-tip"><CircleHelp size={17} /><span>Confirming an order consumes reserved stock. Review the details first.</span></div></section></div></>;
}

function Records({ api, section, staff }: { api: Api; section: 'products' | 'categories' | 'orders' | 'users'; staff: Staff }) {
  const [search, setSearch] = useState(''); const [query, setQuery] = useState(''); const [page, setPage] = useState(1);
  const [status, setStatus] = useState(''); const [revision, refresh] = useState(0);
  const [rows, setRows] = useState<Page<Product | Category | Order | User> | null>(null);
  const [categories, setCategories] = useState<Category[]>([]);
  const [error, setError] = useState(''); const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState<Product | Category | User | 'new' | null>(null);
  const [variants, setVariants] = useState<Product | null>(null); const [order, setOrder] = useState<Order | null>(null);
  const [toast, setToast] = useState('');
  useEffect(() => { const t = setTimeout(() => { setQuery(search.trim()); setPage(1); }, 300); return () => clearTimeout(t); }, [search]);
  useEffect(() => { if (!toast) return; const t = setTimeout(() => setToast(''), 4500); return () => clearTimeout(t); }, [toast]);
  useEffect(() => {
    const abort = new AbortController(); setLoading(true); setError(''); setRows(null);
    const params = new URLSearchParams({ page: String(page), pageSize: '20' });
    if (query && (section === 'products' || section === 'users')) params.set('search', query);
    if (section === 'orders' && status) params.set('status', status);
    const prefix = section === 'products' || section === 'categories' ? '/admin/catalog' : '/admin';
    api<Page<Product | Category | Order | User>>(`${prefix}/${section}?${params}`, 'GET', undefined, undefined, abort.signal)
      .then(data => { if (!abort.signal.aborted) { if (!data.items.length && page > 1) setPage(p => p - 1); else setRows(data); } })
      .catch(err => { if (!abort.signal.aborted) setError(message(err)); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [api, section, query, page, status, revision]);
  useEffect(() => {
    if (section !== 'products') return;
    const abort = new AbortController();
    void (async () => {
      try {
        const items: Category[] = []; let p = 1; let total = 0;
        do { const result = await api<Page<Category>>(`/admin/catalog/categories?page=${p++}&pageSize=100`, 'GET', undefined, undefined, abort.signal); items.push(...result.items); total = result.totalCount; if (!result.items.length) break; } while (items.length < total);
        if (!abort.signal.aborted) setCategories(items);
      } catch (err) { if (!abort.signal.aborted) setError(message(err)); }
    })(); return () => abort.abort();
  }, [api, section, revision]);
  const changed = (text = 'Changes saved.') => { refresh(r => r + 1); setToast(text); };
  return <>{toast && <div className="velo-admin-toast" role="status"><Check size={17} />{toast}</div>}<section className="velo-admin-panel velo-admin-records"><div className="velo-admin-panel-heading"><div><h2>All {section} <Badge>{rows?.totalCount ?? '—'}</Badge></h2><p>{section === 'products' ? 'Products and their sellable SKUs.' : section === 'categories' ? 'The building blocks of your catalog.' : section === 'users' ? 'Registered customer and staff accounts.' : 'Live orders, payment status, and fulfillment.'}</p></div><div className="velo-admin-actions"><button className="velo-admin-secondary" onClick={() => refresh(r => r + 1)} disabled={loading}><RefreshCw size={15} className={loading ? 'velo-admin-spin' : ''} /> Refresh</button>{(section === 'products' || section === 'categories' || section === 'users') && <button className="velo-admin-primary" onClick={() => setEditing('new')}><Plus size={16} /> Add {section === 'products' ? 'product' : section === 'users' ? 'user' : 'category'}</button>}</div></div>
      <div className="velo-admin-filters">{(section === 'products' || section === 'users') ? <label className="velo-admin-search"><Search size={18} /><input aria-label={`Search ${section}`} placeholder={section === 'users' ? 'Search name, email, or phone…' : 'Search product names…'} value={search} onChange={e => setSearch(e.target.value)} maxLength={100} />{search && <button className="velo-admin-icon" aria-label="Clear search" onClick={() => setSearch('')}><X size={15} /></button>}</label> : <span className="velo-admin-filter-caption">{section === 'orders' ? 'ORDER QUEUE' : 'CATALOG ORGANIZATION'}</span>}{section === 'orders' ? <label className="velo-admin-inline-label">Status<select value={status} onChange={e => { setStatus(e.target.value); setPage(1); }}><option value="">All statuses</option>{orderStatuses.map(s => <option key={s}>{s}</option>)}</select></label> : <span className="velo-admin-filter-note">Active & inactive records</span>}</div>
      {error && <Alert>{error}</Alert>}{loading ? <Empty title={`Loading ${section}…`} /> : !rows?.items.length ? <Empty title={error ? 'Could not load records' : query ? 'No matching records' : `No ${section} yet`}>{query ? 'Try another search.' : section === 'users' ? 'Use Add user to register a customer, admin, driver, or crew member.' : `Your ${section} will appear here. ${section === 'orders' ? 'Customer orders use the live API.' : 'Use the Add button to get started.'}`}</Empty> : <div className="velo-admin-table-scroll"><table><thead><tr>{section === 'products' ? <><th>Product</th><th>Category</th><th>Price / SKUs</th><th>Status</th><th>Manage</th></> : section === 'categories' ? <><th>Category</th><th>Category ID</th><th>Status</th><th>Manage</th></> : section === 'users' ? <><th>User</th><th>Phone</th><th>Role</th><th>Verification</th><th>Status</th><th>Manage</th></> : <><th>Order</th><th>Customer</th><th>Status</th><th>Payment</th><th>Total</th><th>Manage</th></>}</tr></thead><tbody>{rows.items.map(row => {
        if (section === 'products') { const p = row as Product; return <tr key={p.id}><td><div className="velo-admin-product-cell"><span className="velo-admin-product-thumb">{p.imageUrl ? <img src={p.imageUrl} alt="" loading="lazy" referrerPolicy="no-referrer" onError={e => { e.currentTarget.style.display = 'none'; }} /> : <Box size={22} />}</span><div><strong>{p.name}</strong><small>{p.description || 'No description'}</small></div></div></td><td>{categories.find(c => c.id === p.categoryId)?.name ?? p.categoryId.slice(0, 8)}</td><td><strong>{p.variants.length ? money(Math.min(...p.variants.map(v => v.price))) : 'No SKUs'}</strong><small>{p.variants.length} {p.variants.length === 1 ? 'variant' : 'variants'}</small></td><td><Status value={p.isActive ? 'Active' : 'Inactive'} /></td><td><div className="velo-admin-row-actions"><button onClick={() => setEditing(p)} aria-label={`Edit ${p.name}`}><Pencil size={14} />Edit</button><button onClick={() => setVariants(p)}>SKUs</button></div></td></tr>; }
        if (section === 'categories') { const c = row as Category; return <tr key={c.id}><td><div className="velo-admin-product-cell"><span className="velo-admin-category-thumb"><FolderTree size={20} /></span><strong>{c.name}</strong></div></td><td><code>{c.id}</code></td><td><Status value={c.isActive ? 'Active' : 'Inactive'} /></td><td><button className="velo-admin-row-link" onClick={() => setEditing(c)} aria-label={`Edit ${c.name}`}><Pencil size={14} /> Edit category</button></td></tr>; }
        if (section === 'users') { const u = row as User; return <tr key={u.id}><td><strong>{u.name || 'Unnamed account'}</strong><small>{u.email || 'No email'}</small></td><td className="velo-admin-nowrap">{u.phoneNumber}</td><td><Badge>{u.role}</Badge></td><td><Badge tone={u.phoneNumberConfirmed ? 'green' : 'amber'}>{u.phoneNumberConfirmed ? 'Phone verified' : 'Unverified'}</Badge></td><td><Status value={u.isActive ? 'Active' : 'Inactive'} /></td><td><button className="velo-admin-row-link" onClick={() => setEditing(u)}><Pencil size={14} /> Edit user</button></td></tr>; }
        const o = row as Order; return <tr key={o.id}><td><strong>#{o.id.slice(0, 8).toUpperCase()}</strong><small>{date(o.createdAt)}</small></td><td><strong>{o.address.recipientName}</strong><small>{o.address.area}</small></td><td><Status value={o.status} /></td><td><Status value={o.paymentStatus} /><small>{label(o.paymentMethod)}</small></td><td className="velo-admin-nowrap">{money(o.total, o.currency)}</td><td><button className="velo-admin-row-link" onClick={() => setOrder(o)}>View order <ChevronRight size={15} /></button></td></tr>;
      })}</tbody></table></div>}
      <div className="velo-admin-pagination"><span>{rows ? `${(page - 1) * 20 + (rows.totalCount ? 1 : 0)}–${Math.min(page * 20, rows.totalCount)} of ${rows.totalCount} ${section}` : 'Records are loaded from the Velo API'}</span><div><button className="velo-admin-icon" aria-label="Previous page" disabled={loading || page === 1} onClick={() => setPage(p => p - 1)}><ChevronLeft size={17} /></button><span>Page {page}</span><button className="velo-admin-icon" aria-label="Next page" disabled={loading || !rows || page * 20 >= rows.totalCount} onClick={() => setPage(p => p + 1)}><ChevronRight size={17} /></button></div></div>
    </section>{section === 'products' && <p className="velo-admin-footnote"><CircleHelp size={15} /> A product appears in the live catalog when its category, product, and at least one SKU are active. The customer demo currently uses its own sample catalog.</p>}
    {editing && (section === 'users' ? <UserEditor api={api} user={editing as User | 'new'} currentUser={staff.userId} close={() => setEditing(null)} saved={() => { setEditing(null); changed(editing === 'new' ? 'User registered. Phone verification is required on first sign-in.' : 'User saved.'); }} /> : <Editor api={api} section={section as 'products' | 'categories'} record={editing} categories={categories} currentUser={staff.userId} close={() => setEditing(null)} saved={product => { setEditing(null); changed(); if (product) setVariants(product); }} />)}
    {variants && <Variants api={api} product={variants} close={() => setVariants(null)} changed={changed} />}
    {order && <OrderDetail api={api} order={order} close={() => setOrder(null)} changed={changed} />}
  </>;
}

function Dialog({ title, subtitle, children, close, busy = false }: { title: string; subtitle?: string; children: ReactNode; close(): void; busy?: boolean }) {
  const titleId = useId();
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => { const d = ref.current!; d.showModal(); return () => d.close(); }, []);
  return <dialog ref={ref} className="velo-admin-dialog" aria-labelledby={titleId} onCancel={e => { e.preventDefault(); if (!busy) close(); }}><div className="velo-admin-dialog-head"><div><span className="velo-admin-eyebrow">VELO WORKSPACE</span><h2 id={titleId}>{title}</h2>{subtitle && <p>{subtitle}</p>}</div><button className="velo-admin-icon" aria-label="Close panel" disabled={busy} onClick={close}><X size={21} /></button></div>{children}</dialog>;
}

function Editor({ api, section, record, categories, currentUser, close, saved }: { api: Api; section: 'products' | 'categories' | 'users'; record: Product | Category | User | 'new'; categories: Category[]; currentUser: string; close(): void; saved(product?: Product): void }) {
  const original = record === 'new' ? null : record;
  const [name, setName] = useState(original?.name ?? ''); const [active, setActive] = useState(original?.isActive ?? true);
  const product = original as Product | null; const user = original as User | null;
  const [category, setCategory] = useState(product?.categoryId ?? categories[0]?.id ?? '');
  const [description, setDescription] = useState(product?.description ?? ''); const [image, setImage] = useState(product?.imageUrl ?? '');
  const [email, setEmail] = useState(user?.email ?? ''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false); const lock = useRef(false);
  const noun = section === 'products' ? 'product' : section === 'categories' ? 'category' : 'user';
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return; lock.current = true; setBusy(true); setError('');
    try {
      const body = { name: name.trim(), isActive: active, ...(original ? { version: original.version } : {}), ...(section === 'products' ? { categoryId: category, description: description.trim() || null, imageUrl: image.trim() || null } : {}), ...(section === 'users' ? { email: email.trim() } : {}) };
      const result = await api<Product>(`${section === 'users' ? '/admin' : '/admin/catalog'}/${section}${original ? `/${original.id}` : ''}`, original ? 'PUT' : 'POST', body);
      saved(section === 'products' && !original ? result : undefined);
    } catch (err) { setError(message(err)); } finally { lock.current = false; setBusy(false); }
  }
  return <Dialog title={`${original ? 'Edit' : 'Add'} ${noun}`} subtitle={section === 'products' && !original ? 'Create the product, then add its sellable SKUs.' : 'Keep the details clear and up to date.'} close={close} busy={busy}><form className="velo-admin-form" onSubmit={submit}>{error && <Alert>{error}</Alert>}<label>{noun === 'user' ? 'Full name' : `${label(noun[0].toUpperCase() + noun.slice(1))} name`}<input autoFocus required maxLength={160} value={name} onChange={e => setName(e.target.value)} pattern=".*\S.*" placeholder={section === 'categories' ? 'e.g. Fruit & vegetables' : section === 'products' ? 'e.g. Fresh whole milk' : 'Full name'} /></label>{section === 'products' && <><label>Category<select required value={category} onChange={e => setCategory(e.target.value)}><option value="" disabled>Select a category</option>{categories.map(c => <option key={c.id} value={c.id}>{c.name}{!c.isActive ? ' (inactive)' : ''}</option>)}</select>{!categories.length && <small>Create a category before adding a product.</small>}</label><label>Description<textarea maxLength={2000} rows={4} value={description} onChange={e => setDescription(e.target.value)} placeholder="What should your customers know?" /></label><label>Main image URL<input type="url" pattern="https://.*" title="Use an HTTPS image URL" maxLength={2048} value={image} onChange={e => setImage(e.target.value)} placeholder="https://…" /><small>A single HTTPS product image.</small></label></>}{section === 'users' && <><label>Email<input type="email" required maxLength={256} value={email} onChange={e => setEmail(e.target.value)} /></label><div className="velo-admin-readonly"><span>Phone number</span><strong>{user?.phoneNumber}</strong><small>Phone verification and staff permission grants are managed separately.</small></div></>}<label className="velo-admin-toggle-label"><input type="checkbox" checked={active} disabled={section === 'users' && original?.id === currentUser} onChange={e => setActive(e.target.checked)} /><span><strong>Active {noun}</strong><small>{section === 'users' ? 'Deactivating revokes this user’s sessions and prevents sign-in.' : 'Deactivate to hide it while keeping its history.'}</small></span></label><div className="velo-admin-form-actions"><button type="button" className="velo-admin-secondary" disabled={busy} onClick={close}>Cancel</button><button className="velo-admin-primary" disabled={busy || (section === 'products' && !categories.length)}>{busy ? 'Saving…' : !original && section === 'products' ? 'Create & add SKUs' : 'Save changes'}<Check size={16} /></button></div></form></Dialog>;
}

function Variants({ api, product: initial, close, changed }: { api: Api; product: Product; close(): void; changed(text?: string): void }) {
  const [product, setProduct] = useState(initial); const [selected, setSelected] = useState<Variant | null>(null);
  const [sku, setSku] = useState(''); const [name, setName] = useState(''); const [price, setPrice] = useState(''); const [barcode, setBarcode] = useState(''); const [active, setActive] = useState(true);
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const lock = useRef(false);
  const select = (v: Variant | null) => { setSelected(v); setSku(v?.sku ?? ''); setName(v?.name ?? ''); setPrice(v?.price.toString() ?? ''); setBarcode(v?.barcode ?? ''); setActive(v?.isActive ?? true); setError(''); };
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return; lock.current = true; setBusy(true); setError('');
    try {
      const variant = await api<Variant>(`/admin/catalog/products/${product.id}/variants${selected ? `/${selected.id}` : ''}`, selected ? 'PUT' : 'POST', { sku: sku.trim().toUpperCase(), name: name.trim(), price: Number(price), barcode: barcode.trim() || null, isActive: active, ...(selected ? { version: selected.version } : {}) });
      setProduct(p => ({ ...p, variants: selected ? p.variants.map(v => v.id === variant.id ? variant : v) : [...p.variants, variant] })); select(null); changed('SKU saved.');
    } catch (err) { setError(message(err)); } finally { lock.current = false; setBusy(false); }
  }
  return <Dialog title="Product SKUs" subtitle={product.name} close={close} busy={busy}><div className="velo-admin-variant-list">{product.variants.length ? product.variants.map(v => <button disabled={busy} className={selected?.id === v.id ? 'selected' : ''} key={v.id} onClick={() => select(v)}><span><strong>{v.name}</strong><small>{v.sku} · {v.isActive ? 'Active' : 'Inactive'}</small></span><strong>{money(v.price)}</strong><Pencil size={14} /></button>) : <p>No SKUs yet. Add one below to make this product sellable.</p>}</div><form className="velo-admin-form" onSubmit={submit}><div className="velo-admin-subhead"><h3>{selected ? 'Edit SKU' : 'Add a SKU'}</h3>{selected && <button type="button" disabled={busy} className="velo-admin-text-button" onClick={() => select(null)}><Plus size={14} /> New SKU</button>}</div>{error && <Alert>{error}</Alert>}<div className="velo-admin-form-grid"><label>SKU<input autoFocus required maxLength={64} pattern="[A-Za-z0-9][A-Za-z0-9._\-]*" value={sku} onChange={e => setSku(e.target.value)} placeholder="MILK-1L" /></label><label>Variant name<input required maxLength={160} pattern=".*\S.*" value={name} onChange={e => setName(e.target.value)} placeholder="1 litre" /></label><label>Price (EGP)<input required type="number" min="0.01" max="999999999.99" step="0.01" value={price} onChange={e => setPrice(e.target.value)} placeholder="45.50" /></label><label>Barcode (optional)<input maxLength={64} pattern="\S*" value={barcode} onChange={e => setBarcode(e.target.value)} /></label></div><label className="velo-admin-toggle-label"><input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} /><span><strong>Active SKU</strong><small>Available for publication when the product and category are active.</small></span></label><div className="velo-admin-form-actions"><button type="button" className="velo-admin-secondary" disabled={busy} onClick={close}>Done</button><button className="velo-admin-primary" disabled={busy}>{busy ? 'Saving…' : selected ? 'Save SKU' : 'Add SKU'}<Check size={16} /></button></div></form></Dialog>;
}

function OrderDetail({ api, order: initial, close, changed }: { api: Api; order: Order; close(): void; changed(text?: string): void }) {
  const [order, setOrder] = useState(initial); const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const [reason, setReason] = useState(''); const lock = useRef(false);
  useEffect(() => { const abort = new AbortController(); api<Order>(`/admin/orders/${initial.id}`, 'GET', undefined, undefined, abort.signal).then(o => { if (!abort.signal.aborted) setOrder(o); }).catch(err => { if (!abort.signal.aborted) setError(message(err)); }); return () => abort.abort(); }, [api, initial.id]);
  async function action(command: string) {
    if (lock.current) return; lock.current = true; setBusy(true); setError('');
    try { setOrder(await api<Order>(`/admin/orders/${order.id}/${command}`, 'POST', command === 'cancel' ? { reason: reason.trim() } : undefined)); changed('Order updated.'); setReason(''); }
    catch (err) { setError(message(err)); } finally { lock.current = false; setBusy(false); }
  }
  const next = order.status === 'Pending' ? ['confirm', 'Confirm order'] : order.status === 'Confirmed' ? ['prepare', 'Start preparing'] : order.status === 'Preparing' ? ['ready', 'Ready for dispatch'] : null;
  return <Dialog title={`Order #${order.id.slice(0, 8).toUpperCase()}`} subtitle={date(order.createdAt)} close={close} busy={busy}><div className="velo-admin-order-detail">{error && <Alert>{error}</Alert>}<div className="velo-admin-order-state"><Status value={order.status} /><span>{label(order.paymentMethod)} · {label(order.paymentStatus)}</span></div><section className="velo-admin-address"><h3>Delivery details</h3><strong>{order.address.recipientName}</strong><p>{order.address.phoneNumber}<br />{[order.address.street, order.address.building, order.address.floor && `Floor ${order.address.floor}`, order.address.apartment && `Apartment ${order.address.apartment}`, order.address.area, order.address.city].filter(Boolean).join(', ')}</p>{order.address.instructions && <p>{order.address.instructions}</p>}</section><h3>Order items</h3>{order.items.map(i => <div className="velo-admin-order-item" key={i.variantId}><span><strong>{i.productName}</strong><small>{i.variantName} · {i.sku} · Qty {i.quantity}</small></span><strong>{money(i.lineTotal, order.currency)}</strong></div>)}<div className="velo-admin-totals"><div><span>Subtotal</span><span>{money(order.subtotal, order.currency)}</span></div><div><span>Delivery</span><span>{money(order.deliveryFee, order.currency)}</span></div><div><strong>Total</strong><strong>{money(order.total, order.currency)}</strong></div></div><h3>Status history</h3><ol className="velo-admin-timeline">{order.history.map((h, i) => <li key={i}><strong>{label(h.status)}</strong><time>{date(h.createdAt)}</time>{h.reason && <p>{h.reason}</p>}</li>)}</ol>{order.status === 'Pending' && <div className="velo-admin-tip"><CircleHelp size={17} /><span>Confirm before {date(order.expiresAt)}. Confirmation consumes reserved stock.</span></div>}{next && <button className="velo-admin-primary velo-admin-full" disabled={busy || (order.status === 'Pending' && Date.parse(order.expiresAt) <= Date.now())} onClick={() => action(next[0])}>{busy ? 'Updating…' : next[1]}<ArrowRight size={16} /></button>}{['Pending', 'Confirmed', 'Preparing', 'ReadyForDispatch'].includes(order.status) && <form className="velo-admin-cancel-order" onSubmit={e => { e.preventDefault(); void action('cancel'); }}><label>Cancellation reason<textarea required minLength={1} maxLength={500} value={reason} onChange={e => setReason(e.target.value)} placeholder="Explain why this order needs to be cancelled" /></label><button className="velo-admin-secondary velo-admin-danger" disabled={busy || !reason.trim()}>Cancel order</button></form>}{order.status === 'ReadyForDispatch' && <p className="velo-admin-footnote">Driver assignment and delivery proof continue through the existing operations API.</p>}</div></Dialog>;
}

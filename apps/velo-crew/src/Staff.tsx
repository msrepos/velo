import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ArrowRight, ArrowUpRight, Check, CheckCheck, ChevronRight, Clock3, ClipboardList, LogOut, MapPin, Minus, Package, Plus, RefreshCw, Search, ShieldCheck, Truck, Wifi, Box, CircleCheck, ShoppingBag } from 'lucide-react';
import { createApi, money, type Page, type Staff as StaffAccess, type Tokens } from './api';
import { canComplete, demoOrders, demoVan, pickedCount, stageLabel, statuses, unitCount, type Picked, type VanOrder } from './workflow';
import './staff.css';

type Van = { id: string; code: string; name: string; type: string; isActive: boolean };
const errorText = (e: unknown) => e instanceof Error ? e.message : 'Unable to complete this action. Please retry.';
const shortId = (id: string) => id.startsWith('demo-') ? id.slice(5) : id.slice(0, 8).toUpperCase();
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const time = (value: string) => new Date(value).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });

export default function Staff() {
  const [demo, setDemo] = useState(() => new URLSearchParams(location.search).get('demo') === '1');
  const [session, setSession] = useState<Tokens | null>(null);
  const tokenRef = useRef<Tokens | null>(null);
  const [access, setAccess] = useState<StaffAccess | null>(null);
  const [authError, setAuthError] = useState('');
  const [api] = useState(() => createApi(() => tokenRef.current?.accessToken, () => { tokenRef.current = null; setSession(null); setAccess(null); setAuthError('Session expired. Please sign in again.'); }));
  useEffect(() => { document.title = 'Velo Crew · Minivan order preparation'; }, []);
  useEffect(() => {
    if (!session) return;
    const timer = setTimeout(async () => {
      try { const next = await api<Tokens>('/auth/refresh', 'POST', { refreshToken: session.refreshToken }); if (tokenRef.current === session) { tokenRef.current = next; setSession(next); } }
      catch (e) { if (tokenRef.current === session) { tokenRef.current = null; setSession(null); setAccess(null); setAuthError(errorText(e)); } }
    }, Math.max(1000, (session.expiresIn - 60) * 1000));
    return () => clearTimeout(timer);
  }, [session, api]);
  async function signedIn(tokens: Tokens) {
    const staff = await api<StaffAccess>('/auth/staff/me', 'GET', undefined, tokens.accessToken);
    if (!staff.permissions.includes('orders.manage')) throw new Error('Your account needs orders.manage permission. Ask your operator to grant staff access.');
    tokenRef.current = tokens; setAccess(staff); setSession(tokens); setAuthError('');
  }
  async function logout() {
    const old = tokenRef.current; tokenRef.current = null; setSession(null); setAccess(null); setDemo(false); history.replaceState(null, '', '/');
    if (old) await api('/auth/logout', 'POST', undefined, old.accessToken).catch(() => setAuthError('Signed out locally. Server session revocation failed.'));
  }
  if (!demo && !session) return <SignIn api={api} signedIn={signedIn} error={authError} explore={() => { history.replaceState(null, '', '/?demo=1'); setDemo(true); }} />;
  return <Workspace key={demo ? 'demo' : access?.userId} demo={demo} access={access} api={api} logout={logout} />;
}

function SignIn({ api, signedIn, error, explore }: { api: ReturnType<typeof createApi>; signedIn(t: Tokens): Promise<void>; error: string; explore(): void }) {
  const [phone, setPhone] = useState(''); const [code, setCode] = useState('');
  const [challenge, setChallenge] = useState<{ challengeId: string; developmentCode?: string } | null>(null);
  const [message, setMessage] = useState(error); const [busy, setBusy] = useState(false); const lock = useRef(false);
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return; lock.current = true; setBusy(true); setMessage('');
    let customer: Tokens | undefined; let staff: Tokens | undefined;
    try {
      if (!challenge) setChallenge(await api('/auth/otp/request', 'POST', { phoneNumber: phone }));
      else { customer = await api<Tokens>('/auth/otp/verify', 'POST', { challengeId: challenge.challengeId, code }); staff = await api<Tokens>('/auth/staff/exchange', 'POST', undefined, customer.accessToken); await signedIn(staff); }
    } catch (e) { setMessage(errorText(e)); if (customer) { setChallenge(null); setCode(''); } if (staff) await api('/auth/logout', 'POST', undefined, staff.accessToken).catch(() => {}); }
    finally { if (customer) await api('/auth/logout', 'POST', undefined, customer.accessToken).catch(() => {}); lock.current = false; setBusy(false); }
  }
  return <div className="crew crew-login"><section><img src="/logo.png" alt="Velo" /><span className="crew-eyebrow">THE MINIVAN STAFF WORKSPACE</span><h1>Good things.<br />Packed on the go<span>.</span></h1><p>Your van is your warehouse. Receive orders, pick every item, and get them ready for the doorstep.</p><div className="crew-login-steps"><span><ClipboardList /> Receive</span><ChevronRight /><span><ShoppingBag /> Prepare</span><ChevronRight /><span><CheckCheck /> Ready</span></div><small>A LITTLE MORE LOCAL. A LITTLE MORE VELO.</small></section><section><div><span className="crew-login-icon"><Truck size={30} /></span><span className="crew-eyebrow">WELCOME TO VELO CREW</span><h2>Let’s get packing.</h2><p>Sign in with your staff phone number.</p>{(message || error) && <p className="crew-error" role="alert">{message || error}</p>}<form onSubmit={submit}><label>{challenge ? 'Verification code' : 'Phone number'}<input required type={challenge ? 'text' : 'tel'} inputMode={challenge ? 'numeric' : 'tel'} autoComplete={challenge ? 'one-time-code' : 'tel'} pattern={challenge ? '[0-9]{6}' : undefined} value={challenge ? code : phone} onChange={e => challenge ? setCode(e.target.value) : setPhone(e.target.value)} placeholder={challenge ? '6-digit code' : '+20 10 1234 5678'} /></label>{challenge?.developmentCode && <p>Development code: <strong>{challenge.developmentCode}</strong></p>}<button className="crew-primary" disabled={busy}>{busy ? 'Please wait…' : challenge ? 'Verify & sign in' : 'Send verification code'}<ArrowRight size={18} /></button>{challenge && <button type="button" disabled={busy} onClick={() => { setChallenge(null); setCode(''); }}>Use a different number / resend</button>}</form><div className="crew-auth-note"><ShieldCheck size={19} /> For authorized minivan staff. Your operator manages access.</div><button className="crew-secondary crew-demo-button" onClick={explore}>Explore the interactive demo <ArrowUpRight size={16} /></button></div></section></div>;
}

function Workspace({ demo, access, api, logout }: { demo: boolean; access: StaffAccess | null; api: ReturnType<typeof createApi>; logout(): void }) {
  const [vanId, setVanId] = useState(demo ? demoVan : ''); const [vanInput, setVanInput] = useState(''); const [vans, setVans] = useState<Van[]>([]);
  const [orders, setOrders] = useState<VanOrder[]>(() => demo ? demoOrders() : []);
  const [selected, setSelected] = useState(''); const [filter, setFilter] = useState('active'); const [search, setSearch] = useState('');
  const [error, setError] = useState(''); const [notice, setNotice] = useState(''); const [busy, setBusy] = useState(false); const lock = useRef(false);
  const [loading, setLoading] = useState(false); const [revision, refresh] = useState(0); const [lastSync, setLastSync] = useState<Date | null>(null);
  const [online, setOnline] = useState(navigator.onLine);
  const [checks, setChecks] = useState<Record<string, Picked>>({});
  const storageKey = `velo-crew-picks:${demo ? 'demo' : access?.userId}:${vanId}`;
  useEffect(() => { try { setChecks(JSON.parse(sessionStorage.getItem(storageKey) || '{}')); } catch { setChecks({}); } }, [storageKey]);
  useEffect(() => { const update = () => setOnline(navigator.onLine); window.addEventListener('online', update); window.addEventListener('offline', update); return () => { window.removeEventListener('online', update); window.removeEventListener('offline', update); }; }, []);
  useEffect(() => {
    if (demo || !access?.permissions.includes('inventory.manage')) return;
    const controller = new AbortController();
    async function load() { try { const result: Van[] = []; let page = 1; let total = Infinity; while (result.length < total) { const batch = await api<Page<Van>>(`/admin/inventory/warehouses?page=${page++}&pageSize=100`, 'GET', undefined, undefined, controller.signal); result.push(...batch.items); total = batch.totalCount; if (!batch.items.length) break; } setVans(result.filter(v => v.isActive && v.type === 'Mobile')); } catch (e) { if (!controller.signal.aborted) setError(errorText(e)); } }
    void load(); return () => controller.abort();
  }, [demo, access, api]);
  useEffect(() => {
    if (demo || !vanId) return;
    const controller = new AbortController(); let active = true; let fetching = false;
    async function load() {
      if (fetching) return; fetching = true; setLoading(true);
      try {
        const batches = await Promise.all(statuses.map(async status => {
          const all: VanOrder[] = []; let page = 1; let total = Infinity;
          while (all.length < total) { const result = await api<Page<VanOrder>>(`/admin/orders?warehouseId=${vanId}&status=${status}&page=${page++}&pageSize=100`, 'GET', undefined, undefined, controller.signal); all.push(...result.items); total = result.totalCount; if (!result.items.length) break; }
          return all;
        }));
        if (active) { setOrders(batches.flat().filter(o => o.warehouseId === vanId).sort((a, b) => a.createdAt.localeCompare(b.createdAt))); setLastSync(new Date()); setError(''); }
      } catch (e) { if (active && !controller.signal.aborted) setError(errorText(e)); }
      finally { fetching = false; if (active) setLoading(false); }
    }
    void load(); const timer = setInterval(load, 15000); return () => { active = false; controller.abort(); clearInterval(timer); };
  }, [demo, vanId, revision, api]);
  const visible = orders.filter(o => (filter === 'active' ? o.status !== 'ReadyForDispatch' : filter === 'new' ? ['Pending', 'Confirmed'].includes(o.status) : filter === 'preparing' ? o.status === 'Preparing' : o.status === 'ReadyForDispatch') && `${o.id} ${o.address.recipientName} ${o.address.building}`.toLowerCase().includes(search.toLowerCase()));
  const order = visible.find(o => o.id === selected) ?? visible[0]; const picked = order ? checks[order.id] ?? {} : {};
  const van = vans.find(v => v.id === vanId); const vanName = demo ? 'MV-007' : van?.code ?? (vanId ? shortId(vanId) : 'Select your van');
  function changeVan(id: string) { if (busy) return; setVanId(id); setOrders([]); setSelected(''); setError(''); setNotice(''); }
  function pick(itemId: string, quantity: number) {
    if (!order || order.status !== 'Preparing' || busy) return;
    const next = { ...checks, [order.id]: { ...picked, [itemId]: quantity } }; setChecks(next);
    try { sessionStorage.setItem(storageKey, JSON.stringify(next)); } catch { setNotice('Checklist is available for this visit; browser storage is unavailable.'); }
  }
  async function advance() {
    if (!order || lock.current || !online || !statuses.includes(order.status as typeof statuses[number])) return;
    if (order.status === 'Preparing' && !canComplete(order, picked)) return;
    const action = order.status === 'Pending' ? 'confirm' : order.status === 'Confirmed' ? 'prepare' : 'ready';
    lock.current = true; setBusy(true); setError('');
    try {
      const next = demo ? { ...order, status: action === 'confirm' ? 'Confirmed' : action === 'prepare' ? 'Preparing' : 'ReadyForDispatch' } : await api<VanOrder>(`/admin/orders/${order.id}/${action}`, 'POST');
      setOrders(previous => previous.map(o => o.id === next.id ? next : o)); setSelected(next.id); setNotice(`Order #${shortId(next.id)} ${stageLabel(next.status).toLowerCase()}.${next.status === 'ReadyForDispatch' ? ' Ready for the delivery handoff.' : ''}`);
      if (next.status === 'Preparing') setFilter('preparing');
      if (next.status === 'ReadyForDispatch') setFilter('prepared');
    } catch (e) { setError(errorText(e)); refresh(n => n + 1); }
    finally { lock.current = false; setBusy(false); }
  }
  return <div className="crew"><a className="crew-skip" href="#crew-main">Skip to orders</a><aside className="crew-sidebar"><a href="/" className="crew-brand"><img src="/logo.png" alt="Velo" /><span>CREW</span></a><div className="crew-workspace"><span><Truck size={21} /></span><div><strong>Minivan workspace</strong><small>Fulfillment on the move</small></div></div><span className="crew-nav-label">YOUR WORKSPACE</span><nav><a href="#crew-main" className="active"><ClipboardList size={20} /> Order preparation <span>{orders.filter(o => o.status !== 'ReadyForDispatch').length}</span></a></nav><div className="crew-van-card"><span className="crew-eyebrow">YOUR MOVING WAREHOUSE</span><Truck size={65} strokeWidth={1} /><h3>{vanName}</h3><p>A small space.<br />A big part of the neighborhood.</p><div><Box size={15} /> Top 100 selling products</div></div><div className="crew-sidebar-foot"><span><ShieldCheck size={17} /> Staff workspace</span><button onClick={logout}><LogOut size={17} /> {demo ? 'Exit demo' : 'Sign out'}</button></div></aside><div className="crew-shell"><header className="crew-topbar"><span>Workspace <ChevronRight size={14} /> <strong>Order preparation</strong></span><div><span className={`crew-connection ${online ? '' : 'offline'}`}><i />{demo ? 'Demo workspace' : online ? 'Connected' : 'Offline'}</span><span className="crew-avatar">VC</span><strong>Velo crew</strong><button onClick={logout} aria-label="Sign out"><LogOut size={17} /></button></div></header><main id="crew-main"><div className="crew-heading"><div><span className="crew-eyebrow">GOOD THINGS START IN YOUR VAN</span><h1>Let’s get orders ready<span>.</span></h1><p>A little care in every bag. A faster doorstep delivery.</p></div><div className="crew-van-select"><Truck size={20} /><div><small>MOVING WAREHOUSE</small><strong>{vanName}</strong></div>{demo ? <span className="crew-tag">Madinaty</span> : vans.length > 0 ? <select aria-label="Select minivan" disabled={busy} value={vanId} onChange={e => changeVan(e.target.value)}><option value="">Choose van</option>{vans.map(v => <option key={v.id} value={v.id}>{v.code} · {v.name}</option>)}</select> : null}</div></div>
      {demo && <div className="crew-demo"><ShieldCheck size={17} /><span><strong>Interactive demo.</strong> Sample orders and van. Changes stay in this browser and do not affect live operations.</span><button onClick={() => { setOrders(demoOrders()); setChecks({}); sessionStorage.removeItem(storageKey); setFilter('active'); setNotice('Demo reset.'); }}>Reset demo</button></div>}
      {!demo && !vanId && <form className="crew-setup" onSubmit={e => { e.preventDefault(); if (uuid.test(vanInput)) changeVan(vanInput); else setError('Enter the complete warehouse UUID supplied by your operator.'); }}><Truck /><div><strong>Connect to your minivan</strong><p>Use the unique warehouse ID supplied by your operator.{!access?.permissions.includes('inventory.manage') && ' Van lookup requires inventory.manage permission.'}</p></div><input aria-label="Minivan warehouse ID" placeholder="Warehouse UUID" value={vanInput} onChange={e => setVanInput(e.target.value.trim())} required /><button className="crew-primary">Connect <ArrowRight size={16} /></button></form>}
      {vanId && <div className="crew-identity">Warehouse ID <code>{vanId}</code></div>}
      <div className="crew-stats">{[{ label: 'Awaiting preparation', value: orders.filter(o => ['Pending', 'Confirmed'].includes(o.status)).length, icon: ClipboardList, tone: 'coral', note: 'Ready for your attention' }, { label: 'Being prepared', value: orders.filter(o => o.status === 'Preparing').length, icon: ShoppingBag, tone: 'amber', note: 'A little care in every item' }, { label: 'Prepared & ready', value: orders.filter(o => o.status === 'ReadyForDispatch').length, icon: CheckCheck, tone: 'green', note: 'Ready for the next handoff' }].map(({ label, value, icon: Icon, tone, note }) => <div key={label}><span className={`crew-stat-icon ${tone}`}><Icon size={23} /></span><div><span>{label}</span><strong>{value.toString().padStart(2, '0')}</strong><small>{note}</small></div><ArrowUpRight size={17} /></div>)}</div>
      {error && <div className="crew-error" role="alert">{error} <button onClick={() => refresh(n => n + 1)}>Retry</button></div>}{!online && <div className="crew-error" role="alert">You’re offline. Reconnect before receiving or completing an order.</div>}{notice && <div className="crew-notice" role="status"><CircleCheck size={17} />{notice}</div>}
      <div className="crew-board"><section className="crew-queue"><div className="crew-section-head"><div><h2>Order queue <span>{orders.length}</span></h2><p>Your van’s orders, all in one place.</p></div><button disabled={loading} aria-label="Refresh orders" onClick={() => { if (demo) setNotice('Demo queue is up to date.'); else refresh(n => n + 1); }}><RefreshCw size={17} className={loading ? 'crew-spin' : ''} /></button></div><div className="crew-tabs" role="tablist" aria-label="Order status">{[['active', 'Active'], ['new', 'New'], ['preparing', 'Preparing'], ['prepared', 'Prepared']].map(([key, title]) => <button key={key} role="tab" aria-selected={filter === key} onClick={() => setFilter(key)}>{title}</button>)}</div><label className="crew-search"><Search size={17} /><input aria-label="Search orders" placeholder="Search order or customer…" value={search} onChange={e => setSearch(e.target.value)} /></label><div className="crew-order-list">{visible.map(o => <button className={`crew-order-card ${order?.id === o.id ? 'selected' : ''}`} key={o.id} onClick={() => setSelected(o.id)} aria-pressed={order?.id === o.id}><div><strong>#{shortId(o.id)}</strong><span className={`crew-status ${o.status}`}>{stageLabel(o.status)}</span></div><h3>{o.address.recipientName}</h3><p><MapPin size={13} />{o.address.building} · {o.address.area}</p><footer><span><Package size={13} />{unitCount(o)} items <i />{money(o.total, o.currency)}</span><span><Clock3 size={13} />{time(o.createdAt)}</span></footer></button>)}{!visible.length && <div className="crew-empty"><ClipboardList size={35} /><h3>{loading ? 'Loading your queue…' : search ? 'No matching orders' : 'All clear for now'}</h3><p>{search ? 'Try another order number or customer.' : vanId ? 'Orders assigned to this van will appear here.' : 'Connect your minivan to receive orders.'}</p></div>}</div><div className="crew-queue-foot"><Wifi size={13} />{demo ? 'Sample orders · demo mode' : lastSync ? `Last synced ${lastSync.toLocaleTimeString('en-GB')} · every 15 sec` : 'Waiting for a connected van'}</div></section>
      <section className="crew-detail" aria-label="Selected order">{order ? <><div className="crew-detail-head"><div><span className="crew-eyebrow">LET’S PACK SOMETHING GOOD</span><h2>Order #{shortId(order.id)}</h2><p>Received at {time(order.createdAt)} <span>·</span> {order.items.length} products <span>·</span> {unitCount(order)} items</p></div><span className={`crew-status ${order.status}`}>{stageLabel(order.status)}</span></div><div className="crew-progress-steps">{['Received', 'Preparing', 'Prepared'].map((title, i) => { const stage = order.status === 'Pending' ? -1 : order.status === 'Confirmed' ? 0 : order.status === 'Preparing' ? 1 : 2; return <div className={i <= stage ? 'done' : ''} key={title}><span>{i < stage ? <Check size={13} /> : i + 1}</span>{title}{i < 2 && <i />}</div>; })}</div><div className="crew-destination"><span><MapPin size={21} /></span><div><small>DELIVERING TO</small><strong>{order.address.recipientName}</strong><p>{order.address.building} · {order.address.street} · {order.address.area}</p><p>{[order.address.floor && `Floor ${order.address.floor}`, order.address.apartment && `Apartment ${order.address.apartment}`].filter(Boolean).join(' · ')}</p></div><span className="crew-destination-icon"><Truck size={25} /></span></div><div className="crew-motion"><Truck size={17} /><p>{demo ? <><strong>Driver on the way</strong><span>Sample journey · preparation continues while the van moves.</span></> : <><strong>Pack while the van is on the move</strong><span>Preparation and travel happen in parallel. Live driver location is not connected.</span></>}</p></div>{order.address.instructions && <div className="crew-instruction"><strong>Customer note</strong><p>{order.address.instructions}</p></div>}<div className="crew-pick-heading"><h3>Picking checklist <span>{order.items.length} products</span></h3><span>{order.status === 'ReadyForDispatch' ? unitCount(order) : pickedCount(order, picked)} / {unitCount(order)} packed</span></div><div className="crew-meter"><span style={{ width: `${order.status === 'ReadyForDispatch' ? 100 : unitCount(order) ? pickedCount(order, picked) / unitCount(order) * 100 : 0}%` }} /></div><div className="crew-products">{order.items.map((item, index) => { const count = order.status === 'ReadyForDispatch' ? item.quantity : Math.min(item.quantity, Math.max(0, picked[item.variantId] || 0)); const complete = count === item.quantity; return <div className={`crew-product ${complete ? 'picked' : ''}`} key={item.variantId}><span className="crew-product-number">{String(index + 1).padStart(2, '0')}</span><div className="crew-product-info"><strong>{item.productName}</strong><span>{item.variantName}</span><small>SKU {item.sku}</small></div><div className="crew-quantity"><button disabled={order.status !== 'Preparing' || busy || count === 0} aria-label={`Remove one ${item.productName}`} onClick={() => pick(item.variantId, count - 1)}><Minus size={14} /></button><span>{count}<small>/{item.quantity}</small></span><button disabled={order.status !== 'Preparing' || busy || complete} aria-label={`Pick one ${item.productName}`} onClick={() => pick(item.variantId, count + 1)}><Plus size={14} /></button></div><button className={`crew-item-check ${complete ? 'checked' : ''}`} aria-label={`${complete ? 'Unpick' : 'Pick all'} ${item.productName}`} aria-pressed={complete} disabled={order.status !== 'Preparing' || busy} onClick={() => pick(item.variantId, complete ? 0 : item.quantity)}><Check size={16} /></button></div>; })}</div><div className="crew-pack-note"><ShieldCheck size={17} /><span>Check quantities and product condition. Keep chilled items together.</span></div><div className="crew-complete"><div><strong>{order.status === 'ReadyForDispatch' ? 'Packed with care. Ready to go.' : order.status === 'Pending' ? 'A new order for your van.' : order.status === 'Confirmed' ? 'Ready when you are.' : canComplete(order, picked) ? 'Every item is accounted for.' : 'A little closer to the doorstep.'}</strong><p>{order.status === 'ReadyForDispatch' ? 'Preparation is complete. Delivery is handled separately.' : order.status === 'Preparing' ? 'Pack every item to complete preparation.' : 'Receive the order, then start your picking checklist.'}</p></div>{order.status === 'ReadyForDispatch' ? <span className="crew-ready"><CheckCheck size={20} /> Prepared</span> : <button className="crew-primary" disabled={busy || !online || (order.status === 'Preparing' && !canComplete(order, picked))} onClick={advance}>{busy ? 'Saving…' : order.status === 'Pending' ? 'Receive order' : order.status === 'Confirmed' ? 'Start preparing' : 'Mark as prepared'}{order.status === 'Preparing' ? <CheckCheck size={18} /> : <ArrowRight size={18} />}</button>}</div><p className="crew-storage-note">Picking progress is saved in this tab. Order status is {demo ? 'simulated for this demo' : 'saved to Velo'}.</p></> : <div className="crew-empty crew-detail-empty"><ShoppingBag size={46} strokeWidth={1.2} /><h2>Every good delivery starts here.</h2><p>Select an order to see its items and start preparing.</p></div>}</section></div><footer className="crew-footer"><span>Velo crew <i>·</i> A little care. A little more local.</span><span><Truck size={14} /> Prepared on the move.</span></footer></main></div></div>;
}


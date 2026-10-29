import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Check, ChevronLeft, ChevronRight, Pencil, Plus, RefreshCw, Truck, Users, X } from 'lucide-react';
import type { Page, Staff, User } from './api';
import { allPages, availableTeam, type Api, type Dispatch, type Minivan, type Person } from './fleetApi';
import './fleet.css';

type Section = 'drivers' | 'crew' | 'minivans' | 'dispatching';
const root = '/admin/delivery';
const message = (error: unknown) => error instanceof Error ? error.message : 'Unable to save. Please try again.';
const date = (value: string) => new Date(value).toLocaleString('en-GB');
function Alert({ children }: { children: ReactNode }) { return <div className="velo-admin-alert" role="alert">{children}</div>; }
function State({ active }: { active: boolean }) { return <span className={`velo-admin-badge ${active ? 'green' : ''}`}>{active ? 'Active' : 'Inactive'}</span>; }

function useRows<T>(api: Api, path: string) {
  const [page, setPage] = useState(1);
  const [revision, refresh] = useState(0);
  const [rows, setRows] = useState<Page<T> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  useEffect(() => {
    const abort = new AbortController(); setLoading(true); setRows(null); setError('');
    api<Page<T>>(`${path}${path.includes('?') ? '&' : '?'}page=${page}&pageSize=20`, 'GET', undefined, undefined, abort.signal)
      .then(data => { if (!abort.signal.aborted) { if (!data.items.length && page > 1) setPage(p => p - 1); else setRows(data); } })
      .catch(err => { if (!abort.signal.aborted) setError(message(err)); })
      .finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [api, path, page, revision]);
  return { rows, loading, error, page, setPage, refresh: () => refresh(n => n + 1) };
}
type ListState = ReturnType<typeof useRows<unknown>>;
function ListPanel({ title, list, add, children }: { title: string; list: ListState; add?: () => void; children: ReactNode }) {
  return <section className="velo-admin-panel velo-admin-records"><div className="velo-admin-panel-heading"><div><h2>{title} <span className="velo-admin-badge">{list.rows?.totalCount ?? '—'}</span></h2><p>Live operations records</p></div><div className="velo-admin-actions"><button className="velo-admin-secondary" disabled={list.loading} onClick={list.refresh}><RefreshCw size={15} /> Refresh</button>{add && <button className="velo-admin-primary" onClick={add}><Plus size={16} /> Add {title === 'All minivans' ? 'minivan' : title === 'All drivers' ? 'driver' : title === 'All crew' ? 'crew member' : 'dispatch'}</button>}</div></div>
    {list.error && <Alert>{list.error}</Alert>}
    {list.loading ? <div className="velo-admin-empty" role="status">Loading…</div> : !list.rows?.items.length ? <div className="velo-admin-empty"><Truck size={32} /><h3>{list.error ? 'Records unavailable' : 'No records yet'}</h3><p>{list.error ? 'Use Refresh to retry.' : 'Add your first record to get started.'}</p></div> : <div className="velo-admin-table-scroll">{children}</div>}
    <div className="velo-admin-pagination"><span>{list.rows?.totalCount ?? '—'} records</span><div><button className="velo-admin-icon" aria-label="Previous page" disabled={list.loading || list.page === 1} onClick={() => list.setPage(p => p - 1)}><ChevronLeft size={17} /></button><span>Page {list.page}</span><button className="velo-admin-icon" aria-label="Next page" disabled={list.loading || !list.rows || list.page * 20 >= list.rows.totalCount} onClick={() => list.setPage(p => p + 1)}><ChevronRight size={17} /></button></div></div>
  </section>;
}
function FormPanel({ title, busy, close, children }: { title: string; busy: boolean; close(): void; children: ReactNode }) {
  return <section className="velo-admin-panel velo-admin-fleet-editor" aria-label={title}><div className="velo-admin-panel-heading"><h2>{title}</h2><button className="velo-admin-icon" disabled={busy} aria-label="Close editor" onClick={close}><X size={20} /></button></div>{children}</section>;
}
function FormActions({ busy, close }: { busy: boolean; close(): void }) {
  return <div className="velo-admin-form-actions"><button className="velo-admin-secondary" type="button" disabled={busy} onClick={close}>Cancel</button><button className="velo-admin-primary" disabled={busy}>{busy ? 'Saving…' : 'Save'}<Check size={16} /></button></div>;
}

export function FleetPage({ api, section, staff }: { api: Api; section: Section; staff: Staff }) {
  return section === 'drivers' || section === 'crew' ? <PeoplePage api={api} section={section} staff={staff} />
    : section === 'minivans' ? <MinivansPage api={api} staff={staff} /> : <DispatchPage api={api} />;
}

function PeoplePage({ api, section, staff }: { api: Api; section: 'drivers' | 'crew'; staff: Staff }) {
  const list = useRows<Person>(api, `${root}/${section}`);
  const [editing, setEditing] = useState<Person | 'new' | null>(null);
  const [saved, setSaved] = useState('');
  return <>{saved && <p className="velo-admin-fleet-notice" role="status">{saved}</p>}
    <p className="velo-admin-footnote">Register an existing phone-verified account. Crew membership and staff permissions are managed separately.</p>
    {editing && <PersonForm key={editing === 'new' ? 'new' : editing.id} api={api} section={section} person={editing} staff={staff} close={() => setEditing(null)} saved={() => { setEditing(null); setSaved('Registration saved.'); list.refresh(); }} />}
    <ListPanel title={`All ${section}`} list={list} add={() => { setSaved(''); setEditing('new'); }}><table><thead><tr><th>Name</th><th>Account</th><th>Status</th><th>Manage</th></tr></thead><tbody>{list.rows?.items.map(p => <tr key={p.id}><td><strong>{p.name}</strong><small>{section === 'drivers' ? 'Driver' : 'Preparation crew'}</small></td><td><code>{p.userId}</code></td><td><State active={p.isActive} /></td><td><button className="velo-admin-row-link" onClick={() => { setSaved(''); setEditing(p); }}><Pencil size={14} /> Edit</button></td></tr>)}</tbody></table></ListPanel>
  </>;
}

function PersonForm({ api, section, person, staff, close, saved }: { api: Api; section: 'drivers' | 'crew'; person: Person | 'new'; staff: Staff; close(): void; saved(): void }) {
  const existing = person === 'new' ? null : person;
  const [name, setName] = useState(existing?.name ?? '');
  const [userId, setUserId] = useState(existing?.userId ?? '');
  const [active, setActive] = useState(existing?.isActive ?? true);
  const [search, setSearch] = useState('');
  const [accounts, setAccounts] = useState<Page<User> | null>(null);
  const [busy, setBusy] = useState(false); const [finding, setFinding] = useState(false); const [error, setError] = useState('');
  const lock = useRef(false); const lookup = useRef<AbortController | null>(null);
  useEffect(() => () => lookup.current?.abort(), []);
  async function find() {
    lookup.current?.abort(); const abort = new AbortController(); lookup.current = abort; setFinding(true); setError(''); setAccounts(null);
    try { const result = await api<Page<User>>(`/admin/users?search=${encodeURIComponent(search.trim())}&pageSize=20`, 'GET', undefined, undefined, abort.signal); if (!abort.signal.aborted) setAccounts(result); }
    catch (err) { if (!abort.signal.aborted) setError(message(err)); }
    finally { if (!abort.signal.aborted) setFinding(false); }
  }
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return; lock.current = true; setBusy(true); setError('');
    try { await api(`${root}/${section}${existing ? `/${existing.id}` : ''}`, existing ? 'PUT' : 'POST', existing ? { name: name.trim(), isActive: active, version: existing.version } : { name: name.trim(), userId: userId.trim() }); saved(); }
    catch (err) { setError(message(err)); } finally { lock.current = false; setBusy(false); }
  }
  return <FormPanel title={`${existing ? 'Edit' : 'Register'} ${section === 'drivers' ? 'driver' : 'crew member'}`} busy={busy} close={close}><form className="velo-admin-form" onSubmit={submit}><fieldset disabled={busy} className="velo-admin-fleet-fieldset">{error && <Alert>{error}</Alert>}
    {!existing && staff.permissions.includes('users.manage') && <div className="velo-admin-fleet-lookup"><label>Find account by phone or name<div className="velo-admin-fleet-search"><input value={search} maxLength={100} onChange={e => setSearch(e.target.value)} placeholder="+201…" /><button type="button" className="velo-admin-secondary" disabled={finding || !search.trim()} onClick={() => void find()}>{finding ? 'Searching…' : 'Find account'}</button></div></label>{accounts && <><p>{accounts.totalCount} matching accounts. Showing up to 20; refine your search if needed.</p>{accounts.items.map(u => <button type="button" key={u.id} className={`velo-admin-fleet-account ${userId === u.id ? 'selected' : ''}`} disabled={!u.isActive || !u.phoneNumberConfirmed} onClick={() => { setUserId(u.id); if (!name) setName(u.name ?? ''); }}><Users size={17} /><span>{u.name || 'Unnamed account'}<small>{u.phoneNumber} · {u.phoneNumberConfirmed ? 'Verified' : 'Unverified'}{!u.isActive && ' · Inactive'}</small></span>{userId === u.id && <Check size={16} />}</button>)}</>}</div>}
    <div className="velo-admin-form-grid"><label>Name<input autoFocus required maxLength={160} pattern=".*\S.*" value={name} onChange={e => setName(e.target.value)} /></label><label>Verified user ID<input required readOnly={!!existing} value={userId} pattern="[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}" onChange={e => setUserId(e.target.value)} placeholder="User UUID" /></label></div>
    {existing && <label className="velo-admin-toggle-label"><input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} /><span>Active member</span></label>}
    <FormActions busy={busy} close={close} /></fieldset></form></FormPanel>;
}

function MinivansPage({ api, staff }: { api: Api; staff: Staff }) {
  const list = useRows<Minivan>(api, `${root}/minivans`);
  const [editing, setEditing] = useState<Minivan | 'new' | null>(null);
  const [saved, setSaved] = useState(false);
  const canEdit = staff.permissions.includes('inventory.manage');
  return <>{saved && <p className="velo-admin-fleet-notice" role="status">Minivan saved.</p>}<p className="velo-admin-footnote">Each minivan uses its Mobile warehouse inventory. The van code is its unique fleet identifier.{!canEdit && ' Add or edit requires inventory.manage permission.'}</p>
    {editing && <MinivanForm key={editing === 'new' ? 'new' : editing.id} api={api} van={editing} close={() => setEditing(null)} saved={() => { setEditing(null); setSaved(true); list.refresh(); }} />}
    <ListPanel title="All minivans" list={list} add={canEdit ? () => { setSaved(false); setEditing('new'); } : undefined}><table><thead><tr><th>Minivan</th><th>Base / location</th><th>Warehouse ID</th><th>Status</th>{canEdit && <th>Manage</th>}</tr></thead><tbody>{list.rows?.items.map(v => <tr key={v.id}><td><strong>{v.name}</strong><small>{v.code}</small></td><td>{v.address || '—'}<small>{v.latitude != null && v.longitude != null ? `${v.latitude}, ${v.longitude}` : 'Location not set'}</small></td><td><code>{v.id}</code></td><td><State active={v.isActive} /></td>{canEdit && <td><button className="velo-admin-row-link" onClick={() => { setSaved(false); setEditing(v); }}><Pencil size={14} /> Edit</button></td>}</tr>)}</tbody></table></ListPanel>
  </>;
}

function MinivanForm({ api, van, close, saved }: { api: Api; van: Minivan | 'new'; close(): void; saved(): void }) {
  const existing = van === 'new' ? null : van;
  const [name, setName] = useState(existing?.name ?? ''); const [code, setCode] = useState(existing?.code ?? '');
  const [address, setAddress] = useState(existing?.address ?? '');
  const [latitude, setLatitude] = useState(existing?.latitude?.toString() ?? ''); const [longitude, setLongitude] = useState(existing?.longitude?.toString() ?? '');
  const [active, setActive] = useState(existing?.isActive ?? true);
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const lock = useRef(false);
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return;
    if (!!latitude !== !!longitude) { setError('Enter both latitude and longitude, or leave both empty.'); return; }
    lock.current = true; setBusy(true); setError('');
    try { await api(`/admin/inventory/warehouses${existing ? `/${existing.id}` : ''}`, existing ? 'PUT' : 'POST', { name: name.trim(), code: code.trim().toUpperCase(), type: 'Mobile', address: address.trim() || null, latitude: latitude ? Number(latitude) : null, longitude: longitude ? Number(longitude) : null, isActive: active, ...(existing ? { version: existing.version } : {}) }); saved(); }
    catch (err) { setError(message(err)); } finally { lock.current = false; setBusy(false); }
  }
  return <FormPanel title={existing ? 'Edit minivan' : 'Register minivan'} busy={busy} close={close}><form className="velo-admin-form" onSubmit={submit}><fieldset disabled={busy} className="velo-admin-fleet-fieldset">{error && <Alert>{error}</Alert>}<div className="velo-admin-form-grid"><label>Van name<input autoFocus required maxLength={160} pattern=".*\S.*" value={name} onChange={e => setName(e.target.value)} /></label><label>Unique van code<input required maxLength={32} pattern="[A-Za-z0-9][A-Za-z0-9_\-]{0,31}" value={code} onChange={e => setCode(e.target.value)} placeholder="VAN-001" /></label><label>Base address<input maxLength={500} value={address} onChange={e => setAddress(e.target.value)} /></label><div /><label>Latitude<input type="number" min="-90" max="90" step="0.000001" value={latitude} onChange={e => setLatitude(e.target.value)} /></label><label>Longitude<input type="number" min="-180" max="180" step="0.000001" value={longitude} onChange={e => setLongitude(e.target.value)} /></label></div><label className="velo-admin-toggle-label"><input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} /><span>Active mobile warehouse</span></label><FormActions busy={busy} close={close} /></fieldset></form></FormPanel>;
}

function DispatchPage({ api }: { api: Api }) {
  const [filter, setFilter] = useState(false);
  const list = useRows<Dispatch>(api, `${root}/dispatches?activeOnly=${filter}`);
  const [adding, setAdding] = useState(false); const [ending, setEnding] = useState<Dispatch | null>(null);
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const [saved, setSaved] = useState(''); const lock = useRef(false);
  async function end() {
    if (!ending || lock.current) return; lock.current = true; setBusy(true); setError('');
    try { await api(`${root}/dispatches/${ending.id}/end`, 'POST', { version: ending.version }); setEnding(null); setSaved('Shift ended. The team and minivan are available again.'); list.refresh(); }
    catch (err) { setError(message(err)); } finally { lock.current = false; setBusy(false); }
  }
  return <>{saved && <p className="velo-admin-fleet-notice" role="status">{saved}</p>}{error && <Alert>{error}</Alert>}
    <p className="velo-admin-footnote">One minivan + one driver + one crew member per active shift. Finish active deliveries before ending a shift.</p>
    <label className="velo-admin-fleet-filter"><input type="checkbox" checked={filter} onChange={e => { setFilter(e.target.checked); list.setPage(1); }} /> Show active shifts only</label>
    {adding && <DispatchForm api={api} close={() => setAdding(false)} saved={() => { setAdding(false); setSaved('Team dispatched.'); list.refresh(); }} />}
    {ending && <section className="velo-admin-panel velo-admin-fleet-confirm" aria-label="End shift"><h3>End shift for {ending.vanName}?</h3><p>{ending.driverName} and {ending.crewMemberName} will become available for another shift.</p><div className="velo-admin-actions"><button disabled={busy} className="velo-admin-secondary" onClick={() => setEnding(null)}>Keep active</button><button disabled={busy} className="velo-admin-primary" onClick={() => void end()}>{busy ? 'Ending…' : 'End shift'}</button></div></section>}
    <ListPanel title="Dispatch history" list={list} add={() => { setSaved(''); setAdding(true); }}><table><thead><tr><th>Minivan</th><th>Driver</th><th>Crew member</th><th>Shift</th><th>Status</th><th>Manage</th></tr></thead><tbody>{list.rows?.items.map(d => <tr key={d.id}><td><strong>{d.vanName}</strong><small>{d.vanCode}</small></td><td>{d.driverName}</td><td>{d.crewMemberName}</td><td>{date(d.startedAt)}<small>{d.endedAt ? `Ended ${date(d.endedAt)}` : 'In progress'}</small></td><td><span className={`velo-admin-badge ${d.endedAt ? '' : 'green'}`}>{d.endedAt ? 'Ended' : 'Active'}</span></td><td>{!d.endedAt && <button className="velo-admin-row-link" disabled={busy} onClick={() => { setError(''); setEnding(d); }}>End shift</button>}</td></tr>)}</tbody></table></ListPanel>
  </>;
}

function DispatchForm({ api, close, saved }: { api: Api; close(): void; saved(): void }) {
  const [resources, setResources] = useState<{ drivers: Person[]; crew: Person[]; vans: Minivan[] } | null>(null);
  const [driver, setDriver] = useState(''); const [crew, setCrew] = useState(''); const [van, setVan] = useState('');
  const [revision, refresh] = useState(0); const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const lock = useRef(false); const operation = useRef<{ payload: string; id: string } | null>(null);
  useEffect(() => {
    const abort = new AbortController(); setLoading(true); setResources(null); setError(''); setDriver(''); setCrew(''); setVan('');
    Promise.all([allPages<Person>(api, `${root}/drivers`, abort.signal), allPages<Person>(api, `${root}/crew`, abort.signal), allPages<Minivan>(api, `${root}/minivans`, abort.signal), allPages<Dispatch>(api, `${root}/dispatches?activeOnly=true`, abort.signal)])
      .then(([drivers, members, vans, dispatches]) => { if (!abort.signal.aborted) setResources(availableTeam(drivers, members, vans, dispatches)); })
      .catch(err => { if (!abort.signal.aborted) setError(message(err)); }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [api, revision]);
  const driverUser = resources?.drivers.find(p => p.id === driver)?.userId;
  const crewOptions = resources?.crew.filter(p => p.userId !== driverUser) ?? [];
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return; lock.current = true; setBusy(true); setError('');
    const payload = JSON.stringify({ warehouseId: van, driverId: driver, crewMemberId: crew });
    if (operation.current?.payload !== payload) operation.current = { payload, id: crypto.randomUUID() };
    try { await api(`${root}/dispatches`, 'POST', { ...JSON.parse(payload), operationId: operation.current.id }); saved(); }
    catch (err) { setError(message(err)); } finally { lock.current = false; setBusy(false); }
  }
  return <FormPanel title="Dispatch a team" busy={busy} close={close}><form className="velo-admin-form" onSubmit={submit}><fieldset disabled={busy || loading} className="velo-admin-fleet-fieldset">{error && <Alert>{error}</Alert>}<p>Choose the available minivan and the two people on board. This starts their shift.</p>
    {loading ? <p role="status">Loading available teams…</p> : <><div className="velo-admin-form-grid"><label>Minivan<select required value={van} onChange={e => setVan(e.target.value)}><option value="">Select minivan</option>{resources?.vans.map(v => <option key={v.id} value={v.id}>{v.code} · {v.name}</option>)}</select></label><label>Driver<select required value={driver} onChange={e => { setDriver(e.target.value); setCrew(''); }}><option value="">Select driver</option>{resources?.drivers.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label><label>Crew member<select required value={crew} onChange={e => setCrew(e.target.value)}><option value="">Select crew member</option>{crewOptions.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label></div>{resources && (!resources.vans.length || !resources.drivers.length || !crewOptions.length) && <p>No complete team available. Register active members and minivans, or end their existing shifts.</p>}</>}
    <button className="velo-admin-secondary" type="button" disabled={busy || loading} onClick={() => refresh(n => n + 1)}><RefreshCw size={15} /> Refresh availability</button><FormActions busy={busy || loading || !resources} close={close} /></fieldset></form></FormPanel>;
}

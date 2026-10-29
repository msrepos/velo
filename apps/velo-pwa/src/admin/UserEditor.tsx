import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Check, X } from 'lucide-react';
import { type User, type UserRole } from './api';
import type { Api } from './fleetApi';

export const userRoles: UserRole[] = ['Customer', 'Admin', 'Driver', 'Crew'];
export const roleDescriptions: Record<UserRole, string> = {
  Customer: 'Shop and manage their own orders. No staff or fleet access.',
  Admin: 'Manage users, catalog, inventory, orders, delivery, and payments.',
  Driver: 'Access assigned deliveries. A driver profile is created automatically.',
  Crew: 'Prepare orders in Velo Crew. A crew profile and preparation access are created automatically.'
};

export function UserEditor({ api, user, currentUser, close, saved }: { api: Api; user: User | 'new'; currentUser: string; close(): void; saved(): void }) {
  const existing = user === 'new' ? null : user;
  const [name, setName] = useState(existing?.name ?? '');
  const [email, setEmail] = useState(existing?.email ?? '');
  const [phone, setPhone] = useState(existing?.phoneNumber ?? '');
  const [role, setRole] = useState<UserRole>(existing?.role ?? 'Customer');
  const [active, setActive] = useState(existing?.isActive ?? true);
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const lock = useRef(false); const dialog = useRef<HTMLDialogElement>(null); const title = useId();
  const self = existing?.id === currentUser;
  useEffect(() => { const element = dialog.current!; element.showModal(); return () => element.close(); }, []);
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return; lock.current = true; setBusy(true); setError('');
    try {
      await api<User>(`/admin/users${existing ? `/${existing.id}` : ''}`, existing ? 'PUT' : 'POST', {
        name: name.trim(), email: email.trim(), role, isActive: active,
        ...(existing ? { version: existing.version } : { phoneNumber: phone.trim() })
      }); saved();
    } catch (err) { setError(err instanceof Error ? err.message : 'Unable to save this user. Please try again.'); }
    finally { lock.current = false; setBusy(false); }
  }
  return <dialog ref={dialog} className="velo-admin-dialog" aria-labelledby={title} onCancel={e => { e.preventDefault(); if (!busy) close(); }}>
    <div className="velo-admin-dialog-head"><div><span className="velo-admin-eyebrow">VELO WORKSPACE</span><h2 id={title}>{existing ? 'Edit user' : 'Add user'}</h2><p>{existing ? 'Manage their profile and account role.' : 'Create an account and choose what they can do.'}</p></div><button className="velo-admin-icon" aria-label="Close panel" disabled={busy} onClick={close}><X size={21} /></button></div>
    <form className="velo-admin-form" onSubmit={submit}><fieldset className="velo-admin-fleet-fieldset" disabled={busy}>
      {error && <div className="velo-admin-alert" role="alert">{error}</div>}
      <label>Full name<input autoFocus required maxLength={160} pattern=".*\S.*" autoComplete="name" value={name} onChange={e => setName(e.target.value)} /></label>
      <div className="velo-admin-form-grid"><label>Email<input required type="email" autoComplete="email" maxLength={256} value={email} onChange={e => setEmail(e.target.value)} /></label><label>Phone number<input required type="tel" autoComplete="tel" maxLength={40} readOnly={!!existing} placeholder="+20 10 1234 5678" value={phone} onChange={e => setPhone(e.target.value)} /><small>{existing ? 'The verified sign-in number cannot be changed here.' : 'Include the country code. The user verifies ownership by OTP on first sign-in.'}</small></label></div>
      <label>Role<select required disabled={self} value={role} onChange={e => setRole(e.target.value as UserRole)}>{userRoles.map(value => <option key={value}>{value}</option>)}</select><small>{roleDescriptions[role]}</small></label>
      {self && <p className="velo-admin-footnote">Another administrator must change your own role or deactivate your account.</p>}
      {!existing && <p className="velo-admin-footnote">The account starts with an unverified phone. No password is needed; sign-in uses OTP. Fleet assignment requires phone verification.</p>}
      {existing && role !== existing.role && <p className="velo-admin-footnote">Changing the role updates staff permissions and fleet membership, and signs the user out. Historical records are retained.</p>}
      <label className="velo-admin-toggle-label"><input type="checkbox" disabled={self} checked={active} onChange={e => setActive(e.target.checked)} /><span><strong>Active user</strong><small>Inactive accounts cannot sign in. Deactivation revokes existing sessions.</small></span></label>
      <div className="velo-admin-form-actions"><button type="button" className="velo-admin-secondary" disabled={busy} onClick={close}>Cancel</button><button className="velo-admin-primary" disabled={busy}>{busy ? 'Saving…' : existing ? 'Save changes' : 'Create user'}<Check size={16} /></button></div>
    </fieldset></form>
  </dialog>;
}

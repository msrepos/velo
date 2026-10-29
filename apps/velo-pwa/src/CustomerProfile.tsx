import { useId, useState } from 'react';
import { ArrowRight, CheckCircle2, CreditCard, MapPin, Plus, ShieldCheck } from 'lucide-react';
import { money, type Address, type PaymentMethod } from './catalog';
import { instaPayNumber, paymentLabels, removeAddress, saveAddress, type Profile, type SavedAddress } from './profile';
import './profile.css';

const blankAddress: SavedAddress = { id: '', label: 'Home', name: '', phone: '', building: '', area: 'Madinaty' };

export function PaymentOptions({ value, onChange }: { value: PaymentMethod; onChange(value: PaymentMethod): void }) {
  const group = useId();
  return <fieldset className="payment-methods"><legend>Payment method</legend>
    {(Object.keys(paymentLabels) as PaymentMethod[]).map(method => <label className={`payment-choice ${value === method ? 'selected' : ''}`} key={method}>
      <input type="radio" name={group} value={method} checked={value === method} onChange={() => onChange(method)} />
      <CreditCard size={20} /><span><strong>{paymentLabels[method]}</strong><small>{method === 'cod' ? 'Pay cash when your order arrives.' : method === 'card' ? 'Preview only · card payments are not connected yet.' : `Transfer recipient: ${instaPayNumber}`}</small></span>
    </label>)}
    {value === 'instapay' && <div className="transfer-details"><strong>InstaPay recipient</strong><p><bdi>{instaPayNumber}</bdi></p><small>Demo checkout: no transfer is needed. Selecting InstaPay does not confirm a payment.</small></div>}
    {value === 'card' && <p className="fine-print">No card details are collected or stored. This demo does not charge your card.</p>}
  </fieldset>;
}

export function AddressBook({ profile, onChange, selectedId, onSelect, onEditingChange }: { profile: Profile; onChange(value: Profile): void; selectedId?: string; onSelect?(id: string): void; onEditingChange?(editing: boolean): void }) {
  const group = useId();
  const [editing, setEditing] = useState<SavedAddress | null>(null);
  const [makeDefault, setMakeDefault] = useState(false);
  const begin = (address: SavedAddress) => { setEditing(address); onEditingChange?.(true); setMakeDefault(address.id === profile.defaultAddressId || profile.addresses.length === 0); };
  if (editing) return <form className="address-form address-editor" onSubmit={event => {
    event.preventDefault();
    const address = { ...editing, id: editing.id || crypto.randomUUID(), label: editing.label.trim(), name: editing.name.trim(), phone: editing.phone.trim(), building: editing.building.trim() };
    if (address.building.length < 5) return;
    onChange(saveAddress(profile, address, makeDefault)); onSelect?.(address.id); setEditing(null); onEditingChange?.(false);
  }}>
    <h3>{editing.id ? 'Edit address' : 'Add address'}</h3>
    <label>Address label<input required maxLength={40} pattern=".*\S.*" value={editing.label} onChange={e => setEditing({ ...editing, label: e.target.value })} placeholder="Home, Work…" /></label>
    <label>Your name<input required autoComplete="name" maxLength={80} pattern=".*\S.*" value={editing.name} onChange={e => setEditing({ ...editing, name: e.target.value })} /></label>
    <label>Mobile number<input required type="tel" autoComplete="tel" pattern="(?:\+20|0)1[0125][0-9]{8}" title="Use an Egyptian mobile number, e.g. 01012345678 or +201012345678" value={editing.phone} onChange={e => setEditing({ ...editing, phone: e.target.value })} placeholder="01012345678" /></label>
    <label>Neighborhood<select value={editing.area} onChange={e => setEditing({ ...editing, area: e.target.value })}><option>Madinaty</option></select></label>
    <label>Building, apartment & delivery notes<textarea required autoComplete="street-address" minLength={5} maxLength={300} value={editing.building} onChange={e => { e.target.setCustomValidity(e.target.value.trim().length < 5 ? 'Enter at least 5 characters for your delivery address.' : ''); setEditing({ ...editing, building: e.target.value }); }} placeholder="B7, building 12, apartment 4" /></label>
    <label className="default-address-toggle"><input type="checkbox" checked={makeDefault} disabled={editing.id === profile.defaultAddressId || profile.addresses.length === 0} onChange={e => setMakeDefault(e.target.checked)} /> Make this my default address</label>
    <div className="address-actions"><button className="primary" type="submit">Save address</button><button className="secondary-action" type="button" onClick={() => { setEditing(null); onEditingChange?.(false); }}>Cancel</button></div>
  </form>;
  return <section className="address-book" aria-label="Saved delivery addresses"><div className="profile-section-heading"><h3>Delivery addresses</h3><button className="secondary-action" onClick={() => begin({ ...blankAddress })}><Plus size={16} /> Add address</button></div>
    {!profile.addresses.length && <p className="form-intro">Add an address to get started. Your first address becomes the default.</p>}
    {profile.addresses.map(address => <article className="saved-address" key={address.id}>
      <div className="saved-address-heading"><MapPin size={18} /><strong>{address.label}</strong>{profile.defaultAddressId === address.id && <span className="status-tag">Default</span>}</div>
      {onSelect && <label className="delivery-choice"><input type="radio" name={group} checked={selectedId === address.id} onChange={() => onSelect(address.id)} /> Deliver here</label>}
      <p>{address.name} · {address.phone}</p><p>{address.building}</p><p>{address.area}</p>
      <div className="address-actions"><button className="secondary-action" onClick={() => begin(address)}>Edit<span className="sr-only"> {address.label}</span></button>{profile.defaultAddressId !== address.id && <button className="secondary-action" onClick={() => onChange({ ...profile, defaultAddressId: address.id })}>Make default<span className="sr-only"> {address.label}</span></button>}<button className="secondary-action remove-address" onClick={() => onChange(removeAddress(profile, address.id))}>Remove<span className="sr-only"> {address.label}</span></button></div>
    </article>)}
  </section>;
}

export function Checkout({ profile, onChange, total, online, placing, onSubmit }: { profile: Profile; onChange(value: Profile): void; total: number; online: boolean; placing: boolean; onSubmit(address: Address, payment: PaymentMethod): void }) {
  const [addressId, setAddressId] = useState(profile.defaultAddressId ?? '');
  const [payment, setPayment] = useState(profile.paymentMethod);
  const [editing, setEditing] = useState(false);
  const selected = profile.addresses.find(a => a.id === addressId) ?? profile.addresses.find(a => a.id === profile.defaultAddressId);
  return <div className="checkout-profile"><AddressBook profile={profile} onChange={onChange} selectedId={selected?.id} onSelect={setAddressId} onEditingChange={setEditing} />
    <PaymentOptions value={payment} onChange={setPayment} />
    <div className="checkout-total"><span>Total, including delivery</span><strong>{money(total)} EGP</strong></div>
    <div className="demo-note"><ShieldCheck size={16} /> This is a demo order. No charge. No real delivery.</div>
    {!online && <p role="alert" className="error">Please reconnect before placing your demo order.</p>}
    <button className="primary full" disabled={!online || placing || !selected || editing} onClick={() => { if (selected && !editing) onSubmit({ name: selected.name, phone: selected.phone, building: selected.building, area: selected.area }, payment); }}>Place demo order <ArrowRight size={18} /></button>
    {editing && <p className="fine-print">Save or cancel your address changes to continue.</p>}
    {!selected && <p className="fine-print">Save a delivery address before placing your order.</p>}
    {selected && <p className="checkout-destination"><CheckCircle2 size={15} /> Delivering to {selected.label} · {paymentLabels[payment]}</p>}
  </div>;
}

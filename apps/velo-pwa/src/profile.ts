import { readSaved, validAddress, validPaymentMethod, type Address, type PaymentMethod } from './catalog';

export type SavedAddress = Address & { id: string; label: string };
export type Profile = { addresses: SavedAddress[]; defaultAddressId: string | null; paymentMethod: PaymentMethod };
export const emptyProfile: Profile = { addresses: [], defaultAddressId: null, paymentMethod: 'cod' };
export const instaPayNumber = '+201020000115';
export const paymentLabels: Record<PaymentMethod, string> = { cod: 'Cash on delivery (COD)', card: 'Card', instapay: 'InstaPay' };

export function validProfile(value: unknown): value is Profile {
  if (!value || typeof value !== 'object') return false;
  const p = value as Profile;
  return Array.isArray(p.addresses) && p.addresses.every(a => validAddress(a) && typeof a.id === 'string' && a.id.length > 0 && typeof a.label === 'string') &&
    new Set(p.addresses.map(a => a.id)).size === p.addresses.length && validPaymentMethod(p.paymentMethod) &&
    (p.addresses.length === 0 ? p.defaultAddressId === null : p.addresses.some(a => a.id === p.defaultAddressId));
}

export function loadProfile(): Profile {
  const saved = readSaved<Profile | null>('velo-profile', null, (v): v is Profile | null => v === null || validProfile(v));
  if (saved) return saved;
  const legacy = readSaved<Address | null>('velo-address', null, (v): v is Address | null => v === null || validAddress(v));
  if (!legacy?.building.trim()) return { ...emptyProfile, addresses: [] };
  const address: SavedAddress = { ...legacy, id: 'legacy-address', label: 'Home' };
  return { addresses: [address], defaultAddressId: address.id, paymentMethod: 'cod' };
}

export function saveAddress(profile: Profile, address: SavedAddress, makeDefault: boolean): Profile {
  const exists = profile.addresses.some(a => a.id === address.id);
  return { ...profile, addresses: exists ? profile.addresses.map(a => a.id === address.id ? address : a) : [...profile.addresses, address],
    defaultAddressId: makeDefault || !profile.defaultAddressId ? address.id : profile.defaultAddressId };
}

export function removeAddress(profile: Profile, id: string): Profile {
  const addresses = profile.addresses.filter(a => a.id !== id);
  return { ...profile, addresses, defaultAddressId: profile.defaultAddressId === id ? addresses[0]?.id ?? null : profile.defaultAddressId };
}

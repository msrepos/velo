import { afterEach, describe, expect, it, vi } from 'vitest';
import { validOrders, type Order } from './catalog';
import { emptyProfile, loadProfile, removeAddress, saveAddress, validProfile, type SavedAddress } from './profile';

const home: SavedAddress = { id: 'home', label: 'Home', name: 'Nour Ahmed', phone: '01012345678', building: 'B7, building 12, apartment 4', area: 'Madinaty' };
const work: SavedAddress = { ...home, id: 'work', label: 'Work', building: 'Office 25, building 6' };
afterEach(() => vi.unstubAllGlobals());
function storage(values: Record<string, unknown>) {
  vi.stubGlobal('localStorage', { getItem: (key: string) => key in values ? JSON.stringify(values[key]) : null });
}

describe('saved delivery profile', () => {
  it('migrates the old single address without losing contact or delivery details', () => {
    const { id: _id, label: _label, ...legacy } = home;
    storage({ 'velo-address': legacy });
    expect(loadProfile()).toEqual({ addresses: [{ ...legacy, id: 'legacy-address', label: 'Home' }], defaultAddressId: 'legacy-address', paymentMethod: 'cod' });
  });
  it('keeps an empty new profile after all addresses are removed instead of restoring legacy data', () => {
    storage({ 'velo-profile': emptyProfile, 'velo-address': home });
    expect(loadProfile()).toEqual(emptyProfile);
  });
  it('makes the first address default, adds more addresses, and changes the default explicitly', () => {
    const first = saveAddress(emptyProfile, home, false);
    expect(first.defaultAddressId).toBe(home.id);
    const second = saveAddress(first, work, false);
    expect(second.addresses).toEqual([home, work]);
    expect(second.defaultAddressId).toBe(home.id);
    const updated = saveAddress(second, { ...work, building: 'Office 30' }, true);
    expect(updated.defaultAddressId).toBe(work.id);
    expect(updated.addresses).toHaveLength(2);
    expect(updated.addresses[1].building).toBe('Office 30');
    expect(updated.addresses[0]).toEqual(home);
  });
  it('reassigns the default when removed and clears it when the last address is removed', () => {
    const profile = saveAddress(saveAddress(emptyProfile, home, false), work, false);
    const remaining = removeAddress(profile, home.id);
    expect(remaining.defaultAddressId).toBe(work.id);
    expect(removeAddress(remaining, work.id)).toEqual(emptyProfile);
    expect(removeAddress(profile, work.id).defaultAddressId).toBe(home.id);
  });
  it('restores the preferred payment and rejects duplicate addresses, missing defaults, and unsupported payment methods', () => {
    const profile = { ...saveAddress(emptyProfile, home, false), paymentMethod: 'instapay' };
    expect(validProfile(profile)).toBe(true);
    storage({ 'velo-profile': profile });
    expect(loadProfile()).toEqual(profile);
    expect(validProfile({ ...profile, addresses: [home, home] })).toBe(false);
    expect(validProfile({ ...profile, defaultAddressId: 'missing' })).toBe(false);
    expect(validProfile({ ...profile, paymentMethod: 'unknown' })).toBe(false);
    expect(validProfile({ addresses: [null] })).toBe(false);
  });
  it('falls back safely if storage is blocked or corrupt', () => {
    vi.stubGlobal('localStorage', { getItem: () => { throw new Error('blocked'); } });
    expect(loadProfile()).toEqual(emptyProfile);
    vi.stubGlobal('localStorage', { getItem: () => '{bad JSON' });
    expect(loadProfile()).toEqual(emptyProfile);
  });
});

describe('order payment history', () => {
  const order: Order = { id: 'VL-1', date: '2026-10-28T10:00:00Z', items: { milk: 1 }, total: 70, address: home, stage: 0 };
  it('keeps legacy COD orders and all three new payment selections', () => {
    expect(validOrders([order])).toBe(true);
    for (const paymentMethod of ['cod', 'card', 'instapay']) expect(validOrders([{ ...order, paymentMethod }])).toBe(true);
    expect(validOrders([{ ...order, paymentMethod: 'unknown' }])).toBe(false);
  });
});

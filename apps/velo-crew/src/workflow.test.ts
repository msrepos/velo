import { describe, expect, it } from 'vitest';
import { canComplete, demoOrders, pickedCount, unitCount } from './workflow';
describe('minivan preparation safeguards', () => {
  it('requires the full quantity of every line before completion', () => {
    const order = demoOrders()[0];
    expect(unitCount(order)).toBe(5);
    expect(canComplete(order, {})).toBe(false);
    const picked = Object.fromEntries(order.items.map(i => [i.variantId, i.quantity]));
    expect(canComplete(order, picked)).toBe(true);
    picked[order.items[0].variantId] = 1;
    expect(canComplete(order, picked)).toBe(false);
  });
  it('does not let extra or negative quantities mask missing items', () => {
    const order = demoOrders()[0];
    expect(pickedCount(order, { [order.items[0].variantId]: 999 })).toBe(2);
    expect(pickedCount(order, { [order.items[0].variantId]: -4 })).toBe(0);
    expect(canComplete(order, { [order.items[0].variantId]: 999 })).toBe(false);
  });
  it('does not complete received, prepared, or empty orders', () => {
    const order = demoOrders()[0]; const picked = Object.fromEntries(order.items.map(i => [i.variantId, i.quantity]));
    for (const status of ['Pending', 'Confirmed', 'ReadyForDispatch']) expect(canComplete({ ...order, status }, picked)).toBe(false);
    expect(canComplete({ ...order, items: [] }, {})).toBe(false);
  });
});

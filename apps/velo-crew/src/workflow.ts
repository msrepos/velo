import type { Order } from './api';
export type VanOrder = Order & { warehouseId: string };
export type Picked = Record<string, number>;
export const statuses = ['Pending', 'Confirmed', 'Preparing', 'ReadyForDispatch'] as const;
export function pickedCount(order: VanOrder, picked: Picked) {
  return order.items.reduce((sum, item) => sum + Math.max(0, Math.min(item.quantity, picked[item.variantId] || 0)), 0);
}
export function unitCount(order: VanOrder) { return order.items.reduce((sum, item) => sum + item.quantity, 0); }
export function canComplete(order: VanOrder, picked: Picked) {
  return order.status === 'Preparing' && order.items.length > 0 && pickedCount(order, picked) === unitCount(order);
}
export const stageLabel = (status: string) => ({ Pending: 'New order', Confirmed: 'Received', Preparing: 'Preparing', ReadyForDispatch: 'Prepared' }[status] ?? status);
export const demoVan = '8b94721e-d83e-4cc4-a1aa-cda0c67b0007';
export function demoOrders(): VanOrder[] {
  return [
    ['1048', 'Preparing', 'Nour Ahmed', 'B7 · Building 24', [['Whole milk', '1 litre', 'DAI-001', 2], ['Free-range eggs', '6 pack', 'DAI-008', 1], ['Sourdough bread', '400 g', 'BAK-003', 1], ['Bananas', '1 kg', 'FRU-002', 1]]],
    ['1049', 'Pending', 'Omar Hassan', 'B2 · Building 16', [['Mineral water', '1.5 litres', 'DRK-001', 3], ['Potato chips', 'Salt · 150 g', 'SNK-004', 2]]],
    ['1047', 'ReadyForDispatch', 'Salma Ali', 'B6 · Building 8', [['Greek yogurt', '170 g', 'DAI-010', 2], ['Granola', '350 g', 'PAN-014', 1]]]
  ].map(([id, status, name, building, lines], index) => ({
    id: `demo-${id}`, warehouseId: demoVan, status: status as string, paymentStatus: 'Unpaid', paymentMethod: 'CashOnDelivery', currency: 'EGP',
    createdAt: new Date(Date.now() - (index + 1) * 120000).toISOString(), expiresAt: new Date(Date.now() + 600000).toISOString(),
    total: (lines as (string | number)[][]).reduce((sum, line) => sum + Number(line[3]) * 35, 0) + 15,
    subtotal: (lines as (string | number)[][]).reduce((sum, line) => sum + Number(line[3]) * 35, 0), deliveryFee: 15,
    address: { recipientName: name as string, phoneNumber: '+201000000000', city: 'Madinaty', area: 'Madinaty', street: 'Garden Avenue', building: building as string, floor: '2', apartment: '12', instructions: index === 0 ? 'Please keep the bread separate from chilled items.' : undefined },
    items: (lines as (string | number)[][]).map(([productName, variantName, sku, quantity], i) => ({ variantId: `${id}-${i}`, productName: productName as string, variantName: variantName as string, sku: sku as string, quantity: quantity as number, unitPrice: 35, lineTotal: 35 * Number(quantity) })), history: []
  }));
}

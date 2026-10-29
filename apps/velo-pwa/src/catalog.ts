export type Product = { id: string; productId: string; name: string; detail: string; description: string; category: string; price: number; imageUrl: string | null; color: string; oldPrice?: number; badge?: string };
export type Cart = Record<string, number>;
export type Address = { name: string; phone: string; building: string; area: string };
export type PaymentMethod = 'cod' | 'card' | 'instapay';
export const validPaymentMethod = (v: unknown): v is PaymentMethod => v === 'cod' || v === 'card' || v === 'instapay';
export type Order = { id: string; date: string; items: Cart; total: number; address: Address; stage: number; paymentMethod?: PaymentMethod };
export const deliveryFee = 20;
export const money = (value: number) => new Intl.NumberFormat('en-EG', { maximumFractionDigits: 2 }).format(value);
export const subtotal = (cart: Cart, products: Product[]) => products.reduce((sum, product) => sum + product.price * (cart[product.id] ?? 0), 0);
export const count = (cart: Cart) => Object.values(cart).reduce((sum, value) => sum + value, 0);
export function changeQuantity(cart: Cart, id: string, delta: number, products: Product[]): Cart {
  if (!products.some(p => p.id === id)) return cart;
  const next = { ...cart, [id]: Math.min(20, Math.max(0, (cart[id] ?? 0) + delta)) };
  if (!next[id]) delete next[id];
  return next;
}
export function readSaved<T>(key: string, fallback: T, valid: (v: unknown) => v is T): T {
  try { const value: unknown = JSON.parse(localStorage.getItem(key) ?? 'null'); return valid(value) ? value : fallback; } catch { return fallback; }
}
export const validCart = (v: unknown): v is Cart => !!v && typeof v === 'object' && !Array.isArray(v) && Object.entries(v).every(([id,n]) => id.length > 0 && Number.isInteger(n) && n > 0 && n <= 20);
export const validAddress = (v: unknown): v is Address => !!v && typeof v === 'object' && ['name','phone','building','area'].every(k => typeof (v as Record<string, unknown>)[k] === 'string');
export const validOrders = (v: unknown): v is Order[] => Array.isArray(v) && v.every(o => o && typeof o.id === 'string' && typeof o.date === 'string' && validCart(o.items) && validAddress(o.address) && Number.isFinite(o.total) && Number.isInteger(o.stage) && o.stage >= 0 && o.stage <= 3 && (o.paymentMethod === undefined || validPaymentMethod(o.paymentMethod)));

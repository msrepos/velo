export type Product = { id: string; name: string; detail: string; category: string; price: number; oldPrice?: number; color: string; art: string; badge?: string };
export const categories = ['All essentials', 'Fruit & veg', 'Dairy & eggs', 'Bakery', 'Pantry', 'Drinks'];
export const products: Product[] = [
  { id: 'avocado', name: 'Perfectly ripe avocados', detail: '2 pieces · ready to enjoy', category: 'Fruit & veg', price: 89, oldPrice: 105, color: '#edf0df', art: 'avocado', badge: 'GOOD PICK' },
  { id: 'strawberry', name: 'Sweet strawberries', detail: '250 g · locally grown', category: 'Fruit & veg', price: 45, color: '#f9e9e7', art: 'strawberry', badge: 'LOCAL FAVORITE' },
  { id: 'milk', name: 'Fresh whole milk', detail: '1 liter · full cream', category: 'Dairy & eggs', price: 52, color: '#e8eef2', art: 'milk' },
  { id: 'bread', name: 'The daily sourdough', detail: '400 g · baked this morning', category: 'Bakery', price: 75, color: '#f5ecdf', art: 'bread', badge: 'FRESHLY BAKED' },
  { id: 'banana', name: 'Sunshine bananas', detail: '1 kg · naturally sweet', category: 'Fruit & veg', price: 38, color: '#f6f0d8', art: 'banana' },
  { id: 'eggs', name: 'Farm-fresh eggs', detail: '6 pieces · free range', category: 'Dairy & eggs', price: 65, color: '#f1e9df', art: 'eggs' },
  { id: 'tomato', name: 'Vine cherry tomatoes', detail: '250 g · crisp & juicy', category: 'Fruit & veg', price: 32, oldPrice: 40, color: '#f3e5df', art: 'tomato' },
  { id: 'juice', name: 'A little orange sunshine', detail: '500 ml · orange juice', category: 'Drinks', price: 48, color: '#f7ecd6', art: 'juice' },
  { id: 'oats', name: 'Good morning oats', detail: '500 g · wholegrain', category: 'Pantry', price: 68, color: '#e9ecdf', art: 'oats' },
  { id: 'lemon', name: 'Bright yellow lemons', detail: '500 g · a little zest', category: 'Fruit & veg', price: 29, color: '#f5f0d7', art: 'lemon' },
  { id: 'croissant', name: 'Butter croissants', detail: '2 pieces · flaky & golden', category: 'Bakery', price: 60, color: '#f4e6d9', art: 'croissant' },
  { id: 'water', name: 'Everyday mineral water', detail: '1.5 liters · still', category: 'Drinks', price: 15, color: '#e5efed', art: 'water' }
];
export type Cart = Record<string, number>;
export type Address = { name: string; phone: string; building: string; area: string };
export type Order = { id: string; date: string; items: Cart; total: number; address: Address; stage: number };
export const deliveryFee = 20;
export const money = (value: number) => new Intl.NumberFormat('en-EG', { maximumFractionDigits: 2 }).format(value);
export const subtotal = (cart: Cart) => products.reduce((sum, product) => sum + product.price * (cart[product.id] ?? 0), 0);
export const count = (cart: Cart) => Object.values(cart).reduce((sum, value) => sum + value, 0);
export function changeQuantity(cart: Cart, id: string, delta: number): Cart {
  if (!products.some(p => p.id === id)) return cart;
  const next = { ...cart, [id]: Math.min(20, Math.max(0, (cart[id] ?? 0) + delta)) };
  if (!next[id]) delete next[id];
  return next;
}
export function readSaved<T>(key: string, fallback: T, valid: (v: unknown) => v is T): T {
  try { const value: unknown = JSON.parse(localStorage.getItem(key) ?? 'null'); return valid(value) ? value : fallback; } catch { return fallback; }
}
export const validCart = (v: unknown): v is Cart => !!v && typeof v === 'object' && !Array.isArray(v) && Object.entries(v).every(([id,n]) => products.some(p=>p.id===id) && Number.isInteger(n) && n > 0 && n <= 20);
export const validAddress = (v: unknown): v is Address => !!v && typeof v === 'object' && ['name','phone','building','area'].every(k => typeof (v as Record<string, unknown>)[k] === 'string');
export const validOrders = (v: unknown): v is Order[] => Array.isArray(v) && v.every(o => o && typeof o.id === 'string' && typeof o.date === 'string' && validCart(o.items) && validAddress(o.address) && Number.isFinite(o.total) && Number.isInteger(o.stage) && o.stage >= 0 && o.stage <= 3);

import type { Address, Work } from './api';

export const money = (value: number) => `${new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 2 }).format(value)} ج.م`;
export const shortId = (id: string) => id.slice(0, 8).toUpperCase();
export function validPin(address: Address) {
  return Number.isFinite(address.latitude) && Number.isFinite(address.longitude) &&
    address.latitude >= -90 && address.latitude <= 90 && address.longitude >= -180 && address.longitude <= 180;
}
export function navigationUrl(address: Address) {
  if (!validPin(address)) return null;
  const query = new URLSearchParams({ api: '1', destination: `${address.latitude},${address.longitude}`, travelmode: 'driving' });
  return `https://www.google.com/maps/dir/?${query}`;
}
export function mapUrl(address: Address) {
  if (!validPin(address)) return null;
  const lat = address.latitude, lng = address.longitude;
  const bbox = [Math.max(-180, lng - .008), Math.max(-90, lat - .005), Math.min(180, lng + .008), Math.min(90, lat + .005)].join(',');
  return `https://www.openstreetmap.org/export/embed.html?${new URLSearchParams({ bbox, layer: 'mapnik', marker: `${lat},${lng}` })}`;
}
export const preparationLabel = (status: string) => status === 'Confirmed' ? 'الطلب اتأكد' : status === 'Preparing' ? 'الـ crew بيجهّز الطلب' : status === 'ReadyForDispatch' ? 'الطلب جاهز' : status === 'OutForDelivery' ? 'جاهز للتسليم' : status === 'DeliveryFailed' ? 'تعذر التسليم' : 'في انتظار التحديث';
export const canHandover = (work: Work) => work.order.status === 'ReadyForDispatch' && !!work.journey.arrivedAt;
export const canNavigate = (work: Work) => ['Confirmed', 'Preparing', 'ReadyForDispatch', 'OutForDelivery'].includes(work.order.status) && validPin(work.order.address);
export function demoWork(): Work {
  return {
    order: { id: 'de001234-0000-4000-8000-000000000001', status: 'Preparing', total: 185, currency: 'EGP', paymentMethod: 'CashOnDelivery', paymentStatus: 'Unpaid',
      address: { recipientName: 'عميل تجريبي', phoneNumber: '', city: 'مدينتي', area: 'B6', street: 'شارع الخدمات', building: 'عمارة ٢٤', floor: '٣', apartment: '١٢', instructions: 'مدخل العمارة ناحية الحديقة — بيانات تجريبية.', latitude: 30.1008, longitude: 31.6387 },
      items: [{ variantId: 'milk', productName: 'حليب كامل الدسم', variantName: '١ لتر', quantity: 2 }, { variantId: 'bread', productName: 'خبز طازج', variantName: 'عبوة', quantity: 1 }] },
    journey: { orderId: 'de001234-0000-4000-8000-000000000001', status: 'Assigned', travelStartedAt: null, arrivedAt: null }
  };
}

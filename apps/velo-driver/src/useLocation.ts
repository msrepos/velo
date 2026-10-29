import { useEffect, useState } from 'react';
import type { Api } from './api';

export function useLocation(api: Api, enabled: boolean) {
  const [status, setStatus] = useState('');
  useEffect(() => {
    if (!enabled) { setStatus(''); return; }
    if (!navigator.geolocation) { setStatus('الجهاز مش بيدعم تحديد الموقع.'); return; }
    let watch: number | undefined; let position: GeolocationPosition | null = null;
    let sending = false; let lastSent = 0; let stopped = false;
    const abort = new AbortController();
    const send = async () => {
      if (stopped || document.visibilityState !== 'visible' || !navigator.onLine || !position || sending || Date.now() - lastSent < 10000) return;
      sending = true; lastSent = Date.now();
      try {
        await api('/driver/location', 'POST', { latitude: Number(position.coords.latitude.toFixed(6)), longitude: Number(position.coords.longitude.toFixed(6)) }, undefined, abort.signal);
        if (!stopped) setStatus('موقعك بيتحدّث أثناء فتح التطبيق');
      } catch { if (!stopped && !abort.signal.aborted) setStatus('تحديث الموقع متوقف مؤقتًا. راجع الإنترنت وإذن الموقع.'); }
      finally { sending = false; }
    };
    const visibility = () => {
      if (watch !== undefined) { navigator.geolocation.clearWatch(watch); watch = undefined; }
      position = null;
      if (document.visibilityState !== 'visible') { setStatus('مشاركة الموقع متوقفة والتطبيق في الخلفية'); return; }
      setStatus('بنحدد موقعك…');
      watch = navigator.geolocation.watchPosition(p => { position = p; void send(); }, error => {
        setStatus(error.code === 1 ? 'فعّل إذن الموقع من إعدادات المتصفح للمشاركة.' : 'مش قادرين نحدد موقعك حاليًا.');
      }, { enableHighAccuracy: true, maximumAge: 10000, timeout: 15000 });
    };
    visibility(); document.addEventListener('visibilitychange', visibility);
    const timer = setInterval(() => void send(), 15000);
    return () => { stopped = true; abort.abort(); clearInterval(timer); document.removeEventListener('visibilitychange', visibility); if (watch !== undefined) navigator.geolocation.clearWatch(watch); };
  }, [api, enabled]);
  return status;
}

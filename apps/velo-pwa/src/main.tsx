import React, { Suspense, lazy } from 'react';
import { createRoot } from 'react-dom/client';

const admin = /^\/admin(?:\/|$)/.test(window.location.pathname);
const App = admin ? lazy(() => import('./admin/Admin')) : lazy(() => import('./Storefront'));
createRoot(document.getElementById('root')!).render(
  <React.StrictMode><Suspense fallback={<p role="status" style={{padding: 32, fontFamily: 'sans-serif'}}>Loading Velo…</p>}><App /></Suspense></React.StrictMode>
);

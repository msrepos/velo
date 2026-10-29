import React from 'react';
import { createRoot } from 'react-dom/client';
import Driver from './Driver';
import './driver.css';

createRoot(document.getElementById('root')!).render(<React.StrictMode><Driver /></React.StrictMode>);

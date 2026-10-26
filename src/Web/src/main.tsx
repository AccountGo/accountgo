import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './index.css';
import { registerServiceWorker } from './pwa/registerServiceWorker';
import { initScrollWatcher } from './lib/scrollWatcher';
import { ThemeProvider } from './themes/ThemeContext';
import { AuthProvider } from './auth/AuthContext';

// Register PWA service worker (production shell caching + offline fallback)
registerServiceWorker();

// Initialize application-wide auto-hiding scrollbar observer
initScrollWatcher();

ReactDOM.createRoot(document.getElementById('root') as HTMLElement).render(
  <React.StrictMode>
    <ThemeProvider>
      <AuthProvider>
        <App />
      </AuthProvider>
    </ThemeProvider>
  </React.StrictMode>
);

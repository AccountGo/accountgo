/**
 * Registers the PWA service worker in production, or unregisters in local dev
 * so that HMR and rapid development cycles are not interfered with.
 */
export function registerServiceWorker(): void {
  if (typeof window === 'undefined' || !('serviceWorker' in navigator)) return;

  if (!import.meta.env.PROD) {
    // Unregister any active worker in development
    navigator.serviceWorker.getRegistrations().then((registrations) => {
      for (const registration of registrations) {
        registration.unregister();
      }
    });
    return;
  }

  window.addEventListener('load', () => {
    navigator.serviceWorker
      .register('/sw.js')
      .then((registration) => {
        // Check for updates
        registration.addEventListener('updatefound', () => {
          const installingWorker = registration.installing;
          if (installingWorker) {
            installingWorker.addEventListener('statechange', () => {
              if (
                installingWorker.state === 'installed' &&
                navigator.serviceWorker.controller
              ) {
                console.log(
                  'AccountGo PWA: New update available. Reload to update.'
                );
              }
            });
          }
        });
      })
      .catch((error) => {
        console.warn('AccountGo PWA: Service worker registration failed:', error);
      });
  });
}

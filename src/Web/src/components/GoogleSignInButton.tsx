import { useEffect, useRef, useState } from 'react';

type GoogleIdentity = {
  accounts: { id: {
    initialize(options: { client_id: string; callback: (response: { credential: string }) => void }): void;
    renderButton(element: HTMLElement, options: object): void;
  } };
};

declare global { interface Window { google?: GoogleIdentity } }

export function GoogleSignInButton({ clientId, enabled, onStart, onCredential, onError }: {
  clientId: string; enabled: boolean; onStart?: () => void; onCredential: (credential: string) => void; onError: (message: string) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const callback = useRef(onCredential);
  const start = useRef(onStart);
  start.current = onStart;
  const isEnabled = useRef(enabled);
  isEnabled.current = enabled;
  const reportError = useRef(onError);
  const [ready, setReady] = useState(false);
  callback.current = onCredential;
  reportError.current = onError;

  useEffect(() => {
    if (!enabled) {
      container.current?.replaceChildren();
      setReady(false);
      return;
    }
    let active = true;
    const render = () => {
      if (!active || !container.current || !window.google) return;
      window.google.accounts.id.initialize({ client_id: clientId, callback: ({ credential }) => { if (isEnabled.current) callback.current(credential); } });
      container.current.replaceChildren();
      window.google.accounts.id.renderButton(container.current, { theme: 'outline', size: 'large', text: 'continue_with', width: 320, click_listener: () => { if (isEnabled.current) start.current?.(); } });
      setReady(true);
    };
    if (window.google) render();
    else {
      const script = document.createElement('script');
      script.src = 'https://accounts.google.com/gsi/client';
      script.async = true;
      script.onload = render;
      script.onerror = () => { if (active) reportError.current('Google sign-in could not be loaded. Please use your email code.'); };
      document.head.appendChild(script);
    }
    return () => { active = false; };
  }, [clientId, enabled]);

  return <div className="flex justify-center">
    {(!enabled || !ready) && <div className="relative w-full group" tabIndex={!enabled ? 0 : undefined} aria-describedby={!enabled ? 'google-terms-tooltip' : undefined}>
      <button type="button" disabled className="w-full py-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-canvas)] text-[var(--text-secondary)] font-semibold opacity-50 cursor-not-allowed flex items-center justify-center gap-3">
        <span aria-hidden="true" className="text-lg font-bold">G</span> Continue with Google
      </button>
      {!enabled && <span id="google-terms-tooltip" role="tooltip" className="pointer-events-none absolute bottom-full left-1/2 z-20 mb-2 w-64 -translate-x-1/2 rounded-lg bg-[var(--text-main)] p-2 text-center text-xs text-[var(--bg-canvas)] opacity-0 group-hover:opacity-100 group-focus:opacity-100">Please accept the Privacy Policy and Terms of Use checkbox to continue.</span>}
    </div>}
    <div ref={container} className="flex justify-center" />
  </div>;
}

import { useEffect, useRef, useState } from 'react';

type GoogleIdentity = {
  accounts: { id: {
    initialize(options: { client_id: string; callback: (response: { credential: string }) => void }): void;
    renderButton(element: HTMLElement, options: object): void;
  } };
};

declare global { interface Window { google?: GoogleIdentity } }

export function GoogleSignInButton({ clientId, enabled, onCredential, onError }: {
  clientId: string; enabled: boolean; onCredential: (credential: string) => void; onError: (message: string) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const callback = useRef(onCredential);
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
      window.google.accounts.id.renderButton(container.current, { theme: 'outline', size: 'large', text: 'continue_with', width: 320 });
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
    {(!enabled || !ready) && <button type="button" disabled className="w-full py-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-canvas)] text-[var(--text-secondary)] font-semibold opacity-50 cursor-not-allowed flex items-center justify-center gap-3">
      <span aria-hidden="true" className="text-lg font-bold">G</span> Continue with Google
    </button>}
    <div ref={container} className="flex justify-center" />
  </div>;
}

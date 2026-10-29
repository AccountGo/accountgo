import { useEffect, useRef, useImperativeHandle, forwardRef } from 'react';

declare global {
  interface Window {
    turnstile?: {
      render: (
        container: string | HTMLElement,
        params: {
          sitekey: string;
          action?: string;
          cData?: string;
          callback?: (token: string) => void;
          'error-callback'?: (error: any) => void;
          'expired-callback'?: () => void;
          theme?: 'light' | 'dark' | 'auto';
          size?: 'normal' | 'flexible' | 'compact';
          [key: string]: any;
        }
      ) => string;
      reset: (widgetId: string) => void;
      remove: (widgetId: string) => void;
      getResponse: (widgetId: string) => string | undefined;
    };
  }
}

export interface TurnstileWidgetHandle {
  reset: () => void;
}

export interface TurnstileWidgetProps {
  siteKey?: string;
  action: string;
  onVerify: (token: string) => void;
  onExpire?: () => void;
  onError?: (error?: any) => void;
  className?: string;
}

export const TurnstileWidget = forwardRef<TurnstileWidgetHandle, TurnstileWidgetProps>(
  ({ siteKey, action, onVerify, onExpire, onError, className = '' }, ref) => {
    const containerRef = useRef<HTMLDivElement>(null);
    const widgetIdRef = useRef<string | null>(null);
    // Keep latest callbacks in a ref so parent re-renders (new inline fns / setState
    // after verify) do not tear down and re-create the Turnstile widget.
    const callbacksRef = useRef({ onVerify, onExpire, onError });
    callbacksRef.current = { onVerify, onExpire, onError };

    const key = siteKey || import.meta.env.VITE_TURNSTILE_SITE_KEY || '0x4AAAAAAFnojelGMzCnEVtr';

    useImperativeHandle(
      ref,
      () => ({
        reset: () => {
          if (widgetIdRef.current !== null && window.turnstile) {
            try {
              window.turnstile.reset(widgetIdRef.current);
            } catch {
              // Ignore if already reset or unmounted
            }
          }
        },
      }),
      []
    );

    useEffect(() => {
      let isMounted = true;
      let checkTimer: ReturnType<typeof setInterval> | null = null;

      const render = () => {
        if (!isMounted || !containerRef.current || !window.turnstile) return;
        if (widgetIdRef.current !== null) return;

        try {
          const id = window.turnstile.render(containerRef.current, {
            sitekey: key,
            action,
            callback: (token: string) => {
              if (isMounted) callbacksRef.current.onVerify(token);
            },
            'expired-callback': () => {
              if (isMounted) callbacksRef.current.onExpire?.();
            },
            'error-callback': (err: any) => {
              if (isMounted) callbacksRef.current.onError?.(err);
            },
            theme: 'auto',
          });
          widgetIdRef.current = id;
        } catch {
          // Retry on next frame if render failed due to timing
        }
      };

      if (window.turnstile) {
        render();
      } else {
        checkTimer = setInterval(() => {
          if (window.turnstile) {
            if (checkTimer) clearInterval(checkTimer);
            render();
          }
        }, 100);
      }

      return () => {
        isMounted = false;
        if (checkTimer) clearInterval(checkTimer);
        if (widgetIdRef.current !== null && window.turnstile) {
          try {
            window.turnstile.remove(widgetIdRef.current);
          } catch {
            // Ignore on unmount
          }
          widgetIdRef.current = null;
        }
      };
    }, [key, action]);

    return (
      <div
        ref={containerRef}
        className={`cf-turnstile-container min-h-[65px] flex justify-center items-center ${className}`}
      />
    );
  }
);

TurnstileWidget.displayName = 'TurnstileWidget';

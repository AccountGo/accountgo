// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useEffect, useState, useCallback, useRef } from 'react';
import { createPortal } from 'react-dom';
import { AlertCircle, Loader2, RefreshCw, X } from 'lucide-react';
import {
  type ServiceUnavailableCode,
  type ServiceStatusState,
  dismissWriteFailedNotice,
  getServiceStatus,
  isTakingLong,
  nextProbeDelayMs,
  parseResponseServiceProblem,
  reportNetworkFailure,
  reportServiceProblem,
  reportServiceRestored,
  setServiceStatusCollapsed,
  subscribeServiceStatus,
} from '../lib/serviceStatus';

export function getServiceStatusCopy(
  code?: ServiceUnavailableCode,
  message?: string,
  until?: string
): {
  title: string;
  body: string;
  expectedUntil?: string;
} {
  switch (code) {
    case 'SERVICE_UPDATING':
      return {
        title: 'AccountGo is being updated',
        body: "We're installing a new version. This usually takes a minute or two. Your data is safe and you'll stay signed in. This page will continue on its own.",
      };
    case 'SERVICE_STARTING':
      return {
        title: 'AccountGo is starting up',
        body: "It's been quiet for a while, so AccountGo is waking up. This can take a minute or two. You'll stay signed in.",
      };
    case 'SERVICE_MAINTENANCE': {
      let expectedUntil: string | undefined;
      if (until) {
        try {
          const date = new Date(until);
          if (!isNaN(date.getTime())) {
            expectedUntil = `Expected back by ${date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`;
          }
        } catch {}
      }
      return {
        title: 'AccountGo is down for scheduled maintenance',
        body: message?.trim() ? message.trim() : "We're doing planned maintenance and will be back shortly.",
        expectedUntil,
      };
    }
    case 'SERVICE_INTERRUPTED':
      return {
        title: 'AccountGo is restarting',
        body: 'AccountGo restarted while handling a request. It will be back in a moment.',
      };
    case 'SERVICE_UNREACHABLE':
    case 'SERVICE_MISCONFIGURED':
    default:
      return {
        title: "We can't reach AccountGo right now",
        body: "We'll keep trying. If this continues, check your connection.",
      };
  }
}

export interface ServiceStatusCardProps {
  code?: ServiceUnavailableCode;
  message?: string;
  until?: string;
  since?: number;
  secondsUntilProbe?: number;
  isTakingLongTime?: boolean;
  onCheckNow?: () => void;
  onHide?: () => void;
  onReload?: () => void;
  hideControls?: boolean;
}

export const ServiceStatusCard: React.FC<ServiceStatusCardProps> = ({
  code,
  message,
  until,
  since,
  secondsUntilProbe,
  isTakingLongTime,
  onCheckNow,
  onHide,
  onReload,
  hideControls = false,
}) => {
  const { title, body, expectedUntil } = getServiceStatusCopy(code, message, until);
  const now = Date.now();
  const elapsedMinutes = since ? Math.floor(Math.max(0, now - since) / 60_000) : 0;

  return (
    <div
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="service-status-title"
      aria-describedby="service-status-body"
      className="relative w-full max-w-md rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 shadow-2xl flex flex-col items-center gap-4 text-center transition-all duration-200"
    >
      <div className="relative">
        <div className="w-16 h-16 rounded-2xl bg-[var(--brand-primary)]/10 ring-1 ring-[var(--brand-ring)] flex items-center justify-center shadow-lg shadow-[var(--brand-primary)]/10 animate-pulse">
          <img
            src="/accountgo-mark.svg"
            alt="AccountGo"
            className="w-10 h-10 rounded-xl"
          />
        </div>
        <div className="absolute -bottom-1 -right-1">
          <Loader2 className="w-5 h-5 text-[var(--brand-primary)] animate-spin" />
        </div>
      </div>

      <div className="flex flex-col items-center gap-1.5 w-full">
        <h2
          id="service-status-title"
          className="text-base font-semibold text-[var(--text-main)]"
        >
          {title}
        </h2>
        <p
          id="service-status-body"
          className="text-xs text-[var(--text-muted)] leading-relaxed max-w-sm"
        >
          {body}
        </p>
        {expectedUntil && (
          <p className="text-xs font-medium text-[var(--brand-primary)] mt-0.5">
            {expectedUntil}
          </p>
        )}
      </div>

      <div
        aria-live="polite"
        className="flex flex-col items-center gap-1 text-xs text-[var(--text-muted)] border-t border-[var(--border-subtle)]/60 pt-3 w-full"
      >
        {since ? (
          <span>
            Started {elapsedMinutes < 1 ? 'less than a minute' : `${elapsedMinutes} min`} ago
          </span>
        ) : null}
        {typeof secondsUntilProbe === 'number' && secondsUntilProbe >= 0 ? (
          <span>Checking again in {secondsUntilProbe}s</span>
        ) : null}
      </div>

      {!hideControls && (
        <div className="flex flex-col items-center gap-2 w-full pt-1">
          <div className="flex items-center justify-center gap-3 w-full">
            {onCheckNow && (
              <button
                type="button"
                onClick={onCheckNow}
                className="inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-medium rounded-lg bg-[var(--brand-primary)] text-white hover:opacity-90 active:scale-95 transition"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                Check now
              </button>
            )}
            {onHide && (
              <button
                type="button"
                onClick={onHide}
                className="px-3.5 py-1.5 text-xs font-medium rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover,rgba(0,0,0,0.05))] transition"
              >
                Hide for now
              </button>
            )}
          </div>

          {isTakingLongTime && (
            <div className="mt-2 flex flex-col items-center gap-2 p-2.5 rounded-lg bg-amber-500/10 border border-amber-500/20 text-xs text-amber-600 dark:text-amber-400 w-full">
              <span>This is taking longer than usual.</span>
              <button
                type="button"
                onClick={onReload || (() => window.location.reload())}
                className="px-3 py-1 font-semibold rounded bg-amber-600 text-white text-[11px] hover:bg-amber-700 transition"
              >
                Reload page
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export const ServiceStatusOverlay: React.FC = () => {
  const [status, setStatus] = useState<ServiceStatusState>(getServiceStatus);
  const [secondsUntilProbe, setSecondsUntilProbe] = useState<number>(5);
  const [takingLong, setTakingLong] = useState<boolean>(false);
  const probeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const countdownIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const nextProbeTimeRef = useRef<number>(0);

  useEffect(() => {
    return subscribeServiceStatus(setStatus);
  }, []);

  const probe = useCallback(async () => {
    try {
      const res = await fetch('/api/status', {
        credentials: 'include',
        cache: 'no-store',
      });
      if (res.status === 200) {
        reportServiceRestored();
        return;
      }
      if (res.status === 503) {
        const problem = await parseResponseServiceProblem(res);
        if (problem) {
          reportServiceProblem(problem);
          return;
        }
      }
      reportNetworkFailure({ status: res.status });
    } catch {
      reportNetworkFailure();
    }
  }, []);

  const scheduleNextProbe = useCallback(
    (delayMs: number) => {
      if (probeTimerRef.current) clearTimeout(probeTimerRef.current);
      if (countdownIntervalRef.current) clearInterval(countdownIntervalRef.current);

      nextProbeTimeRef.current = Date.now() + delayMs;
      setSecondsUntilProbe(Math.max(1, Math.round(delayMs / 1000)));

      countdownIntervalRef.current = setInterval(() => {
        const remaining = Math.max(0, Math.round((nextProbeTimeRef.current - Date.now()) / 1000));
        setSecondsUntilProbe(remaining);
      }, 1000);

      probeTimerRef.current = setTimeout(async () => {
        if (countdownIntervalRef.current) clearInterval(countdownIntervalRef.current);
        await probe();
      }, delayMs);
    },
    [probe]
  );

  useEffect(() => {
    if (status.state !== 'unavailable') {
      if (probeTimerRef.current) clearTimeout(probeTimerRef.current);
      if (countdownIntervalRef.current) clearInterval(countdownIntervalRef.current);
      setTakingLong(false);
      return;
    }

    const elapsed = status.since ? Date.now() - status.since : 0;
    setTakingLong(isTakingLong(elapsed));

    const delay = nextProbeDelayMs(elapsed, status.lastRetryAfter);
    scheduleNextProbe(delay);

    const handleVisibilityOrOnline = () => {
      if (document.visibilityState === 'visible' && navigator.onLine) {
        void probe();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityOrOnline);
    window.addEventListener('online', handleVisibilityOrOnline);

    return () => {
      if (probeTimerRef.current) clearTimeout(probeTimerRef.current);
      if (countdownIntervalRef.current) clearInterval(countdownIntervalRef.current);
      document.removeEventListener('visibilitychange', handleVisibilityOrOnline);
      window.removeEventListener('online', handleVisibilityOrOnline);
    };
  }, [status.state, status.since, status.lastRetryAfter, probe, scheduleNextProbe]);

  // Keep takingLong up to date periodically
  useEffect(() => {
    if (status.state !== 'unavailable' || !status.since) return;
    const interval = setInterval(() => {
      const elapsed = Date.now() - (status.since || Date.now());
      setTakingLong(isTakingLong(elapsed));
    }, 10_000);
    return () => clearInterval(interval);
  }, [status.state, status.since]);

  if (typeof document === 'undefined') return null;

  const content = (
    <>
      {status.state === 'unavailable' && (
        status.collapsed ? (
          <aside
            aria-label="Service unavailable notice"
            className="fixed top-0 inset-x-0 z-[100] flex items-center justify-between gap-3 border-b border-[var(--border-subtle)] bg-[var(--bg-surface)] px-4 py-2.5 text-xs text-[var(--text-main)] shadow-sm pt-[max(0.625rem,env(safe-area-inset-top))] pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))]"
          >
            <div className="flex items-center gap-2 min-w-0">
              <AlertCircle className="w-4 h-4 shrink-0 text-[var(--brand-primary)]" />
              <span className="truncate">
                AccountGo is unavailable — changes can't be saved yet.
              </span>
            </div>
            <button
              type="button"
              onClick={() => setServiceStatusCollapsed(false)}
              className="shrink-0 font-medium text-[var(--brand-primary)] hover:underline"
            >
              Show
            </button>
          </aside>
        ) : (
          <div className="fixed inset-0 z-[100] flex items-center justify-center bg-[var(--bg-canvas)]/80 backdrop-blur-sm p-4 pt-[max(1rem,env(safe-area-inset-top))] pb-[max(1rem,env(safe-area-inset-bottom))] pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))]">
            <ServiceStatusCard
              code={status.code}
              message={status.message}
              until={status.until}
              since={status.since}
              secondsUntilProbe={secondsUntilProbe}
              isTakingLongTime={takingLong}
              onCheckNow={probe}
              onHide={() => setServiceStatusCollapsed(true)}
              onReload={() => window.location.reload()}
            />
          </div>
        )
      )}

      {status.state === 'ok' && status.writeFailed && (
        <div
          role="status"
          aria-live="polite"
          className="fixed bottom-4 right-4 z-[90] max-w-sm rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4 shadow-xl flex items-start gap-3 text-xs text-[var(--text-main)] transition-all animate-in fade-in"
        >
          <AlertCircle className="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />
          <div className="flex-1 leading-relaxed">
            Something you tried to save while AccountGo was unavailable may not have been saved. Please check it before trying again.
          </div>
          <button
            type="button"
            onClick={dismissWriteFailedNotice}
            aria-label="Dismiss notice"
            className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-0.5 -mr-1 -mt-0.5 rounded transition"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}
    </>
  );

  return createPortal(content, document.body);
};

// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { ModalShell } from './ModalShell';
import {
  getServiceStatus,
  shouldSuppressOverlay,
  type ServiceStatusState,
  subscribeServiceStatus,
} from '../lib/serviceStatus';
import {
  deferUpdate,
  getDeferredUpdate,
  shouldOpenUpdateDialog,
  startVersionWatch,
  subscribeDeferredUpdate,
} from '../lib/appVersion';

function isAuthRoute(pathname: string): boolean {
  return (
    pathname === '/login' ||
    pathname === '/signup' ||
    pathname.startsWith('/invite') ||
    pathname === '/cli/authorize'
  );
}

export const UpdateAvailableDialog: React.FC = () => {
  const { isAuthenticated, isLoading, isStarting } = useAuth();
  const [serviceStatus, setServiceStatus] = useState<ServiceStatusState>(getServiceStatus);
  const [deferredId, setDeferredId] = useState<string | null>(getDeferredUpdate);
  const [pendingUpdateId, setPendingUpdateId] = useState<string | null>(null);

  useEffect(() => {
    return subscribeServiceStatus(setServiceStatus);
  }, []);

  useEffect(() => {
    return subscribeDeferredUpdate(setDeferredId);
  }, []);

  useEffect(() => {
    const watch = startVersionWatch({
      enabled: import.meta.env.PROD,
      onUpdate: (newBuildId) => {
        setPendingUpdateId(newBuildId);
      },
    });
    return () => {
      watch.stop();
    };
  }, []);

  const handleLater = () => {
    if (pendingUpdateId) {
      deferUpdate(pendingUpdateId);
    }
  };

  const handleReload = () => {
    window.location.reload();
  };

  const isAuthPage =
    !isAuthenticated ||
    (typeof window !== 'undefined' && isAuthRoute(window.location.pathname));

  const isOpen =
    !isAuthPage &&
    shouldOpenUpdateDialog({
      pendingId: pendingUpdateId,
      deferredId,
      serviceState: serviceStatus.state,
      bootSuppressed: shouldSuppressOverlay({ isLoading, isStarting }),
    });

  if (!isOpen) return null;

  return (
    <ModalShell
      open={isOpen}
      onClose={handleLater}
      role="alertdialog"
      maxWidthClassName="max-w-md"
      panelClassName="p-6"
      aria-labelledby="update-dialog-title"
      aria-describedby="update-dialog-body"
    >
      <div className="flex flex-col items-center text-center gap-4">
        <div className="w-12 h-12 rounded-2xl bg-[var(--brand-primary)]/10 ring-1 ring-[var(--brand-ring)] flex items-center justify-center text-[var(--brand-primary)] shadow-sm">
          <RefreshCw className="w-6 h-6 animate-pulse" />
        </div>
        <div className="space-y-2">
          <h2
            id="update-dialog-title"
            className="text-base font-semibold text-[var(--text-main)]"
          >
            A new version of AccountGo is available
          </h2>
          <p
            id="update-dialog-body"
            className="text-xs text-[var(--text-muted)] leading-relaxed"
          >
            Reload to get the latest fixes and improvements. Save anything you're
            working on first — reloading discards unsaved changes.
          </p>
        </div>
        <div className="flex items-center justify-end gap-3 w-full pt-2">
          <button
            type="button"
            onClick={handleLater}
            className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--border-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)] transition"
          >
            Later
          </button>
          <button
            type="button"
            onClick={handleReload}
            className="px-4 py-2 text-xs font-semibold rounded-lg bg-[var(--brand-primary)] text-white hover:opacity-90 active:scale-95 transition"
          >
            Reload now
          </button>
        </div>
      </div>
    </ModalShell>
  );
};

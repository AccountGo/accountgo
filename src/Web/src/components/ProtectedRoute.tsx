import React from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { Loader2 } from 'lucide-react';
import { ServiceStatusCard } from './ServiceStatusOverlay';
import { getServiceStatus } from '../lib/serviceStatus';

export const ProtectedRoute: React.FC = () => {
  const { isAuthenticated, isLoading, isStarting, signedOutNotice } = useAuth();
  const location = useLocation();

  if (isLoading) {
    if (isStarting) {
      const status = getServiceStatus();
      return (
        <div className="flex min-h-dvh w-screen items-center justify-center bg-[var(--bg-canvas)] p-4 pt-[max(1rem,env(safe-area-inset-top))] pb-[max(1rem,env(safe-area-inset-bottom))] pl-[max(1rem,env(safe-area-inset-left))] pr-[max(1rem,env(safe-area-inset-right))] transition-colors duration-200">
          <ServiceStatusCard
            code={status.code || 'SERVICE_STARTING'}
            message={status.message}
            until={status.until}
            since={status.since}
            hideControls
          />
        </div>
      );
    }

    return (
      <div className="flex h-dvh w-screen items-center justify-center bg-[var(--bg-canvas)] text-[var(--text-main)] transition-colors duration-200">
        <div className="flex flex-col items-center gap-4">
          <div className="relative">
            <div className="w-16 h-16 rounded-2xl bg-[var(--brand-primary)]/10 ring-1 ring-[var(--brand-ring)] flex items-center justify-center shadow-2xl shadow-[var(--brand-primary)]/20 animate-pulse">
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
          <div className="flex flex-col items-center gap-1">
            <span className="text-sm font-semibold text-[var(--text-main)]">
              Verifying Session
            </span>
            <span className="text-xs text-[var(--text-muted)] text-center max-w-xs" role="status">
              Authenticating with AccountGo Cloud...
            </span>
          </div>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location, ...(signedOutNotice ? { notice: signedOutNotice } : {}) }} replace />;
  }

  return <Outlet />;
};

export default ProtectedRoute;

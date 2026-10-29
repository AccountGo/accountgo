import React from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { Loader2 } from 'lucide-react';

export const ProtectedRoute: React.FC = () => {
  const { isAuthenticated, isLoading, isStarting } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-[var(--bg-canvas)] text-[var(--text-main)] transition-colors duration-200">
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
              {isStarting ? 'AccountGo is starting up' : 'Verifying Session'}
            </span>
            <span className="text-xs text-[var(--text-muted)] text-center max-w-xs" role="status">
              {isStarting
                ? "This can take a few minutes after an update. You'll stay signed in."
                : 'Authenticating with AccountGo Cloud...'}
            </span>
          </div>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  return <Outlet />;
};

export default ProtectedRoute;

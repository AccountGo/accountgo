import React from 'react';
import { Link } from 'react-router-dom';
import { HelpCircle, ArrowLeft, Building2 } from 'lucide-react';

export const NotFound: React.FC = () => {
  return (
    <div className="min-h-screen bg-[var(--bg-canvas)] flex items-center justify-center p-4 sm:p-6 text-[var(--text-main)] safe-area-all transition-colors duration-200">
      <div className="max-w-md w-full bg-[var(--bg-surface)] backdrop-blur-xl rounded-3xl p-6 sm:p-8 shadow-2xl text-center">
        <div className="w-14 h-14 rounded-2xl bg-amber-500/10 text-amber-400 flex items-center justify-center mx-auto mb-4 shadow-sm">
          <HelpCircle className="w-7 h-7" />
        </div>
        <div className="font-mono text-xs uppercase tracking-widest text-[var(--brand-primary)] font-semibold mb-1">
          404 Error
        </div>
        <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] mb-2">Page Not Found</h1>
        <p className="text-xs sm:text-sm text-[var(--text-muted)] mb-6 leading-relaxed">
          The requested URL path does not exist in the AccountGo multi-tenant system.
        </p>

        <div className="flex flex-col gap-2.5">
          <Link
            to="/select-org"
            className="btn-primary !h-11 w-full"
          >
            <Building2 className="w-4 h-4" />
            <span>Select Organization</span>
          </Link>
          <Link
            to="/"
            className="w-full h-10 inline-flex items-center justify-center gap-2 px-4 rounded-xl text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold transition-all active:scale-[0.98]"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
            <span>Back to Home</span>
          </Link>
        </div>
      </div>
    </div>
  );
};

export default NotFound;

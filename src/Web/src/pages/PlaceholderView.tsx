import React from 'react';
import { useOutletContext, useLocation, Link } from 'react-router-dom';
import { Layers, ArrowLeft, Sparkles } from 'lucide-react';
import { TenantContextType } from '../types';

export const PlaceholderView: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const location = useLocation();

  const pathParts = location.pathname.split('/').filter(Boolean);
  const currentSection = pathParts[2] || 'Module';
  const title = currentSection.charAt(0).toUpperCase() + currentSection.slice(1);

  return (
    <div className="p-5 sm:p-8 rounded-3xl bg-[var(--bg-surface)] backdrop-blur-md shadow-xl text-center max-w-2xl mx-auto space-y-4 my-4 sm:my-8 text-[var(--text-main)] transition-colors duration-200">
      <div className="w-12 h-12 rounded-2xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center mx-auto shadow-sm">
        <Layers className="w-6 h-6" />
      </div>
      <div>
        <h2 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] capitalize">{title} Module Skeleton</h2>
        <p className="text-xs sm:text-sm text-[var(--text-muted)] mt-1">
          Scoped to tenant workspace <code className="text-[var(--brand-primary)] font-mono">/org/{currentOrg.slug}/{currentSection}</code>.
        </p>
      </div>

      <div className="p-4 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-xs text-[var(--text-muted)] text-left space-y-2">
        <div className="flex items-center gap-2 text-[var(--text-main)] font-semibold">
          <Sparkles className="w-4 h-4 text-[var(--brand-primary)] shrink-0" />
          <span>Multi-Tenant State Verified</span>
        </div>
        <p className="leading-relaxed">
          This view receives <code className="text-[var(--text-secondary)]">currentOrg</code> via React Router's <code className="text-[var(--text-secondary)]">useOutletContext()</code>. When features are implemented in subsequent gates, API calls to <code className="text-[var(--text-secondary)]">/api/{currentSection}</code> will carry the active organization context.
        </p>
      </div>

      <div className="pt-2">
        <Link
          to={`/org/${currentOrg.slug}/dashboard`}
          className="h-10 inline-flex items-center justify-center gap-2 px-4 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-[var(--text-main)] text-xs font-semibold transition-all shadow-sm active:scale-[0.98]"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          <span>Back to Dashboard</span>
        </Link>
      </div>
    </div>
  );
};

export default PlaceholderView;

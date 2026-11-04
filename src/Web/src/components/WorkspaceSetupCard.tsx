import React, { useCallback, useEffect, useState } from 'react';
import { AlertCircle, ClipboardList } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import type { WorkspaceOperatingPolicySummary } from '../types';
import { buildWorkspaceSetupRows, workspaceSetupIsConfigured } from '../lib/workspaceSetupLabels';

const OPERATING_SETTINGS_PATH = '/api/organizations/current/operating-settings';

/**
 * Read-only display of the workspace setup answers from CreateOrg Review
 * (purpose, who uses it, access, journal posting) for Organization Settings.
 */
export const WorkspaceSetupCard: React.FC = () => {
  const { authFetch } = useAuth();
  const [settings, setSettings] = useState<WorkspaceOperatingPolicySummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await authFetch(OPERATING_SETTINGS_PATH);
      if (!response.ok) {
        setError(`Could not load workspace setup (${response.status}).`);
        return;
      }
      const parsed = await response.json();
      setSettings(parsed && !Array.isArray(parsed) ? parsed : null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load workspace setup.');
    } finally {
      setLoading(false);
    }
  }, [authFetch]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) return null;

  if (error) {
    return (
      <div role="alert" className="p-4 rounded-2xl bg-red-500/10 text-red-400 text-xs font-semibold flex items-center gap-2.5">
        <AlertCircle className="w-4 h-4 shrink-0" />
        <span>{error}</span>
      </div>
    );
  }

  if (!settings) return null;

  const configured = workspaceSetupIsConfigured(settings);
  const rows = buildWorkspaceSetupRows(settings);

  return (
    <section
      aria-labelledby="workspace-setup-heading"
      className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4"
      data-testid="workspace-setup-card"
    >
      <div className="flex items-center gap-2.5 pb-2 border-b border-[var(--border-subtle)]">
        <ClipboardList className="w-5 h-5 text-[var(--brand-primary)]" />
        <div>
          <h2 id="workspace-setup-heading" className="text-sm sm:text-base font-bold text-[var(--text-main)]">
            Workspace setup
          </h2>
          <p className="text-xs text-[var(--text-muted)]">
            Answers from when this workspace was set up. Journal approval can be changed under Access &amp; Billing.
          </p>
        </div>
      </div>

      {!configured ? (
        <p className="text-sm text-[var(--text-secondary)]" data-testid="workspace-setup-unset">
          Purpose and collaboration were not recorded for this workspace yet.
        </p>
      ) : (
        <dl className="rounded-xl bg-[var(--bg-subtle)] divide-y divide-[var(--bg-surface)]" data-testid="workspace-setup-rows">
          {rows.map(([label, value]) => (
            <div key={label} className="flex items-start justify-between gap-4 px-3.5 py-2.5 text-sm">
              <dt className="text-[var(--text-muted)]">{label}</dt>
              <dd className="text-[var(--text-main)] font-medium text-right break-words max-w-[65%]">{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </section>
  );
};

export default WorkspaceSetupCard;

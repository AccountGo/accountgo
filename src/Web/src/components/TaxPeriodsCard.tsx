import React, { useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { TaxPeriodFrequency, TaxPeriodSettings } from '../lib/taxReport';

const QUARTER_CYCLES: Array<{ value: 1 | 2 | 3; label: string }> = [
  { value: 1, label: 'Jan\u2013Mar, Apr\u2013Jun, Jul\u2013Sep, Oct\u2013Dec' },
  { value: 2, label: 'Feb\u2013Apr, May\u2013Jul, Aug\u2013Oct, Nov\u2013Jan' },
  { value: 3, label: 'Mar\u2013May, Jun\u2013Aug, Sep\u2013Nov, Dec\u2013Feb' },
];

export const TaxPeriodsCard: React.FC<{ canManage: boolean }> = ({ canManage }) => {
  const { authFetch } = useAuth();
  const [settings, setSettings] = useState<TaxPeriodSettings | null>(null);
  const [frequency, setFrequency] = useState<TaxPeriodFrequency>('Quarterly');
  const [cycleChoice, setCycleChoice] = useState<number>(1);
  const [saving, setSaving] = useState(false);
  const [savedNotice, setSavedNotice] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    authFetch('/api/tax/settings')
      .then(async (res) => {
        if (!res.ok || !active) return;
        const data = (await res.json()) as TaxPeriodSettings;
        setSettings(data);
        setFrequency(data.frequency);
        setCycleChoice(data.effectiveQuarterCycleStartMonth);
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [authFetch]);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canManage || !settings) return;
    setSaving(true);
    setError(null);
    setSavedNotice(false);
    try {
      const quarterCycleStartMonth =
        frequency === 'Monthly'
          ? settings.quarterCycleStartMonth
          : cycleChoice === settings.defaultQuarterCycleStartMonth
            ? null
            : cycleChoice;

      const res = await authFetch('/api/tax/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ frequency, quarterCycleStartMonth }),
      });
      if (!res.ok) {
        const body = (await res.json().catch(() => null)) as { message?: string } | null;
        throw new Error(body?.message || 'Tax period settings could not be saved.');
      }
      const updated = (await res.json()) as TaxPeriodSettings;
      setSettings(updated);
      setFrequency(updated.frequency);
      setCycleChoice(updated.effectiveQuarterCycleStartMonth);
      setSavedNotice(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Tax period settings could not be saved.');
    } finally {
      setSaving(false);
    }
  };

  const defaultCycle = settings?.defaultQuarterCycleStartMonth ?? 1;

  return (
    <form
      onSubmit={(e) => void handleSave(e)}
      className="card p-5 space-y-4 border border-[var(--border-subtle)] bg-[var(--bg-surface)] rounded-2xl"
    >
      <div>
        <h3 className="text-base font-bold text-[var(--text-main)]">Tax periods</h3>
        <p className="text-xs text-[var(--text-muted)] mt-0.5">
          Tax periods don&apos;t have to match your fiscal year.
        </p>
      </div>

      <div className="space-y-3">
        <div>
          <span className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
            How often do you file?
          </span>
          <div className="flex flex-wrap gap-3">
            {(['Monthly', 'Quarterly'] as const).map((opt) => (
              <label key={opt} className="inline-flex items-center gap-2 text-sm text-[var(--text-main)]">
                <input
                  type="radio"
                  name="tax-period-frequency"
                  value={opt}
                  checked={frequency === opt}
                  onChange={() => setFrequency(opt)}
                  disabled={!canManage}
                />
                {opt}
              </label>
            ))}
          </div>
        </div>

        {frequency === 'Quarterly' && (
          <div>
            <label
              htmlFor="tax-quarter-cycle"
              className="block text-xs font-semibold text-[var(--text-secondary)] mb-1"
            >
              Quarters run
            </label>
            <select
              id="tax-quarter-cycle"
              value={cycleChoice}
              onChange={(e) => setCycleChoice(Number(e.target.value))}
              disabled={!canManage}
              className="w-full max-w-md rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-page)] px-3 py-2 text-sm text-[var(--text-main)]"
            >
              {QUARTER_CYCLES.map((cycle) => (
                <option key={cycle.value} value={cycle.value}>
                  {cycle.label}
                  {cycle.value === defaultCycle ? ' (from your fiscal year)' : ''}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>

      {error && (
        <p role="alert" className="text-sm text-rose-400">
          {error}
        </p>
      )}
      {savedNotice && (
        <p role="status" className="text-xs text-emerald-600 dark:text-emerald-400">
          Tax period settings saved.
        </p>
      )}

      {canManage && (
        <div className="flex justify-end">
          <button
            type="submit"
            disabled={saving || !settings}
            className="btn-primary !h-10 !px-4 text-xs"
          >
            {saving ? 'Saving…' : 'Save tax periods'}
          </button>
        </div>
      )}
    </form>
  );
};

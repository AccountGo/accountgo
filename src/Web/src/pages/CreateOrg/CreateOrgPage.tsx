import React, { useEffect, useRef } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { AlertCircle, ArrowLeft, ArrowRight, Check, Layers, Loader2 } from 'lucide-react';
import { useCreateOrgWizard } from './hooks/useCreateOrgWizard';
import { IdentityStep } from './steps/IdentityStep';
import { IntentStep } from './steps/IntentStep';
import { MoneyTimeStep } from './steps/MoneyTimeStep';
import { ReportingStep } from './steps/ReportingStep';
import { ReviewStep } from './steps/ReviewStep';
import type { StepId } from './lib/policyDefaults';

const TITLES: Record<StepId, { title: string; subtitle: string }> = {
  intent: { title: 'What do you need?', subtitle: 'Pick the plan that fits. You can change it later.' },
  identity: { title: 'Create your workspace', subtitle: 'Name it and choose its web address.' },
  moneyTime: { title: 'Money & time', subtitle: 'Your books currency and when they begin.' },
  reporting: { title: 'How you report', subtitle: 'These choices shape your chart of accounts and reports.' },
  review: { title: 'Review & create', subtitle: 'Check everything before we set up your books.' },
};

export const CreateOrgPage: React.FC = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const wizard = useCreateOrgWizard({
    name: searchParams.get('name') ?? undefined,
    slug: searchParams.get('slug') ?? undefined,
  });
  const { state, update, step, stepIndex, stepCount, errors, status, submitError, next, back, submit } = wizard;
  const headingRef = useRef<HTMLHeadingElement>(null);
  const busy = status !== 'idle';

  // Move focus to the step heading so keyboard and screen reader users land at the top of each step.
  useEffect(() => {
    headingRef.current?.focus();
  }, [step]);

  const onSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    if (busy) return;
    if (step === 'review') void submit();
    else next();
  };

  const { title, subtitle } = TITLES[step];

  return (
    <div className="min-h-screen bg-[var(--bg-canvas)] flex flex-col">
      <header className="px-4 sm:px-6 py-4 flex items-center gap-2.5">
        <div className="w-9 h-9 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center">
          <Layers className="w-5 h-5" aria-hidden="true" />
        </div>
        <span className="font-bold text-[var(--text-main)] text-sm">New workspace</span>
        <button
          type="button"
          onClick={() => navigate('/select-org')}
          disabled={busy}
          className="ml-auto h-9 px-3 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition-colors cursor-pointer disabled:opacity-50"
        >
          Cancel
        </button>
      </header>

      <main className="flex-1 flex items-start sm:items-center justify-center px-4 pb-8">
        <div className="w-full max-w-2xl bg-[var(--bg-surface)] rounded-2xl p-5 sm:p-7 shadow-2xl shadow-black/40">
          <div
            role="progressbar"
            aria-valuemin={1}
            aria-valuemax={stepCount}
            aria-valuenow={stepIndex + 1}
            aria-label={`Step ${stepIndex + 1} of ${stepCount}`}
            className="h-1.5 rounded-full bg-[var(--bg-subtle)] overflow-hidden mb-5"
          >
            <div
              className="h-full rounded-full bg-[var(--brand-primary)] transition-all duration-300 ease-out"
              style={{ width: `${((stepIndex + 1) / stepCount) * 100}%` }}
            />
          </div>

          {status === 'success' ? (
            <div role="status" className="py-10 flex flex-col items-center gap-3 text-center">
              <div className="w-14 h-14 rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center animate-in zoom-in-50 duration-300">
                <Check className="w-7 h-7" aria-hidden="true" />
              </div>
              <p className="font-bold text-[var(--text-main)]">Your workspace is ready</p>
              <p className="text-xs text-[var(--text-muted)]">Opening your dashboard…</p>
            </div>
          ) : (
            <form onSubmit={onSubmit} noValidate>
              <p className="text-[11px] uppercase tracking-wider font-semibold text-[var(--text-muted)]">
                Step {stepIndex + 1} of {stepCount}
              </p>
              <h1 ref={headingRef} tabIndex={-1} className="text-lg font-bold text-[var(--text-main)] focus:outline-none">{title}</h1>
              <p className="text-xs text-[var(--text-muted)] mb-5">{subtitle}</p>

              {submitError && (
                <div role="alert" className="flex items-start gap-2 p-3 mb-4 rounded-xl bg-rose-500/10 text-rose-400 text-xs font-medium ring-1 ring-rose-500/20">
                  <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" aria-hidden="true" />
                  <span className="flex-1">{submitError}</span>
                </div>
              )}

              {step === 'intent' && <IntentStep plan={state.plan} onChange={(plan) => update({ plan })} />}
              {step === 'identity' && <IdentityStep state={state} errors={errors} update={update} />}
              {step === 'moneyTime' && <MoneyTimeStep state={state} errors={errors} update={update} />}
              {step === 'reporting' && <ReportingStep state={state} update={update} />}
              {step === 'review' && <ReviewStep state={state} />}

              <div className="pt-6 flex flex-col-reverse sm:flex-row items-stretch sm:items-center justify-between gap-2.5">
                <button
                  type="button"
                  onClick={back}
                  disabled={stepIndex === 0 || busy}
                  className="h-11 px-4 rounded-xl text-sm font-semibold text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition-colors cursor-pointer disabled:opacity-40 disabled:pointer-events-none inline-flex items-center justify-center gap-2"
                >
                  <ArrowLeft className="w-4 h-4" aria-hidden="true" /> Back
                </button>
                <button type="submit" disabled={busy} aria-busy={status === 'submitting'} className="btn-primary !h-11">
                  {status === 'submitting' ? (
                    <><Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Setting up your chart of accounts…</>
                  ) : step === 'review' ? (
                    'Create workspace'
                  ) : (
                    <>Next <ArrowRight className="w-4 h-4" aria-hidden="true" /></>
                  )}
                </button>
              </div>
            </form>
          )}
        </div>
      </main>
    </div>
  );
};

export default CreateOrgPage;

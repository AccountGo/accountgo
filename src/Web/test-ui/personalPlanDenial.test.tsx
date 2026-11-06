import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen } from '@testing-library/react';
import { PlanLimitDialog, PlanLimitDialogView } from '../src/components/PlanLimitDialog';
import { ENTITLEMENT_DENIED_EVENT, setEntitlementWorkspace } from '../src/lib/entitlementEvents';
afterEach(cleanup);
it('explains a personal books limit without upgrade or billing actions', () => {
  render(<PlanLimitDialogView personal problem={{ code: 'QUOTA_EXCEEDED', detail: 'Upgrade to continue', canManageBilling: true, upgradeAvailable: true, used: 1, limit: 1 }} onClose={vi.fn()} />);
  expect(screen.getByText(/1 of 1 used/)).toBeTruthy();
  expect(document.body.textContent).not.toMatch(/upgrade|billing|subscription/i);
});

it('the global denial dialog uses the active personal workspace', () => {
  window.history.pushState({}, '', '/org/personal/banking');
  const clear = setEntitlementWorkspace('personal', true);
  render(<PlanLimitDialog />);
  act(() => window.dispatchEvent(new CustomEvent(ENTITLEMENT_DENIED_EVENT, { detail: { code: 'QUOTA_EXCEEDED', detail: 'Upgrade now', canManageBilling: true, upgradeAvailable: true } })));
  expect(screen.getByText('This action is unavailable in these books.')).toBeTruthy();
  expect(document.body.textContent).not.toMatch(/upgrade|billing/i);
  clear();
});

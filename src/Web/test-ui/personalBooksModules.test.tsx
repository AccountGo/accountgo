import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { SubscriptionSettingsTab } from '../src/components/SubscriptionSettingsTab';
import type { TenantContextType } from '../src/types';
vi.mock('../src/lib/usePlanCatalog', () => ({ usePlanCatalog: () => ({ plans: [], includedModules: () => [], isIncluded: () => false, isRequired: () => false }) }));
vi.mock('../src/lib/entitlements', () => ({ useCapabilities: () => ({ entitlements: null, allowedTransitions: [], reload: vi.fn(), startBillingReturnPoll: vi.fn(), version: null }) }));
vi.mock('../src/lib/useBilling', () => ({ useBilling: () => ({ billingEnabled: true, status: 'active', plan: 'household-test', billing: { tier: 'free' }, reload: vi.fn() }) }));
afterEach(cleanup);
it('personal books use workspace module metadata without showing sales or upgrade prompts', () => {
  const context = { currentOrg: { isPersonalBooks: true, includedModules: ['financials', 'purchases', 'reports'], requiredModules: [] },
    subscription: { planId: 'household-test', planName: 'Personal', activeModules: ['financials', 'purchases', 'reports'] },
    updateSubscription: vi.fn() } as unknown as TenantContextType;
  render(<SubscriptionSettingsTab tenantContext={context} />);
  expect(screen.queryByText('Change plan')).toBeNull();
  expect(screen.queryByText('Invoicing & Sales')).toBeNull();
  expect(screen.queryByText('Upgrade')).toBeNull();
  expect(screen.getByText('General Ledger & Banking')).toBeTruthy();
  expect(screen.getAllByRole('switch').every(toggle => toggle.getAttribute('aria-checked') === 'true')).toBe(true);
});

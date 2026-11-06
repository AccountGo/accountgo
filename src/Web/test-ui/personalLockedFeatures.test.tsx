import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import { RequiresModule, RequiresStatementImport } from '../src/components/Gating';
vi.mock('../src/lib/entitlements', () => ({ FEATURE_BANKING_STATEMENT_IMPORT: 'banking.statement_import', useCapabilities: () => ({ canStatementImport: false, entitlements: null }) }));
afterEach(cleanup);
function show(child: React.ReactNode) {
  const context = { currentOrg: { slug: 'personal', isPersonalBooks: true }, hasModule: () => false, hasPermission: () => true };
  render(<MemoryRouter initialEntries={['/feature']}><Routes><Route element={<Outlet context={context} />}><Route path="/feature" element={child} /></Route></Routes></MemoryRouter>);
}
it('personal books do not show upgrade prompts on unavailable features', () => {
  show(<RequiresModule module="sales"><p>Available</p></RequiresModule>);
  expect(screen.queryByText(/upgrade/i)).toBeNull();
  cleanup();
  show(<RequiresStatementImport><p>Available</p></RequiresStatementImport>);
  expect(screen.queryByText(/upgrade/i)).toBeNull();
});

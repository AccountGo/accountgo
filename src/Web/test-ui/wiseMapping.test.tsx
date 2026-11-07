import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
import BankStatementImportPage from '../src/pages/BankStatementImport';
const { fetcher } = vi.hoisted(() => ({ fetcher: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: fetcher }) }));
vi.mock('../src/components/Gating', () => ({ useTenantContext: () => ({ hasPermission: () => true }) }));
vi.mock('../src/lib/entitlements', () => ({ useCapabilities: () => ({ canModule: () => false, canWrite: () => false, canCreate: () => false }) }));
vi.mock('../src/lib/integrations', () => ({ refreshIncomingReview: () => {} }));
afterEach(() => { cleanup(); fetcher.mockReset(); });
it.each([1, 0])('updates the map and retains skipped details with %i ready rows', async (readyRows) => {
  const response = (body: unknown) => ({ ok: true, json: async () => body });
  let submitted: any;
  fetcher.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url.includes('ResolveMapping')) return response({ mappingSource: 'preset', presetName: 'Wise activity', columnMap: { hasHeaderRow: true, dateColumn: 0, directionColumn: 1, skippedDirections: ['neutral', 'internal'], amountColumn: 3, amountCarriesSign: true, invertSign: false } });
    if (url.endsWith('/Preview')) { submitted = JSON.parse(String(init?.body)); return response({ totalRows: 2, readyRows, duplicateRows: 0, errorRows: 0, skippedRows: 1, rows: [] }); }
    if (url.endsWith('/StatementImports')) { submitted = JSON.parse(String(init?.body)); return response({ imported: readyRows, duplicates: 0, errors: 0, skipped: 1, autoMatched: 0, skippedRowDetails: [{ rowNumber: 3, error: 'neutral conversion skipped' }] }); }
    if (url.includes('/StatementLines')) return response({ lines: [], totalCount: 0, page: 1, pageSize: 100, unmatchedCount: 0, matchedCount: 0, booksStartDate: "2024-01-01", beforeBooksStartCount: 1 });
    return response({ id: 1, name: 'Wise', currencyCode: 'USD' });
  });
  render(<MemoryRouter initialEntries={['/bank/1']}><Routes><Route element={<Outlet context={{ currentOrg: { id: 1, slug: 'books', currency: 'USD' } }} />}><Route path="/bank/:bankId" element={<BankStatementImportPage />} /></Route></Routes></MemoryRouter>);
  const input = await screen.findByLabelText('Statement file');
  const data = new TextEncoder().encode('date,direction,amount,signed_amount\n2024-01-01,out,10,-10');
  const file = new File([data], 'wise.csv', { type: 'text/csv' });
  Object.defineProperty(file, 'arrayBuffer', { value: async () => data.buffer });
  fireEvent.change(input, { target: { files: [file] } });
  await waitFor(() => expect(screen.getByLabelText('Amount interpretation')).toBeTruthy());
  fireEvent.change(screen.getByLabelText('Direction (in/out)'), { target: { value: '' } });
  expect(screen.getByText(/Select a direction column/)).toBeTruthy();
  expect((screen.getByRole('button', { name: 'Preview' }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.change(screen.getByLabelText('Direction (in/out)'), { target: { value: '1' } });
  fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '2' } });
  expect((screen.getByLabelText('Amount interpretation') as HTMLSelectElement).value).toBe('false');
  fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '3' } });
  expect((screen.getByLabelText('Amount interpretation') as HTMLSelectElement).value).toBe('true');
  fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '2' } });
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  await waitFor(() => expect(screen.getByRole('button', { name: `Import ${readyRows} ${readyRows === 1 ? 'line' : 'lines'}` })).toBeTruthy());
  expect(submitted.columnMap.amountCarriesSign).toBe(false);
  fireEvent.click(screen.getByRole('button', { name: `Import ${readyRows} ${readyRows === 1 ? 'line' : 'lines'}` }));
  await screen.findByText('Row 3: neutral conversion skipped');
  expect(screen.getAllByLabelText('Skipped rows')).toHaveLength(1);
  expect(submitted.columnMap.amountColumn).toBe(2);
  expect(submitted.columnMap.amountCarriesSign).toBe(false);
  expect(screen.getByText(/1 skipped/)).toBeTruthy();
});


it.each(['ofx', 'xml', 'mt940', 'qif'])('allows preview of %s after an unresolved CSV amount mapping', async (extension) => {
  const response = (body: unknown) => ({ ok: true, json: async () => body });
  fetcher.mockImplementation(async (url: string) => {
    if (url.includes('ResolveMapping')) return response({ mappingSource: 'preset', columnMap: { hasHeaderRow: true, dateColumn: 0, directionColumn: 1, amountColumn: 3, amountCarriesSign: true, invertSign: false } });
    if (url.includes('/StatementLines')) return response({ lines: [], totalCount: 0, page: 1, pageSize: 100, unmatchedCount: 0, matchedCount: 0 });
    return response({ id: 1, name: 'Wise', currencyCode: 'USD' });
  });
  render(<MemoryRouter initialEntries={['/bank/1']}><Routes><Route element={<Outlet context={{ currentOrg: { id: 1, slug: 'books', currency: 'USD' } }} />}><Route path="/bank/:bankId" element={<BankStatementImportPage />} /></Route></Routes></MemoryRouter>);
  const input = await screen.findByLabelText('Statement file');
  const data = new TextEncoder().encode('date,direction,amount,signed_amount,custom\n2024-01-01,out,10,-10,10');
  const csv = new File([data], 'wise.csv');
  Object.defineProperty(csv, 'arrayBuffer', { value: async () => data.buffer });
  fireEvent.change(input, { target: { files: [csv] } });
  await screen.findByLabelText('Amount interpretation');
  fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '4' } });
  expect((screen.getByRole('button', { name: 'Preview' }) as HTMLButtonElement).disabled).toBe(true);
  const other = new File([data], `statement.${extension}`);
  Object.defineProperty(other, 'arrayBuffer', { value: async () => data.buffer });
  fireEvent.change(input, { target: { files: [other] } });
  await waitFor(() => expect((screen.getByRole('button', { name: 'Preview' }) as HTMLButtonElement).disabled).toBe(false));
});

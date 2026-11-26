// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { act, cleanup, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Documents } from '../src/pages/Documents';
import { DocumentDetailDialog } from '../src/components/documents/DocumentDetailDialog';
import { WorkspaceDeleteDocumentsStep } from '../src/components/documents/WorkspaceDeleteDocumentsStep';
import { useDeleteCurrentWorkspace } from '../src/lib/useDeleteCurrentWorkspace';
import { Sidebar } from '../src/components/Sidebar';
import { RecordAttachments } from '../src/components/documents/RecordAttachments';
import { DownloadAllDocuments } from '../src/components/documents/DownloadAllDocuments';
import type { DocumentCapabilities, LibraryDocument } from '../src/lib/documents';

const { authFetch, outlet, navigate, permissions } = vi.hoisted(() => ({
  permissions: { set: new Set<string>(['documents.view']) },
  authFetch: vi.fn(),
  outlet: { currentOrg: { id: 'ws_1', name: 'Acme', slug: 'acme', role: 'Member' as string, currency: 'USD' } },
  navigate: vi.fn(),
}));

vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch }) }));
vi.mock('../src/components/Gating', () => ({
  usePermissions: () => ({ hasPermission: (p: string) => permissions.set.has(p) }),
}));
vi.mock('../src/components/StepUpPrompt', () => ({
  StepUpPrompt: ({ onVerified }: { onVerified: () => void }) => <button type="button" onClick={onVerified}>Verify code</button>,
}));
vi.mock('../src/components/OrgSwitcher', () => ({ OrgSwitcher: () => null }));
vi.mock('../src/components/UserProfileSection', () => ({ UserProfileSection: () => null }));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useOutletContext: () => outlet, useNavigate: () => navigate };
});

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const caps = (overrides: Partial<DocumentCapabilities> = {}): DocumentCapabilities => ({
  enabled: true,
  allowedMimeTypes: ['application/pdf', 'image/jpeg', 'image/png'],
  categories: ['contract'],
  libraryLimit: { maxBytes: 15 * 1024 * 1024, maxPdfPages: 300 },
  recordLimit: { maxBytes: 8 * 1024 * 1024, maxPdfPages: 10 },
  usedBytes: 2 * 1024 * 1024,
  storageLimitBytes: 100 * 1024 * 1024,
  canView: true,
  canManage: true,
  ...overrides,
});

const doc = (overrides: Partial<LibraryDocument> = {}): LibraryDocument => ({
  id: 'cap_01lease000000000000000001',
  kind: 'document',
  status: 'stored',
  title: 'Office lease',
  fileName: 'lease.pdf',
  category: 'contract',
  documentDate: '2026-01-15',
  notes: null,
  contentType: 'application/pdf',
  byteCount: 120_000,
  pageCount: 3,
  width: null,
  height: null,
  storedSha256: 'a'.repeat(64),
  uploadedSha256: 'a'.repeat(64),
  storedCopyOptimised: false,
  archived: false,
  inTrash: false,
  filedAt: '2026-11-20T10:00:00Z',
  createdAt: '2026-11-20T10:00:00Z',
  links: [],
  canManage: true,
  hasLockedLink: false,
  hasSourceLink: false,
  ...overrides,
});

beforeEach(() => {
  outlet.currentOrg.role = 'Member';
  if (!globalThis.crypto?.randomUUID) {
    Object.defineProperty(globalThis, 'crypto', { value: { randomUUID: () => 'uuid-1' }, configurable: true });
  }
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('Documents page', () => {
  it('lists documents, shows storage, and sends tab, category and search to the server', async () => {
    const urls: string[] = [];
    authFetch.mockImplementation(async (url: string) => {
      urls.push(url);
      if (url === '/api/documents/capabilities') return json(caps());
      if (url.startsWith('/api/documents?')) return json({ items: [doc()], total: 1, page: 1, pageSize: 50 });
      return json({}, 404);
    });
    render(<MemoryRouter><Documents /></MemoryRouter>);

    expect(await screen.findByText('Office lease')).toBeTruthy();
    expect(screen.getByText(/2 MB of 100 MB used\. Files in the trash still count\./)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Download all documents/ })).toBeNull();

    fireEvent.click(screen.getByRole('tab', { name: 'Trash' }));
    await waitFor(() => expect(urls.some((u) => u.includes('trash=true'))).toBe(true));
    fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'contract' } });
    await waitFor(() => expect(urls.some((u) => u.includes('category=contract'))).toBe(true));
    fireEvent.change(screen.getByLabelText('Search documents'), { target: { value: 'lease' } });
    await waitFor(() => expect(urls.some((u) => u.includes('q=lease'))).toBe(true), { timeout: 2000 });
  });

  it('owners get Download all documents', async () => {
    outlet.currentOrg.role = 'Owner';
    authFetch.mockImplementation(async (url: string) =>
      url === '/api/documents/capabilities' ? json(caps()) : json({ items: [], total: 0, page: 1, pageSize: 50 }));
    render(<MemoryRouter><Documents /></MemoryRouter>);
    expect(await screen.findByRole('button', { name: /Download all documents/ })).toBeTruthy();
    expect(await screen.findByText(/Files arrive here three ways/)).toBeTruthy();
  });

  it('uploads with an idempotency key and offers open or copy for a duplicate', async () => {
    const posts: FormData[] = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/documents/capabilities') return json(caps());
      if (url === '/api/documents' && init?.method === 'POST') {
        posts.push(init.body as FormData);
        expect((init.headers as Record<string, string>)['Idempotency-Key']).toBeTruthy();
        return posts.length === 1
          ? json({ code: 'DOCUMENT_DUPLICATE', documentId: 'cap_01lease000000000000000001' }, 409)
          : json(doc({ id: 'cap_copy' }));
      }
      return json({ items: [], total: 0, page: 1, pageSize: 50 });
    });
    render(<MemoryRouter><Documents /></MemoryRouter>);
    const input = (await screen.findByLabelText('Choose files to upload')) as HTMLInputElement;
    const file = new File(['%PDF-1.4'], 'lease.pdf', { type: 'application/pdf' });
    fireEvent.change(input, { target: { files: [file] } });

    expect(await screen.findByText(/This file is already in Documents\./)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'upload a separate copy' }));
    await waitFor(() => expect(posts).toHaveLength(2));
    expect(posts[1].get('Force')).toBe('true');
    expect(posts[0].get('Force')).toBeNull();
  });

  it('says so when the library is switched off, with no upload button', async () => {
    authFetch.mockImplementation(async (url: string) =>
      url === '/api/documents/capabilities' ? json(caps({ enabled: false })) : json({ code: 'DOCUMENTS_DISABLED' }, 404));
    render(<MemoryRouter><Documents /></MemoryRouter>);
    expect(await screen.findByText(/isn't available in this workspace yet/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Upload files/ })).toBeNull();
  });

  it('explains storage and size limits in plain words', async () => {
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/documents/capabilities') return json(caps());
      if (init?.method === 'POST') return json({ code: 'DOCUMENT_STORAGE_LIMIT', usedBytes: 100 * 1024 * 1024, limitBytes: 100 * 1024 * 1024 }, 403);
      return json({ items: [], total: 0, page: 1, pageSize: 50 });
    });
    render(<MemoryRouter><Documents /></MemoryRouter>);
    const input = (await screen.findByLabelText('Choose files to upload')) as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File(['x'], 'a.pdf', { type: 'application/pdf' })] } });
    expect(await screen.findByText(/You've used 100 MB of 100 MB for documents\. Permanently delete files you no longer need \(files in the trash still count\)/)).toBeTruthy();
  });
});

describe('Document details', () => {
  it('saves details and keeps evidence for posted records from being removed', async () => {
    authFetch.mockImplementation(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'PATCH') {
        expect(JSON.parse(String(init.body))).toMatchObject({ title: 'Lease 2026', category: 'contract' });
        return json(doc({ title: 'Lease 2026' }));
      }
      return json({}, 404);
    });
    const onChanged = vi.fn();
    render(
      <MemoryRouter>
        <DocumentDetailDialog
          document={doc({ hasLockedLink: true, links: [{ targetType: 'bill', targetId: 'bill_1', role: 'attachment', linkedAt: '', locked: true }] })}
          orgSlug="acme" onChanged={onChanged} onRemoved={vi.fn()} onClose={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Kept as evidence for a posted bill or invoice/)).toBeTruthy();
    expect((screen.getByRole('button', { name: /Move to trash/ }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole('link', { name: /Bill/ }).getAttribute('href')).toBe('/org/acme/purchases/bill_1');

    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Lease 2026' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save details' }));
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith(expect.objectContaining({ title: 'Lease 2026' })));
  });

  it('tells the user how to remove a scanned source', () => {
    render(
      <MemoryRouter>
        <DocumentDetailDialog document={doc({ kind: 'purchase-invoice', hasSourceLink: true })} orgSlug="acme"
          onChanged={vi.fn()} onRemoved={vi.fn()} onClose={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Scanned source of a bill or invoice\. While the bill or invoice is a draft, deleting the draft frees this file/)).toBeTruthy();
    expect((screen.getByRole('button', { name: /Move to trash/ }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe('Workspace delete documents step', () => {
  it('is not needed without files', async () => {
    authFetch.mockResolvedValue(json({ filedCount: 0, filedBytes: 0, currentRevision: 0, latest: null }));
    const onReady = vi.fn();
    render(<WorkspaceDeleteDocumentsStep orgSlug="acme" choice={null} onChoice={vi.fn()} onReady={onReady} />);
    await waitFor(() => expect(onReady).toHaveBeenLastCalledWith(true));
  });

  it('needs a current export to confirm, or an explicit skip', async () => {
    authFetch.mockResolvedValue(json({
      filedCount: 3, filedBytes: 2048, currentRevision: 5,
      latest: { id: 'dex_1', status: 'server_finished', createdAt: '', completedAt: '', selectedCount: 3, selectedBytes: 2048, writtenCount: 3, failureReason: null, current: false },
    }));
    const onReady = vi.fn();
    const onChoice = vi.fn();
    const { rerender } = render(<WorkspaceDeleteDocumentsStep orgSlug="acme" choice={null} onChoice={onChoice} onReady={onReady} />);
    expect(await screen.findByText(/keeps 3 files/)).toBeTruthy();
    expect(screen.getByText(/Documents changed after your last download/)).toBeTruthy();
    expect((screen.getByLabelText(/I opened the downloaded ZIP/) as HTMLInputElement).disabled).toBe(true);
    expect(onReady).toHaveBeenLastCalledWith(false);
    fireEvent.click(screen.getByLabelText(/I don't need my documents/));
    expect(onChoice).toHaveBeenCalledWith('skip');
    rerender(<WorkspaceDeleteDocumentsStep orgSlug="acme" choice="skip" onChoice={onChoice} onReady={onReady} />);
    await waitFor(() => expect(onReady).toHaveBeenLastCalledWith(true));
  });

  it('sends the choice with the delete request and explains a server refusal', async () => {
    authFetch.mockResolvedValueOnce(json({ code: 'DOCUMENT_EXPORT_STALE' }, 409));
    const { result } = renderHook(() => useDeleteCurrentWorkspace(), { wrapper: MemoryRouter });
    await act(async () => {
      await result.current.remove('acme', { documentsExportConfirmed: true });
    });
    const [, init] = authFetch.mock.calls[0];
    expect(JSON.parse(String(init.body))).toEqual({ confirmSlug: 'acme', documentsExportConfirmed: true });
    expect(result.current.error).toMatch(/Documents changed after your last download/);
  });
});

describe('Sidebar', () => {
  const props = {
    currentOrg: { id: 'ws_1', name: 'Acme', slug: 'acme', role: 'Owner' } as never,
    availableOrgs: [],
    user: { id: 'u1', name: 'Owner', email: 'o@example.test' } as never,
  };

  it('shows Documents only when the library is on and the user may view it', () => {
    const { rerender } = render(<MemoryRouter><Sidebar {...props} showDocuments hasPermission={() => true} /></MemoryRouter>);
    expect(screen.getAllByRole('link', { name: /Documents/ }).length).toBeGreaterThan(0);
    rerender(<MemoryRouter><Sidebar {...props} showDocuments={false} hasPermission={() => true} /></MemoryRouter>);
    expect(screen.queryByRole('link', { name: /Documents/ })).toBeNull();
    rerender(<MemoryRouter><Sidebar {...props} showDocuments hasPermission={(p) => p !== 'documents.view'} /></MemoryRouter>);
    expect(screen.queryByRole('link', { name: /Documents/ })).toBeNull();
  });
});

describe('Record attachments', () => {
  const attached = (overrides: Partial<LibraryDocument> = {}) =>
    doc({ id: 'cap_receipt', title: 'Taxi receipt', links: [{ targetType: 'bill', targetId: 'bill_1', role: 'attachment', linkedAt: '', locked: false }], ...overrides });
  const scan = doc({ id: 'cap_scan', title: 'Scanned bill', kind: 'purchase-invoice', links: [{ targetType: 'bill', targetId: 'bill_1', role: 'source', linkedAt: '', locked: false }] });

  it('lists attachments (not the scanned source), uploads to the record and removes from a draft', async () => {
    let items = [attached(), scan];
    const calls: { url: string; init?: RequestInit }[] = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      calls.push({ url, init });
      if (url.startsWith('/api/documents/linked/bill/bill_1')) return json({ items, total: items.length, page: 1, pageSize: 100 });
      if (url === '/api/documents' && init?.method === 'POST') return json(attached({ id: 'cap_new', title: 'New' }));
      if (init?.method === 'DELETE') {
        items = [scan];
        return new Response(null, { status: 204 });
      }
      return json({}, 404);
    });
    render(<RecordAttachments targetType="bill" targetId="bill_1" canAttach canRemove recordLabel="bill" />);
    expect(await screen.findByText('Taxi receipt')).toBeTruthy();
    expect(screen.queryByText('Scanned bill')).toBeNull();

    fireEvent.change(screen.getByLabelText('Attach files to this bill'), { target: { files: [new File(['%PDF'], 'r.pdf', { type: 'application/pdf' })] } });
    await waitFor(() => expect(calls.some((c) => c.init?.method === 'POST')).toBe(true));
    const form = calls.find((c) => c.init?.method === 'POST')!.init!.body as FormData;
    expect([form.get('LinkTargetType'), form.get('LinkTargetId')]).toEqual(['bill', 'bill_1']);

    fireEvent.click(screen.getByRole('button', { name: 'Remove Taxi receipt' }));
    await waitFor(() => expect(screen.queryByText('Taxi receipt')).toBeNull());
    expect(calls.some((c) => c.url === '/api/documents/cap_receipt/links/bill/bill_1' && c.init?.method === 'DELETE')).toBe(true);
  });

  it('keeps attachments of a posted record and offers no removal', async () => {
    authFetch.mockResolvedValue(json({ items: [attached({ links: [{ targetType: 'bill', targetId: 'bill_1', role: 'attachment', linkedAt: '', locked: true }] })], total: 1, page: 1, pageSize: 100 }));
    render(<RecordAttachments targetType="bill" targetId="bill_1" canAttach canRemove={false} recordLabel="bill" />);
    expect(await screen.findByText('Kept as evidence for this posted bill')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Remove/ })).toBeNull();
  });

  it('links an existing library file, and only offers the library to users who can view it', async () => {
    const posts: string[] = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.startsWith('/api/documents/linked/')) return json({ items: [], total: 0, page: 1, pageSize: 100 });
      if (url.startsWith('/api/documents?')) return json({ items: [doc({ id: 'cap_contract', title: 'Supply contract' })], total: 1, page: 1, pageSize: 50 });
      if (url.endsWith('/links') && init?.method === 'POST') {
        posts.push(String(init.body));
        return json(doc());
      }
      return json({}, 404);
    });
    const { unmount } = render(<RecordAttachments targetType="vendor" targetId="ven_1" canAttach canRemove recordLabel="vendor" />);
    fireEvent.click(await screen.findByRole('button', { name: /Choose from Documents/ }));
    fireEvent.click(await screen.findByText('Supply contract'));
    await waitFor(() => expect(posts).toHaveLength(1));
    expect(JSON.parse(posts[0])).toEqual({ targetType: 'vendor', targetId: 'ven_1' });
    unmount();

    permissions.set = new Set<string>();
    render(<RecordAttachments targetType="vendor" targetId="ven_1" canAttach canRemove recordLabel="vendor" />);
    await screen.findByText(/No files attached/);
    expect(screen.queryByRole('button', { name: /Choose from Documents/ })).toBeNull();
    permissions.set = new Set(['documents.view']);
  });

  it('is hidden when the library is switched off', async () => {
    authFetch.mockResolvedValue(json({ code: 'DOCUMENTS_DISABLED' }, 404));
    const { container } = render(<RecordAttachments targetType="invoice" targetId="inv_1" canAttach canRemove recordLabel="invoice" />);
    await waitFor(() => expect(container.querySelector('section')).toBeNull());
  });
});

describe('Download all documents', () => {
  it('after the MFA step-up, waits for a fresh click instead of starting on its own', async () => {
    let posts = 0;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/documents/exports' && init?.method === 'POST') {
        posts += 1;
        return json({ code: 'mfa_step_up_required' }, 403);
      }
      return json({}, 404);
    });
    render(<DownloadAllDocuments orgSlug="acme" />);
    fireEvent.click(screen.getByRole('button', { name: /Download all documents/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'Verify code' }));
    expect(await screen.findByText(/Verified\. Click Download all documents again to start\./)).toBeTruthy();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(posts).toBe(1);
  });

  it('without a save-to-disk dialog, refuses exports too large to hold in memory before downloading', async () => {
    const calls: string[] = [];
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      calls.push(`${init?.method ?? 'GET'} ${url}`);
      if (url === '/api/documents/exports' && init?.method === 'POST')
        return json({ id: 'dex_1', status: 'ready', createdAt: '', completedAt: null, selectedCount: 900, selectedBytes: 600 * 1024 * 1024, writtenCount: 0, failureReason: null, current: false });
      return json({}, 404);
    });
    render(<DownloadAllDocuments orgSlug="acme" />);
    fireEvent.click(screen.getByRole('button', { name: /Download all documents/ }));
    expect(await screen.findByText(/too much for this browser to save in one piece/)).toBeTruthy();
    expect(calls.some((c) => c.includes('/file'))).toBe(false);
  });
});

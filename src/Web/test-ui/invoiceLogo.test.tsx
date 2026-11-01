import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { InvoiceLogoSection, type InvoiceLogoMetadata } from '../src/components/InvoiceLogoSection';

const { authFetch, completeStepUp } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  completeStepUp: vi.fn(),
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, completeStepUp }),
}));

const mockRevoke = vi.fn();
const mockCreate = vi.fn((_blob: Blob | File) => 'blob:mock-url-' + Math.random().toString(36).slice(2));

beforeEach(() => {
  global.URL.createObjectURL = mockCreate;
  global.URL.revokeObjectURL = mockRevoke;
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

const defaultMetadata: InvoiceLogoMetadata = {
  hasLogo: false,
  revision: 0,
  hash: null,
  width: null,
  height: null,
  byteCount: null,
  limits: {
    maxUploadBytes: 2 * 1024 * 1024,
    maxStoredBytes: 512 * 1024,
    maxDimensionPixels: 4096,
    maxTotalPixels: 4000000,
    maxNormalizedDimension: 1024,
  },
};

describe('InvoiceLogoSection', () => {
  it('renders initial empty state with file upload input', async () => {
    authFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => defaultMetadata,
    });

    render(<InvoiceLogoSection canManage={true} />);

    expect(screen.getByText('Company logo')).toBeTruthy();
    expect(screen.getByText('No logo')).toBeTruthy();
    await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/invoice-branding/logo'));
  });

  it('renders existing logo image via temporary blob URL', async () => {
    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/invoice-branding/logo') {
        return {
          ok: true,
          json: async () => ({ ...defaultMetadata, hasLogo: true, revision: 1, hash: 'h1' }),
        };
      }
      if (url === '/api/invoice-branding/logo/image') {
        return {
          ok: true,
          blob: async () => new Blob(['fake-image-bytes'], { type: 'image/png' }),
        };
      }
      return { ok: false };
    });

    render(<InvoiceLogoSection canManage={true} />);

    await waitFor(() => {
      const img = screen.getByAltText('Invoice logo preview') as HTMLImageElement;
      expect(img).toBeTruthy();
      expect(img.src).toContain('blob:mock-url');
    });

    expect(screen.getByRole('button', { name: /Remove logo/ })).toBeTruthy();
  });

  it('rejects client-side files over 2 MB', async () => {
    authFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => defaultMetadata,
    });

    render(<InvoiceLogoSection canManage={true} />);
    await waitFor(() => expect(authFetch).toHaveBeenCalled());

    const largeFile = new File([new ArrayBuffer(2 * 1024 * 1024 + 10)], 'large.png', { type: 'image/png' });
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;

    fireEvent.change(input, { target: { files: [largeFile] } });

    expect(screen.getByRole('alert').textContent).toContain('exceeds the 2 MB limit');
    expect(screen.queryByRole('button', { name: /Upload logo/ })).toBeNull();
  });

  it('rejects client-side files with unsupported types', async () => {
    authFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => defaultMetadata,
    });

    render(<InvoiceLogoSection canManage={true} />);
    await waitFor(() => expect(authFetch).toHaveBeenCalled());

    const gifFile = new File(['gif'], 'test.gif', { type: 'image/gif' });
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;

    fireEvent.change(input, { target: { files: [gifFile] } });

    expect(screen.getByRole('alert').textContent).toContain('Only PNG and JPEG images are allowed');
  });

  it('shows local preview on valid selection and uploads with FormData', async () => {
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/invoice-branding/logo' && init?.method === 'PUT') {
        return {
          ok: true,
          json: async () => ({ ...defaultMetadata, hasLogo: true, revision: 1 }),
        };
      }
      if (url === '/api/invoice-branding/logo') {
        return {
          ok: true,
          json: async () => defaultMetadata,
        };
      }
      return { ok: false };
    });

    render(<InvoiceLogoSection canManage={true} />);
    await waitFor(() => expect(authFetch).toHaveBeenCalled());

    const validFile = new File(['valid-png'], 'logo.png', { type: 'image/png' });
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;

    fireEvent.change(input, { target: { files: [validFile] } });

    expect(screen.getByText(/Local preview/)).toBeTruthy();
    const uploadBtn = screen.getByRole('button', { name: /Upload logo/ });
    expect(uploadBtn).toBeTruthy();

    fireEvent.click(uploadBtn);

    await waitFor(() => {
      expect(authFetch).toHaveBeenCalledWith(
        '/api/invoice-branding/logo',
        expect.objectContaining({
          method: 'PUT',
          body: expect.any(FormData),
        })
      );
    });
  });

  it('removes logo on explicit button click', async () => {
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.startsWith('/api/invoice-branding/logo?revision=1') && init?.method === 'DELETE') {
        return {
          ok: true,
          json: async () => ({ ...defaultMetadata, hasLogo: false, revision: 2 }),
        };
      }
      if (url === '/api/invoice-branding/logo') {
        return {
          ok: true,
          json: async () => ({ ...defaultMetadata, hasLogo: true, revision: 1 }),
        };
      }
      if (url === '/api/invoice-branding/logo/image') {
        return {
          ok: true,
          blob: async () => new Blob(['image'], { type: 'image/png' }),
        };
      }
      return { ok: false };
    });

    render(<InvoiceLogoSection canManage={true} />);
    await waitFor(() => expect(screen.getByRole('button', { name: /Remove logo/ })).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: /Remove logo/ }));

    await waitFor(() => {
      expect(authFetch).toHaveBeenCalledWith(
        '/api/invoice-branding/logo?revision=1',
        expect.objectContaining({ method: 'DELETE' })
      );
    });
  });

  it('renders read-only presentation when canManage is false', async () => {
    authFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ ...defaultMetadata, hasLogo: true, revision: 1 }),
    });
    authFetch.mockResolvedValueOnce({
      ok: true,
      blob: async () => new Blob(['image'], { type: 'image/png' }),
    });

    render(<InvoiceLogoSection canManage={false} />);

    await waitFor(() => expect(authFetch).toHaveBeenCalled());
    expect(screen.getByText(/Only organization owners and admins can upload or remove/)).toBeTruthy();
    expect(document.querySelector('input[type="file"]')).toBeNull();
    expect(screen.queryByRole('button', { name: /Remove logo/ })).toBeNull();
  });
});

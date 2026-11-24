// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { act, cleanup, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CaptureAiMappingSettingsTab } from '../src/components/CaptureAiMappingSettingsTab';
import {
  CaptureAiMappingSettings,
  isCaptureAiMappingVisible,
  useCaptureAiMappingSettings,
} from '../src/lib/captureAiMapping';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

afterEach(() => {
  cleanup();
  authFetch.mockReset();
});

const base: CaptureAiMappingSettings = {
  available: true,
  unavailableReason: null,
  enabled: false,
  consentedAt: null,
  consentedBy: null,
  consentVersion: '2026-11-24',
  needsReconsent: false,
  canManage: true,
};

const json = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });

describe('AI field reading setting', () => {
  it('asks for consent with the published wording before turning on, and returns focus to the toggle on cancel', async () => {
    const update = vi.fn(async () => ({ ok: true as const }));
    render(<CaptureAiMappingSettingsTab settings={base} update={update} />);
    const toggle = screen.getByRole('switch');
    expect(toggle.getAttribute('aria-checked')).toBe('false');

    fireEvent.click(toggle);

    const dialog = await screen.findByRole('dialog');
    expect(dialog.textContent).toContain('Text from your scanned documents is sent to AccountGo Assistant to improve field reading.');
    expect(dialog.textContent).toContain('Processed by Anter, our AI service provider.');
    expect(dialog.textContent).toContain('Bank-detail documents and your client and vendor list are never sent.');
    expect(screen.getByRole('link', { name: 'Read the AI disclosure' }).getAttribute('href')).toBe('https://www.accountgo.dev/legal/ai-disclosure/');
    expect(update).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(document.activeElement).toBe(toggle));
    expect(update).not.toHaveBeenCalled();
  });

  it('Escape cancels the dialog without saving', async () => {
    const update = vi.fn(async () => ({ ok: true as const }));
    render(<CaptureAiMappingSettingsTab settings={base} update={update} />);
    fireEvent.click(screen.getByRole('switch'));
    await screen.findByRole('dialog');

    fireEvent.keyDown(document, { key: 'Escape' });

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(update).not.toHaveBeenCalled();
  });

  it('turns on only after the owner confirms, then closes the dialog', async () => {
    const update = vi.fn(async () => ({ ok: true as const }));
    render(<CaptureAiMappingSettingsTab settings={base} update={update} />);
    fireEvent.click(screen.getByRole('switch'));
    await screen.findByRole('dialog');

    fireEvent.click(screen.getByRole('button', { name: 'Turn on' }));

    await waitFor(() => expect(update).toHaveBeenCalledWith(true));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('keeps the dialog open and shows the reason when saving fails', async () => {
    const update = vi.fn(async () => ({ ok: false as const, message: 'AI field reading is not available for this workspace.' }));
    render(<CaptureAiMappingSettingsTab settings={base} update={update} />);
    fireEvent.click(screen.getByRole('switch'));
    await screen.findByRole('dialog');

    fireEvent.click(screen.getByRole('button', { name: 'Turn on' }));

    expect((await screen.findByRole('alert')).textContent).toContain('not available');
    expect(screen.getByRole('dialog')).toBeTruthy();
  });

  it('turning it off is one click with no dialog', async () => {
    const update = vi.fn(async () => ({ ok: true as const }));
    render(<CaptureAiMappingSettingsTab settings={{ ...base, enabled: true, consentedAt: '2026-11-24T01:00:00Z', consentedBy: 'owner@example.com' }} update={update} />);
    expect(screen.getByText(/by owner@example.com/)).toBeTruthy();

    fireEvent.click(screen.getByRole('switch'));

    await waitFor(() => expect(update).toHaveBeenCalledWith(false));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('shows members the state read-only', () => {
    render(<CaptureAiMappingSettingsTab settings={{ ...base, enabled: true, canManage: false }} update={vi.fn()} />);

    const toggle = screen.getByRole('switch') as HTMLButtonElement;
    expect(toggle.disabled).toBe(true);
    expect(toggle.getAttribute('aria-checked')).toBe('true');
    expect(screen.getByText(/Only the workspace owner can change this/)).toBeTruthy();
  });

  it('explains a workspace that support turned off instead of offering a toggle that does nothing', () => {
    render(<CaptureAiMappingSettingsTab settings={{ ...base, available: false, unavailableReason: 'turned_off_by_support' }} update={vi.fn()} />);

    expect((screen.getByRole('switch') as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole('status').textContent).toContain('turned off for this workspace by AccountGo support');
  });

  it('asks the owner to agree again when the wording changed', () => {
    render(<CaptureAiMappingSettingsTab settings={{ ...base, needsReconsent: true }} update={vi.fn()} />);

    expect(screen.getByRole('status').textContent).toContain('wording changed');
  });

  it('does not tell a non-owner to turn it on again, because they cannot', () => {
    render(<CaptureAiMappingSettingsTab settings={{ ...base, canManage: false, needsReconsent: true }} update={vi.fn()} />);

    expect(screen.queryByText(/Turn it on again/)).toBeNull();
    expect(screen.getByRole('status').textContent).toContain('Only the workspace owner can change this');
  });

  it.each([
    [{ available: true, unavailableReason: null }, true],
    [{ available: false, unavailableReason: 'turned_off_by_support' }, true],
    [{ available: false, unavailableReason: 'cap_not_set' }, false],
    [{ available: false, unavailableReason: 'plan' }, false],
    [{ available: false, unavailableReason: 'not_offered' }, false],
  ] as const)('visibility of the Documents tab for %j is %s', (partial, visible) => {
    expect(isCaptureAiMappingVisible({ ...base, ...partial } as CaptureAiMappingSettings)).toBe(visible);
  });

  it('stays hidden until the setting has loaded, and when loading fails', async () => {
    expect(isCaptureAiMappingVisible(null)).toBe(false);
    authFetch.mockResolvedValue(json({}, 500));

    const { result } = renderHook(() => useCaptureAiMappingSettings(true, 'ws-1'));

    await waitFor(() => expect(authFetch).toHaveBeenCalled());
    expect(result.current.settings).toBeNull();
  });

  it('loads the setting and saves a change through the API', async () => {
    authFetch.mockImplementation(async (_url: string, init?: { method?: string }) =>
      init?.method === 'PUT' ? json({ ...base, enabled: true }) : json(base));

    const { result } = renderHook(() => useCaptureAiMappingSettings(true, 'ws-1'));
    await waitFor(() => expect(result.current.settings?.available).toBe(true));

    await act(async () => {
      expect(await result.current.update(true)).toEqual({ ok: true });
    });

    expect(result.current.settings?.enabled).toBe(true);
    const put = authFetch.mock.calls.find((call) => call[1]?.method === 'PUT');
    expect(put?.[0]).toBe('/api/document-captures/ai-mapping/settings');
    expect(JSON.parse(put?.[1].body)).toEqual({ enabled: true });
  });

  it('does not call the API while the hook is disabled', () => {
    renderHook(() => useCaptureAiMappingSettings(false, 'ws-1'));

    expect(authFetch).not.toHaveBeenCalled();
  });
});

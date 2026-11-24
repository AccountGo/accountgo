// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';

export type CaptureAiMappingUnavailableReason = 'not_offered' | 'plan' | 'turned_off_by_support' | 'cap_not_set';

export interface CaptureAiMappingSettings {
  available: boolean;
  unavailableReason: CaptureAiMappingUnavailableReason | null;
  enabled: boolean;
  consentedAt: string | null;
  consentedBy: string | null;
  consentVersion: string;
  needsReconsent: boolean;
  canManage: boolean;
}

export const AI_DISCLOSURE_URL = 'https://www.accountgo.dev/legal/ai-disclosure/';
export const AI_MAPPING_CONSENT_TEXT = 'Text from your scanned documents is sent to AccountGo Assistant to improve field reading.';

/**
 * The setting is shown when the host offers it, and also when support turned it off for this workspace, so the owner sees why instead of
 * a toggle that does nothing. Not offered, not on the plan, or the allowance not set up (an operator state, not a workspace one): hidden.
 */
export function isCaptureAiMappingVisible(settings: CaptureAiMappingSettings | null): boolean {
  if (!settings) return false;
  if (settings.available) return true;
  return settings.unavailableReason === 'turned_off_by_support';
}

export function describeUnavailableReason(reason: CaptureAiMappingUnavailableReason | null): string {
  switch (reason) {
    case 'turned_off_by_support':
      return 'AI field reading has been turned off for this workspace by AccountGo support.';
    case 'cap_not_set':
      return 'AI field reading is not available right now.';
    case 'plan':
      return 'AI field reading is not included in this workspace’s plan.';
    default:
      return 'AI field reading is not offered here.';
  }
}

/** Loads the workspace's AI field-mapping setting. A failed load reads as "not shown": nothing guesses availability. */
export function useCaptureAiMappingSettings(enabled: boolean, workspaceKey: string | undefined) {
  const { authFetch } = useAuth();
  const [settings, setSettings] = useState<CaptureAiMappingSettings | null>(null);

  useEffect(() => {
    let cancelled = false;
    setSettings(null);
    if (!enabled) return undefined;
    void (async () => {
      try {
        const res = await authFetch('/api/document-captures/ai-mapping/settings');
        if (!res.ok) return;
        const body = (await res.json()) as CaptureAiMappingSettings;
        if (!cancelled) setSettings(body);
      } catch {
        // Hidden on failure: the setting is optional and never blocks Settings.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, enabled, workspaceKey]);

  const update = useCallback(
    async (turnOn: boolean): Promise<{ ok: true } | { ok: false; message: string }> => {
      try {
        const res = await authFetch('/api/document-captures/ai-mapping/settings', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ enabled: turnOn }),
        });
        if (res.ok) {
          setSettings((await res.json()) as CaptureAiMappingSettings);
          return { ok: true };
        }
        if (res.status === 403) return { ok: false, message: 'Only the workspace owner can change this.' };
        if (res.status === 409) return { ok: false, message: 'AI field reading is not available for this workspace.' };
        return { ok: false, message: 'The setting could not be saved. Try again.' };
      } catch {
        return { ok: false, message: 'The setting could not be saved. Try again.' };
      }
    },
    [authFetch],
  );

  return { settings, update };
}

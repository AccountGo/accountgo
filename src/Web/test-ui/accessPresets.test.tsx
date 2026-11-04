import { expect, it } from 'vitest';
import {
  CUSTOM_PRESET_KEY,
  buildAccessPresets,
  describePermissions,
  matchPresetKey,
} from '../src/lib/accessPresets';
import type { RoleItem } from '../src/types';

function role(id: number, name: string, permissions: string[]): RoleItem {
  return { id, name, displayName: name, permissions: permissions as unknown as RoleItem['permissions'] };
}

const roles: RoleItem[] = [
  role(1, 'WorkspaceFullAccess.v1', ['financials.journal.post']),
  role(2, 'Accountant', ['financials.journal.post', 'reports.view']),
  role(3, 'Sales Representative', ['sales.invoices.create']),
  role(4, 'Purchasing Agent', ['purchases.bills.create']),
  role(5, 'Auditor / Viewer', ['reports.view', 'audit.logs.view']),
];

it('keeps the Full workspace access preset when the seeded managed profile name is used', () => {
  const presets = buildAccessPresets(roles, () => true);
  expect(presets.find(preset => preset.key === 'full')?.roleIds).toEqual([1]);
});

it('summarises the strongest level per area', () => {
  expect(describePermissions(['sales.invoices.view', 'sales.invoices.create', 'financials.journal.post', 'reports.view']))
    .toEqual([
      { area: 'financials', label: 'Accounting', level: 'Manage and post' },
      { area: 'reports', label: 'Reports', level: 'View only' },
      { area: 'sales', label: 'Sales', level: 'Create and edit' },
    ]);
});

it('builds presets only from seeded roles that exist and never invents a grant', () => {
  const presets = buildAccessPresets(roles.filter(item => item.name !== 'Auditor / Viewer'), () => true);
  expect(presets.map(preset => preset.key)).toEqual(['full', 'bookkeeper']);
  expect(presets.find(preset => preset.key === 'bookkeeper')?.roleIds).toEqual([2, 3, 4]);
});

it('marks a preset unavailable when none of its areas are in the plan, and limited when only some are', () => {
  const noReports = buildAccessPresets(roles, module => module !== 'reports' && module !== 'audit');
  expect(noReports.find(preset => preset.key === 'readOnlyAccountant')?.unavailable).toBe(true);
  expect(noReports.find(preset => preset.key === 'bookkeeper')?.limitedByPlan).toBe(true);
  const everything = buildAccessPresets(roles, () => true);
  expect(everything.every(preset => !preset.unavailable && !preset.limitedByPlan)).toBe(true);
});

it('matches the selection to a preset, custom, or nothing', () => {
  const presets = buildAccessPresets(roles, () => true);
  expect(matchPresetKey(presets, [])).toBe('');
  expect(matchPresetKey(presets, [4, 3, 2])).toBe('bookkeeper');
  expect(matchPresetKey(presets, [2])).toBe(CUSTOM_PRESET_KEY);
});

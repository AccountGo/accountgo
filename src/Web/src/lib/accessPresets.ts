import type { ModuleKey, RoleItem } from '../types';

/** Seeded managed profile name — must match WorkspaceAccessProfileKeys.FullWorkspaceAccessV1. */
export const FULL_WORKSPACE_ACCESS_ROLE_NAME = 'WorkspaceFullAccess.v1';
export const CUSTOM_PRESET_KEY = 'custom';

export type AccessPresetKey = 'full' | 'bookkeeper' | 'readOnlyAccountant';

export interface AccessPresetDefinition {
  key: AccessPresetKey;
  label: string;
  description: string;
  /** Seeded security role names combined by this preset (their permissions are unioned). */
  roleNames: string[];
}

export interface AccessPreset extends AccessPresetDefinition {
  roleIds: number[];
  /** True when every area granted by this preset is outside the workspace's enabled modules. */
  unavailable: boolean;
  /** True when part of the preset is outside the workspace's enabled modules and will have no effect. */
  limitedByPlan: boolean;
}

export const ACCESS_PRESET_DEFINITIONS: AccessPresetDefinition[] = [
  {
    key: 'full',
    label: 'Full workspace access',
    description: 'Every functional permission the workspace plan includes. Needs independent approval from an Owner.',
    roleNames: [FULL_WORKSPACE_ACCESS_ROLE_NAME],
  },
  {
    key: 'bookkeeper',
    label: 'Bookkeeper',
    description: 'Day-to-day bookkeeping: post journals, invoices and bills, manage customers, vendors and banking.',
    roleNames: ['Accountant', 'Sales Representative', 'Purchasing Agent'],
  },
  {
    key: 'readOnlyAccountant',
    label: 'Read-only accountant',
    description: 'View ledgers, invoices, bills, banking, reports and the audit log without changing anything.',
    roleNames: ['Auditor / Viewer'],
  },
];

/** Permission prefix → the entitlement module that gates it. Prefixes without a module are always available. */
const PERMISSION_PREFIX_MODULE: Record<string, ModuleKey | undefined> = {
  sales: 'sales',
  financials: 'financials',
  banking: 'financials',
  purchases: 'purchases',
  inventory: 'inventory',
  reports: 'reports',
  donations: 'donations',
  audit: 'audit',
};

const AREA_LABELS: Record<string, string> = {
  sales: 'Sales',
  financials: 'Accounting',
  banking: 'Banking',
  purchases: 'Purchases',
  inventory: 'Inventory',
  reports: 'Reports',
  donations: 'Donations',
  audit: 'Audit log',
  admin: 'Administration',
};

export type AccessLevel = 'View only' | 'Create and edit' | 'Manage and post';
const MANAGE_ACTIONS = ['manage', 'post', 'close', 'reconcile', 'undo'];
const CREATE_ACTION = 'create';

export interface AccessAreaSummary {
  area: string;
  label: string;
  level: AccessLevel;
}

/** Plain-language area/level summary of a permission list. Mirrors the server-side invitee preview summary. */
export function describePermissions(permissionNames: string[]): AccessAreaSummary[] {
  const strongestByArea = new Map<string, AccessLevel>();
  for (const name of permissionNames) {
    if (!name.trim()) continue;
    const segments = name.split('.');
    const area = segments[0].toLowerCase();
    const action = segments[segments.length - 1].toLowerCase();
    const current = strongestByArea.get(area) ?? 'View only';
    let next: AccessLevel = current;
    if (MANAGE_ACTIONS.includes(action)) next = 'Manage and post';
    else if (action === CREATE_ACTION && current !== 'Manage and post') next = 'Create and edit';
    strongestByArea.set(area, next);
  }
  return [...strongestByArea.entries()]
    .map(([area, level]) => ({ area, label: AREA_LABELS[area] ?? area, level }))
    .sort((left, right) => left.label.localeCompare(right.label));
}

export function permissionsForRoles(roles: RoleItem[], roleIds: number[]): string[] {
  const selected = new Set(roleIds);
  const names = new Set<string>();
  for (const role of roles) {
    if (!selected.has(role.id)) continue;
    for (const permission of role.permissions ?? []) {
      names.add(typeof permission === 'string' ? permission : permission.name);
    }
  }
  return [...names];
}

function moduleEnabled(prefix: string, isModuleEnabled: (module: ModuleKey) => boolean): boolean {
  const module = PERMISSION_PREFIX_MODULE[prefix];
  return module === undefined || isModuleEnabled(module);
}

/**
 * Presets whose seeded roles exist in this workspace, annotated by what the plan actually enables.
 * A preset never grants more than its roles; entitlements only decide whether it is useful to offer.
 */
export function buildAccessPresets(
  roles: RoleItem[],
  isModuleEnabled: (module: ModuleKey) => boolean,
): AccessPreset[] {
  const rolesByName = new Map(roles.map(role => [role.name, role]));
  const presets: AccessPreset[] = [];
  for (const definition of ACCESS_PRESET_DEFINITIONS) {
    const matched = definition.roleNames
      .map(name => rolesByName.get(name))
      .filter((role): role is RoleItem => role !== undefined);
    if (matched.length === 0) continue;
    const roleIds = matched.map(role => role.id);
    const prefixes = new Set(permissionsForRoles(roles, roleIds).map(name => name.split('.')[0].toLowerCase()));
    // The managed Full profile carries no per-permission list the client can inspect; it is plan-scoped server-side.
    const inspectable = prefixes.size > 0;
    const enabled = [...prefixes].filter(prefix => moduleEnabled(prefix, isModuleEnabled));
    presets.push({
      ...definition,
      roleIds,
      unavailable: inspectable && enabled.length === 0,
      limitedByPlan: inspectable && enabled.length > 0 && enabled.length < prefixes.size,
    });
  }
  return presets;
}

/** The preset whose role set exactly matches the selection, or the custom key. */
export function matchPresetKey(presets: AccessPreset[], selectedRoleIds: number[]): AccessPresetKey | typeof CUSTOM_PRESET_KEY | '' {
  if (selectedRoleIds.length === 0) return '';
  const selected = [...selectedRoleIds].sort((a, b) => a - b).join(',');
  const match = presets.find(preset => [...preset.roleIds].sort((a, b) => a - b).join(',') === selected);
  return match ? match.key : CUSTOM_PRESET_KEY;
}

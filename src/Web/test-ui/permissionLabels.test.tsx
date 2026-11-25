import { describe, expect, it } from 'vitest';
import { areaForPermission, PERMISSION_AREA_LABELS, permissionResource } from '../src/lib/permissionLabels';
import { serverPermissions } from './serverPermissions';

describe('permission area labels', () => {
  it('reads a sensible number of permissions from the server list', () => {
    expect(serverPermissions().length).toBeGreaterThan(40);
  });

  it('names an area for every permission the server defines', () => {
    const missing = serverPermissions().filter((p) => !PERMISSION_AREA_LABELS[permissionResource(p)]);
    expect(missing, `add an area label in permissionLabels.ts for: ${missing.join(', ')}`).toEqual([]);
  });

  it('has no label for a resource the server no longer defines', () => {
    const resources = new Set(serverPermissions().map(permissionResource));
    const stale = Object.keys(PERMISSION_AREA_LABELS).filter((resource) => !resources.has(resource));
    expect(stale, `remove from permissionLabels.ts: ${stale.join(', ')}`).toEqual([]);
  });

  it('never returns a raw permission key as an area name', () => {
    for (const label of Object.values(PERMISSION_AREA_LABELS)) expect(label).not.toMatch(/^[a-z_]+\.[a-z_.]+$/);
  });

  it('maps a permission to its area and gives null for the unknown', () => {
    expect(areaForPermission('purchases.vendors.view')).toBe('Vendors');
    expect(areaForPermission('sales.customers.manage')).toBe('Clients');
    expect(areaForPermission('banking.reconcile.undo')).toBe('Bank reconciliation');
    expect(areaForPermission('reports.view')).toBe('Reports');
    expect(areaForPermission('something.new.view')).toBeNull();
    expect(areaForPermission(undefined)).toBeNull();
  });
});

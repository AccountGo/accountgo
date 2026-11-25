import React from 'react';
import { describe, expect, it } from 'vitest';
import type { RouteObject } from 'react-router-dom';
import { appRoutes, orgRoutes } from '../src/App';
import { RouteGuard } from '../src/components/RouteGuard';
import { accessKeys, isPublicAccess, publicInWorkspace, requires, requiresAny, type RouteAccess } from '../src/lib/routeAccess';
import { areaForPermission } from '../src/lib/permissionLabels';
import { serverPermissions } from './serverPermissions';

// The rules every organization route must meet. Each rule has a case in the negative fixture below that proves it fires.
function containsRouteGuard(node: React.ReactNode): boolean {
  if (!React.isValidElement(node)) return Array.isArray(node) && node.some(containsRouteGuard);
  if (node.type === RouteGuard) return true;
  return containsRouteGuard((node.props as { children?: React.ReactNode }).children);
}

export function checkOrgRoutes(routes: RouteObject[], knownPermissions: readonly string[]): string[] {
  const problems: string[] = [];
  for (const route of routes) {
    const name = `'${route.path ?? '(index)'}'`;
    if (route.path === '') continue; // the bare path redirects to the dashboard
    const access = (route.handle as { access?: RouteAccess } | undefined)?.access;
    if (!access) {
      problems.push(`${name} has no access declaration (use requires(), requiresAny() or publicInWorkspace() in handle)`);
      continue;
    }
    const declared = ['permission', 'anyOf', 'publicInWorkspace'].filter((key) => key in access);
    if (declared.length !== 1) problems.push(`${name} must declare exactly one of permission, anyOf or publicInWorkspace`);
    if (isPublicAccess(access)) {
      if (!access.publicInWorkspace.trim()) problems.push(`${name} is publicInWorkspace without a reason`);
      continue;
    }
    const keys = accessKeys(access);
    if (keys.length === 0) problems.push(`${name} declares no permission`);
    for (const key of keys) {
      if (!knownPermissions.includes(key)) problems.push(`${name} declares '${key}', which the server does not define`);
      if (!areaForPermission(key)) problems.push(`${name} declares '${key}', which has no area label in permissionLabels.ts`);
    }
    if (!containsRouteGuard(route.element)) problems.push(`${name} declares a permission but its element does not contain <RouteGuard>`);
  }
  return problems;
}

function orgRouteFromApp(routes: RouteObject[]): RouteObject | undefined {
  for (const route of routes) {
    if (route.path === '/org/:orgSlug') return route;
    const inner = route.children && orgRouteFromApp(route.children);
    if (inner) return inner;
  }
  return undefined;
}

describe('organization route permissions', () => {
  it('the app mounts exactly the declared orgRoutes under /org/:orgSlug, so no route can be added outside them', () => {
    const org = orgRouteFromApp(appRoutes);
    expect(org, 'the /org/:orgSlug route was not found in appRoutes').toBeTruthy();
    expect(org?.children).toBe(orgRoutes);
  });

  it('every org route declares a permission or publicInWorkspace with a reason, names real permissions, and is guarded', () => {
    expect(checkOrgRoutes(orgRoutes, serverPermissions())).toEqual([]);
  });

  it('covers a sensible number of routes and keeps the dashboard public so the way back never loops', () => {
    expect(orgRoutes.length).toBeGreaterThan(30);
    const dashboard = orgRoutes.find((route) => route.path === 'dashboard');
    expect(isPublicAccess((dashboard?.handle as { access: RouteAccess }).access)).toBe(true);
    expect(containsRouteGuard(dashboard?.element)).toBe(false);
  });

  it('uses the create key for new pages, and for edit pages because the server has no separate edit permission', () => {
    const keysOf = (path: string) => accessKeys((orgRoutes.find((route) => route.path === path)?.handle as { access: RouteAccess }).access);
    expect(serverPermissions().some((key) => key.endsWith('.edit'))).toBe(false);
    expect(keysOf('sales/new')).toEqual(['sales.invoices.create']);
    expect(keysOf('sales/:invoiceId/edit')).toEqual(['sales.invoices.create']);
    expect(keysOf('purchases/new')).toEqual(['purchases.bills.create']);
    expect(keysOf('purchases/:billId/edit')).toEqual(['purchases.bills.create']);
    expect(keysOf('sales/recurring/:scheduleId/edit')).toEqual(['sales.invoices.create']);
    expect(keysOf('financials/year-end')).toEqual(['financials.accounts.view', 'financials.periods.close']);
  });

  describe('the checker itself (negative fixture)', () => {
    const page = <div />;
    const known = ['purchases.bills.view', 'sales.customers.view'];
    const good: RouteObject = { path: 'bills', handle: requires('purchases.bills.view', 'Bills'), element: <RouteGuard>{page}</RouteGuard> };

    it('accepts a well-formed set', () => {
      expect(checkOrgRoutes([good, { path: 'me', handle: publicInWorkspace('own account'), element: page }], known)).toEqual([]);
    });

    it('catches a route with no declaration', () => {
      expect(checkOrgRoutes([{ path: 'oops', element: page }], known)[0]).toContain("'oops' has no access declaration");
    });

    it('catches an unknown permission key', () => {
      expect(checkOrgRoutes([{ ...good, path: 'x', handle: requires('purchases.bils.view') }], known).join('\n')).toContain('does not define');
    });

    it('catches an empty publicInWorkspace reason', () => {
      expect(checkOrgRoutes([{ path: 'p', handle: publicInWorkspace('  '), element: page }], known)[0]).toContain('without a reason');
    });

    it('catches a declared permission whose element is not inside RouteGuard', () => {
      expect(checkOrgRoutes([{ ...good, path: 'y', element: page }], known)[0]).toContain('does not contain <RouteGuard>');
    });

    it('catches a declaration with both a permission and public', () => {
      const both = { access: { permission: 'purchases.bills.view', publicInWorkspace: 'x' } } as unknown as RouteObject['handle'];
      expect(checkOrgRoutes([{ ...good, path: 'z', handle: both }], known)[0]).toContain('exactly one');
    });

    it('finds the guard through module gates', () => {
      const gated = <section><RouteGuard>{page}</RouteGuard></section>;
      expect(containsRouteGuard(gated)).toBe(true);
      expect(containsRouteGuard(page)).toBe(false);
    });

    it('anyOf declarations are checked key by key', () => {
      expect(checkOrgRoutes([{ ...good, path: 'a', handle: requiresAny(['purchases.bills.view', 'nope.nope']) }], known).join('\n')).toContain("'nope.nope'");
    });
  });
});

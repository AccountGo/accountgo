import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { Sidebar } = await server.ssrLoadModule('/src/components/Sidebar.tsx');
const { MobileBottomNav } = await server.ssrLoadModule('/src/components/MobileBottomNav.tsx');
const { OrgSwitcher } = await server.ssrLoadModule('/src/components/OrgSwitcher.tsx');
const { ThemeProvider } = await server.ssrLoadModule('/src/themes/ThemeContext.tsx');
const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');

const personalOrg = {
  id: '1',
  name: 'Marv Personal Books Inc.',
  shortName: 'PERSONAL',
  slug: 'personal',
  plan: 'free',
  currency: 'USD',
  role: 'Owner',
  operatingPolicy: { purpose: 'Personal' },
  whitelabel: { brandName: 'Marv Personal Books Inc.' },
};

const user = {
  id: 'u1',
  name: 'Marv Perez',
  email: 'marv@example.com',
  role: 'Owner',
};

test('Sidebar shows Dashboard link permanently even when financials module is absent', () => {
  // Simulate an invoicing or personal workspace with NO financials module
  const hasModule = (mod) => mod === 'sales';
  const html = renderToStaticMarkup(
    createElement(AuthProvider, null,
      createElement(ThemeProvider, null,
        createElement(MemoryRouter, { initialEntries: ['/org/personal/sales'] },
          createElement(Sidebar, {
            currentOrg: personalOrg,
            availableOrgs: [personalOrg],
            user,
            hasModule,
            hasPermission: () => true,
          })
        )
      )
    )
  );

  assert.ok(html.includes('/org/personal/dashboard'), 'Sidebar should have link to dashboard');
  assert.ok(html.includes('Dashboard'), 'Sidebar should display Dashboard text');
});

test('MobileBottomNav shows Dashboard item permanently without financials module restriction', () => {
  const hasModule = (mod) => mod === 'sales';
  const html = renderToStaticMarkup(
    createElement(MemoryRouter, { initialEntries: ['/org/personal/sales'] },
      createElement(MobileBottomNav, {
        currentOrg: personalOrg,
        onOpenMenu: () => {},
        hasPermission: () => true,
        hasModule,
      })
    )
  );

  assert.ok(html.includes('/org/personal/dashboard'), 'Mobile bottom nav should include dashboard link');
  assert.ok(html.includes('Dashboard'), 'Mobile bottom nav should display Dashboard item');
});

test('OrgSwitcher displays Short Name / Trading Acronym when present, falling back to full name', () => {
  const htmlWithShortName = renderToStaticMarkup(
    createElement(MemoryRouter, { initialEntries: ['/org/personal/dashboard'] },
      createElement(OrgSwitcher, {
        currentOrg: personalOrg,
        availableOrgs: [personalOrg],
      })
    )
  );

  assert.ok(htmlWithShortName.includes('PERSONAL'), 'OrgSwitcher should show short name PERSONAL');

  const orgWithoutShortName = {
    id: '2',
    name: 'Consulting Group Worldwide LLC',
    slug: 'consulting',
  };

  const htmlWithoutShortName = renderToStaticMarkup(
    createElement(MemoryRouter, { initialEntries: ['/org/consulting/dashboard'] },
      createElement(OrgSwitcher, {
        currentOrg: orgWithoutShortName,
        availableOrgs: [orgWithoutShortName],
      })
    )
  );

  assert.ok(htmlWithoutShortName.includes('Consulting Group Worldwide LLC'), 'OrgSwitcher should fallback to full name');
});

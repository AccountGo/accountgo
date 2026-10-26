# AccountGo — Multi-Tenant Web Frontend (`Web`)

Modern, enterprise-grade multi-tenant frontend architecture built with **Vite 8**, **React 19**, **TypeScript**, **Tailwind CSS v4**, and **React Router v7**.

---

## 🏛️ Architecture: Multi-Tenant Root Routing

In this architecture, every organization-scoped view embeds an `:orgSlug` parameter directly into the URL path (`/org/:orgSlug/*`). This provides:
1. **Isolated Workspace State:** All queries, journals, chart of accounts, and permissions are strictly keyed by tenant.
2. **Deep-Linking & Direct Sharing:** Refreshing the browser or sending a URL like `http://localhost:3000/org/ph-financials/dashboard` directly loads that exact organization.
3. **Multi-Organization Switching:** Users can seamlessly toggle between client workspaces without losing their active sub-path (e.g., jumping from `/org/acme-corp/settings` directly to `/org/ph-financials/settings`).
4. **Fallback & Discovery:** Visiting `/` or `/select-org` presents the Organization Hub to pick or create a tenant.

---

## 📁 Directory Structure

```text
src/Web/
├── public/
│   ├── _redirects              # SPA fallback for Netlify / Cloudflare Pages
│   ├── accountgo-mark.svg      # Vector monogram brand mark
│   ├── accountgo-logo-dark.svg # Full brand lockup
│   ├── favicon.svg             # Modern vector favicon
│   └── favicon.ico             # Standard Windows icon
├── src/
│   ├── components/
│   │   ├── Header.tsx          # Dynamic breadcrumbs, tenant badges, quick search, user profile
│   │   ├── Sidebar.tsx         # Collapsible responsive sidebar with active tenant navigation
│   │   └── OrgSwitcher.tsx     # Enterprise popover dropdown to switch active organization
│   ├── layouts/
│   │   └── OrgLayout.tsx       # Root layout: captures :orgSlug, validates tenant, injects Outlet context
│   ├── pages/
│   │   ├── SelectOrg.tsx       # Root workspace selection
│   │   ├── CreateOrg/          # /create-org setup wizard (steps/, hooks/, lib/); resumes from a localStorage draft
│   │   ├── Dashboard.tsx       # /org/:orgSlug/dashboard with real-time financial metrics skeleton
│   │   ├── Settings.tsx        # /org/:orgSlug/settings with tenant profile, currency, fiscal year, danger zone
│   │   ├── GeneralLedger.tsx   # /org/:orgSlug/financials posted journal register
│   │   ├── JournalEntries.tsx  # /org/:orgSlug/financials/journals manual drafts / post
│   │   ├── Reports.tsx         # /org/:orgSlug/reports TB / P&L / Balance Sheet
│   │   └── NotFound.tsx        # Polished 404 handler with return links
│   ├── lib/
│   │   └── storage.ts          # Tenant resolution and localStorage persistence
│   ├── types/
│   │   └── index.ts            # OrgDetails, UserProfile, TenantContextType
│   ├── App.tsx                 # createBrowserRouter with nested :orgSlug routing
│   ├── main.tsx                # React DOM mount point
│   └── index.css               # Tailwind CSS v4 styling & typography tokens
├── package.json
├── tsconfig.json
└── vite.config.ts              # Vite config with @ alias & /api proxy to http://localhost:8001
```

---

## 🚀 Running Locally

```bash
# Navigate to the Web frontend directory
cd src/Web

# Install dependencies
pnpm install

# Start Vite dev server on http://localhost:3000
pnpm dev

# Build for production
pnpm build

# Preview production build
pnpm preview
```

The dev server proxies `/api` requests directly to the ASP.NET Core backend running on `http://localhost:8001`.

---

## 🌐 SPA Routing in Production

Because single-page applications use client-side routing, non-root paths like `/org/acme-corp/dashboard` require hosting platforms to rewrite unhandled paths to `index.html`:

- **Netlify / Cloudflare Pages:** Managed automatically via `public/_redirects` (`/*  /index.html  200`).
- **Nginx:**
  ```nginx
  location / {
      try_files $uri $uri/ /index.html;
  }
  ```
- **Vercel / Render:** Set a rewrite rule mapping `/(.*)` to `/index.html`.

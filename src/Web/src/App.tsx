import React from 'react';
import { createBrowserRouter, RouterProvider, Navigate, Link, useParams, useSearchParams, type RouteObject } from 'react-router-dom';
import OrgLayout from './layouts/OrgLayout';
import Dashboard from './pages/Dashboard';
import Settings from './pages/Settings';
import Members from './pages/Members';
import Profile from './pages/Profile';
import SelectOrg from './pages/SelectOrg';
import CreateOrgPage from './pages/CreateOrg/CreateOrgPage';
import Login from './pages/Login';
import CliAuthorize from './pages/CliAuthorize';
import Invitation from './pages/Invitation';
import { Invoices } from './pages/Invoices';
import { InvoiceEdit } from './pages/InvoiceEdit';
import { InvoiceDetail } from './pages/InvoiceDetail';
import { CreditsAndSettlements } from './pages/CreditsAndSettlements';
import { RecurringSchedules } from './pages/RecurringSchedules';
import { RecurringScheduleDetailPage } from './pages/RecurringScheduleDetail';
import { RecurringScheduleEdit } from './pages/RecurringScheduleEdit';
import { Parties } from './pages/Parties';
import { PartyDetail } from './pages/PartyDetail';
import { Bills } from './pages/Bills';
import { BillEdit } from './pages/BillEdit';
import { BillDetail } from './pages/BillDetail';
import { ProductsServices } from './pages/ProductsServices';
import { Documents } from './pages/Documents';
import Inventory from './pages/Inventory';
import GeneralLedger from './pages/GeneralLedger';
import JournalEntries from './pages/JournalEntries';
import { YearEndClosePage } from './components/YearEndCloseCard';
import Reports from './pages/Reports';
import { Tax } from './pages/Tax';
import ChartOfAccounts from './pages/ChartOfAccounts';
import Banking from './pages/Banking';
import BankRegisterPage from './pages/BankRegister';
import BankStatementImportPage from './pages/BankStatementImport';
import IncomingReview from './pages/IncomingReview';
import { IntegrationsSettingsTab } from './components/IntegrationsSettingsTab';
import { useIntegrations } from './lib/integrations';
import BankingTransactionsPage from './pages/BankingTransactions';
import ReconciliationHistoryPage from './pages/ReconciliationHistory';
import ReconcilePage from './pages/Reconcile';
import NotFound from './pages/NotFound';
import ProtectedRoute from './components/ProtectedRoute';
import {
  RoleRestrictedCard,
  RequiresModule,
  RequiresStatementImport,
  useTenantContext,
} from './components/Gating';
import { RouteGuard } from './components/RouteGuard';
import { publicInWorkspace, requires, requiresAny } from './lib/routeAccess';
import { useAuth } from './auth/AuthContext';
import { getPersistedOrgSlug } from './lib/storage';
import { Loader2 } from 'lucide-react';

// Root redirector: redirects to the last visited organization if authenticated, or to /login
const RootRedirect: React.FC = () => {
  const { isAuthenticated, isLoading } = useAuth();
  const lastSlug = getPersistedOrgSlug();

  if (isLoading) {
    return (
      <div className="flex h-dvh w-screen items-center justify-center bg-[var(--bg-canvas)]">
        <Loader2 className="w-6 h-6 text-[var(--brand-primary)] animate-spin" />
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  if (lastSlug) {
    return <Navigate to={`/org/${lastSlug}/dashboard`} replace />;
  }
  return <Navigate to="/select-org" replace />;
};

const WorkspaceSettings: React.FC = () => {
  const { currentOrg } = useTenantContext();
  const [params] = useSearchParams();
  const { capabilities } = useIntegrations();
  if (currentOrg.role !== 'Owner' && currentOrg.role !== 'Admin' &&
      params.get('tab') === 'integrations' && capabilities?.canManage)
    return <><h1 className="text-xl font-bold mb-4">Organization Settings</h1><IntegrationsSettingsTab /></>;
  return currentOrg.role === 'Owner' || currentOrg.role === 'Admin'
    ? <Settings />
    : <RoleRestrictedCard />;
};

// Accounting and invoicing each get a product-shaped dashboard.
const WorkspaceHome: React.FC = () => {
  return <Dashboard />;
};

// Inventory moved under Products & services; old links and bookmarks keep working.
const LegacyInventoryRedirect: React.FC = () => {
  const { orgSlug, itemId } = useParams();
  const base = `/org/${orgSlug}/items/inventory`;
  return <Navigate to={itemId ? `${base}/${itemId}` : base} replace />;
};

/**
 * The routes inside a workspace. Each one declares what it needs from the member (`handle.access`): a permission, one of several,
 * or `publicInWorkspace` with a reason. A test fails if one declares nothing, so a new page cannot ship without an answer.
 */
export const orgRoutes: RouteObject[] = [
  {
    path: '',
    element: <Navigate to="dashboard" replace />,
  },
  {
    path: 'dashboard',
    handle: publicInWorkspace('Landing page and the way back from every No-access view; guarding it would loop.'),
    element: <WorkspaceHome />,
  },
  {
    path: 'settings',
    handle: publicInWorkspace('Gated by its own Owner/Admin check, with permission checks per tab inside.'),
    element: <WorkspaceSettings />,
  },
  {
    path: 'financials',
    handle: requires('financials.journal.view', 'General ledger'),
    element: (
      <RequiresModule module="financials">
        <RouteGuard><GeneralLedger /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'financials/journals',
    handle: requires('financials.journal.view', 'Journal entries'),
    element: (
      <RequiresModule module="financials">
        <RouteGuard><JournalEntries /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'financials/year-end',
    handle: requiresAny(['financials.accounts.view', 'financials.periods.close'], 'Year-end close'),
    element: (
      <RequiresModule module="financials">
        <RouteGuard><YearEndClosePage /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'financials/accounts',
    handle: requires('financials.accounts.view', 'Chart of accounts'),
    element: (
      <RequiresModule module="financials">
        <RouteGuard><ChartOfAccounts /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'banking',
    handle: requires('banking.accounts.view', 'Banking'),
    element: (
      <RequiresModule module={['sales', 'purchases', 'financials']}>
        <RouteGuard><Banking /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'banking/transactions',
    handle: requires('banking.accounts.view', 'Bank transactions'),
    element: (
      <RequiresModule module="financials">
        <RouteGuard><BankingTransactionsPage /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'banking/reconcile',
    handle: requires('banking.accounts.view', 'Bank reconciliation'),
    element: (
      <RequiresModule module="financials">
        <RouteGuard><ReconciliationHistoryPage /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'banking/:bankId/reconcile/:reconId',
    handle: requires('banking.accounts.view', 'Bank reconciliation'),
    element: (
      <RequiresModule module="financials">
        <RouteGuard><ReconcilePage /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'banking/:bankId/reconcile',
    handle: requires('banking.accounts.view', 'Bank reconciliation'),
    element: (
      <RequiresModule module="financials">
        <RouteGuard><ReconcilePage /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'banking/needs-review',
    handle: requires('banking.accounts.view', 'Incoming review'),
    element: <RequiresStatementImport><RouteGuard><IncomingReview /></RouteGuard></RequiresStatementImport>,
  },
  {
    path: 'banking/:bankId/statement-import',
    handle: requires('banking.accounts.view', 'Statement import'),
    element: (
      <RequiresStatementImport>
        <RouteGuard><BankStatementImportPage /></RouteGuard>
      </RequiresStatementImport>
    ),
  },
  {
    path: 'banking/:bankId',
    handle: requires('banking.accounts.view', 'Bank account'),
    element: (
      <RequiresModule module={['sales', 'purchases', 'financials']}>
        <RouteGuard><BankRegisterPage /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales',
    handle: requires('sales.invoices.view', 'Invoices'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><Invoices /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/new',
    handle: requires('sales.invoices.create', 'New invoice'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><InvoiceEdit /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/clients',
    handle: requires('sales.customers.view', 'Clients'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><Parties role="client" /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/clients/:partyId',
    handle: requires('sales.customers.view', 'Clients'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><PartyDetail role="client" /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/recurring',
    handle: requires('sales.invoices.view', 'Recurring invoices'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><RecurringSchedules /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/recurring/new',
    handle: requires('sales.invoices.create', 'Recurring invoices'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><RecurringScheduleEdit /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/recurring/:scheduleId',
    handle: requires('sales.invoices.view', 'Recurring invoices'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><RecurringScheduleDetailPage /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/recurring/:scheduleId/edit',
    handle: requires('sales.invoices.create', 'Recurring invoices'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><RecurringScheduleEdit isEdit={true} /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/credits',
    handle: requires('sales.credits.view', 'Sales credits'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><CreditsAndSettlements side="sales" /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'purchases/credits',
    handle: requires('purchases.credits.view', 'Vendor credits'),
    element: (
      <RequiresModule module="purchases">
        <RouteGuard><CreditsAndSettlements side="purchasing" /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/:invoiceId',
    handle: requires('sales.invoices.view', 'Invoices'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><InvoiceDetail /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'sales/:invoiceId/edit',
    handle: requires('sales.invoices.create', 'Invoices'),
    element: (
      <RequiresModule module="sales">
        <RouteGuard><InvoiceEdit isEdit={true} /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'items',
    handle: requires('inventory.items.view', 'Products and services'),
    element: (
      <RequiresModule module={['sales', 'purchases']}>
        <RouteGuard><ProductsServices /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'documents',
    handle: requires('documents.view', 'Documents'),
    element: <RouteGuard><Documents /></RouteGuard>,
  },
  {
    path: 'items/inventory',
    handle: requires('inventory.items.view', 'Stock'),
    element: (
      <RequiresModule module="inventory">
        <RouteGuard><Inventory /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'items/inventory/:itemId',
    handle: requires('inventory.items.view', 'Stock'),
    element: (
      <RequiresModule module="inventory">
        <RouteGuard><Inventory /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'inventory',
    handle: publicInWorkspace('Redirects to the stock page and loads no data.'),
    element: <LegacyInventoryRedirect />,
  },
  {
    path: 'inventory/:itemId',
    handle: publicInWorkspace('Redirects to the stock page and loads no data.'),
    element: <LegacyInventoryRedirect />,
  },
  {
    path: 'purchases',
    handle: requires('purchases.bills.view', 'Bills'),
    element: (
      <RequiresModule module="purchases">
        <RouteGuard><Bills /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'purchases/new',
    handle: requires('purchases.bills.create', 'New bill'),
    element: (
      <RequiresModule module="purchases">
        <RouteGuard><BillEdit /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'purchases/:billId',
    handle: requires('purchases.bills.view', 'Bills'),
    element: (
      <RequiresModule module="purchases">
        <RouteGuard><BillDetail /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'purchases/:billId/edit',
    handle: requires('purchases.bills.create', 'Bills'),
    element: (
      <RequiresModule module="purchases">
        <RouteGuard><BillEdit isEdit={true} /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'purchases/vendors',
    handle: requires('purchases.vendors.view', 'Vendors'),
    element: (
      <RequiresModule module="purchases">
        <RouteGuard><Parties role="vendor" /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'purchases/vendors/:partyId',
    handle: requires('purchases.vendors.view', 'Vendors'),
    element: (
      <RequiresModule module="purchases">
        <RouteGuard><PartyDetail role="vendor" /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'reports',
    handle: requires('reports.view', 'Reports'),
    element: (
      <RequiresModule module="reports">
        <RouteGuard><Reports /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'tax',
    handle: requires('reports.view', 'Tax'),
    element: (
      <RequiresModule module={['sales', 'purchases', 'financials', 'reports']}>
        <RouteGuard><Tax /></RouteGuard>
      </RequiresModule>
    ),
  },
  {
    path: 'members',
    handle: publicInWorkspace('Every member may view the member list, and it hosts the access request and setup flows.'),
    element: <Members />,
  },
  {
    path: 'profile',
    handle: publicInWorkspace("The signed-in user's own account."),
    element: <Profile />,
  },
];

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: <RootRedirect />,
  },
  {
    path: '/login',
    element: <Login />,
  },
  {
    path: '/signup',
    element: <Login initialMode="signup" />,
  },
  {
    path: '/cli/authorize',
    element: <CliAuthorize />,
  },
  {
    path: '/invite',
    element: <Invitation />,
  },
  {
    path: '/invite/:token',
    element: <Invitation />,
  },
  {
    element: <ProtectedRoute />,
    children: [
      {
        path: '/select-org',
        element: <SelectOrg />,
      },
      {
        path: '/profile',
        element: (
          <div className="min-h-screen bg-[var(--bg-canvas)] p-4 sm:p-8">
            <div className="max-w-6xl mx-auto">
              <Link to="/select-org" className="inline-block mb-6 text-sm text-[var(--brand-primary)]">← Back to organizations</Link>
              <Profile />
            </div>
          </div>
        ),
      },
      {
        path: '/create-org',
        element: <CreateOrgPage />,
      },
      {
        path: '/org/:orgSlug',
        element: <OrgLayout />,
        children: orgRoutes,
      },
    ],
  },
  {
    path: '*',
    element: <NotFound />,
  },
];

const router = createBrowserRouter(appRoutes);

export function App() {
  return <RouterProvider router={router} />;
}

export default App;

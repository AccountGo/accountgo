import React from 'react';
import { createBrowserRouter, RouterProvider, Navigate, Link, useParams, useSearchParams } from 'react-router-dom';
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
import { RecurringSchedules } from './pages/RecurringSchedules';
import { RecurringScheduleDetailPage } from './pages/RecurringScheduleDetail';
import { RecurringScheduleEdit } from './pages/RecurringScheduleEdit';
import { Parties } from './pages/Parties';
import { PartyDetail } from './pages/PartyDetail';
import { Bills } from './pages/Bills';
import { BillEdit } from './pages/BillEdit';
import { BillDetail } from './pages/BillDetail';
import { ProductsServices } from './pages/ProductsServices';
import Inventory from './pages/Inventory';
import GeneralLedger from './pages/GeneralLedger';
import JournalEntries from './pages/JournalEntries';
import Reports from './pages/Reports';
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
  PermissionDeniedCard,
  RequiresModule,
  RequiresStatementImport,
  useTenantContext,
} from './components/Gating';
import { useAuth } from './auth/AuthContext';
import { getPersistedOrgSlug } from './lib/storage';
import { Loader2 } from 'lucide-react';

// Root redirector: redirects to the last visited organization if authenticated, or to /login
const RootRedirect: React.FC = () => {
  const { isAuthenticated, isLoading } = useAuth();
  const lastSlug = getPersistedOrgSlug();

  if (isLoading) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-[var(--bg-canvas)]">
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
    : <PermissionDeniedCard permission="Owner or Admin" />;
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

const router = createBrowserRouter([
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
        children: [
          {
            path: '',
            element: <Navigate to="dashboard" replace />,
          },
          {
            path: 'dashboard',
            element: <WorkspaceHome />,
          },
          {
            path: 'settings',
            element: <WorkspaceSettings />,
          },
          {
            path: 'financials',
            element: (
              <RequiresModule module="financials">
                <GeneralLedger />
              </RequiresModule>
            ),
          },
          {
            path: 'financials/journals',
            element: (
              <RequiresModule module="financials">
                <JournalEntries />
              </RequiresModule>
            ),
          },
          {
            path: 'financials/accounts',
            element: (
              <RequiresModule module="financials">
                <ChartOfAccounts />
              </RequiresModule>
            ),
          },
          {
            path: 'banking',
            element: (
              <RequiresModule module={['sales', 'purchases', 'financials']}>
                <Banking />
              </RequiresModule>
            ),
          },
          {
            path: 'banking/transactions',
            element: (
              <RequiresModule module="financials">
                <BankingTransactionsPage />
              </RequiresModule>
            ),
          },
          {
            path: 'banking/reconcile',
            element: (
              <RequiresModule module="financials">
                <ReconciliationHistoryPage />
              </RequiresModule>
            ),
          },
          {
            path: 'banking/:bankId/reconcile/:reconId',
            element: (
              <RequiresModule module="financials">
                <ReconcilePage />
              </RequiresModule>
            ),
          },
          {
            path: 'banking/:bankId/reconcile',
            element: (
              <RequiresModule module="financials">
                <ReconcilePage />
              </RequiresModule>
            ),
          },
          {
            path: 'banking/needs-review',
            element: <RequiresStatementImport><IncomingReview /></RequiresStatementImport>,
          },
          {
            path: 'banking/:bankId/statement-import',
            element: (
              <RequiresStatementImport>
                <BankStatementImportPage />
              </RequiresStatementImport>
            ),
          },
          {
            path: 'banking/:bankId',
            element: (
              <RequiresModule module={['sales', 'purchases', 'financials']}>
                <BankRegisterPage />
              </RequiresModule>
            ),
          },
          {
            path: 'sales',
            element: (
              <RequiresModule module="sales">
                <Invoices />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/new',
            element: (
              <RequiresModule module="sales">
                <InvoiceEdit />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/clients',
            element: (
              <RequiresModule module="sales">
                <Parties role="client" />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/clients/:partyId',
            element: (
              <RequiresModule module="sales">
                <PartyDetail role="client" />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/recurring',
            element: (
              <RequiresModule module="sales">
                <RecurringSchedules />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/recurring/new',
            element: (
              <RequiresModule module="sales">
                <RecurringScheduleEdit />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/recurring/:scheduleId',
            element: (
              <RequiresModule module="sales">
                <RecurringScheduleDetailPage />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/recurring/:scheduleId/edit',
            element: (
              <RequiresModule module="sales">
                <RecurringScheduleEdit isEdit={true} />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/:invoiceId',
            element: (
              <RequiresModule module="sales">
                <InvoiceDetail />
              </RequiresModule>
            ),
          },
          {
            path: 'sales/:invoiceId/edit',
            element: (
              <RequiresModule module="sales">
                <InvoiceEdit isEdit={true} />
              </RequiresModule>
            ),
          },
          {
            path: 'items',
            element: (
              <RequiresModule module={['sales', 'purchases']}>
                <ProductsServices />
              </RequiresModule>
            ),
          },
          {
            path: 'items/inventory',
            element: (
              <RequiresModule module="inventory">
                <Inventory />
              </RequiresModule>
            ),
          },
          {
            path: 'items/inventory/:itemId',
            element: (
              <RequiresModule module="inventory">
                <Inventory />
              </RequiresModule>
            ),
          },
          {
            path: 'inventory',
            element: <LegacyInventoryRedirect />,
          },
          {
            path: 'inventory/:itemId',
            element: <LegacyInventoryRedirect />,
          },
          {
            path: 'purchases',
            element: (
              <RequiresModule module="purchases">
                <Bills />
              </RequiresModule>
            ),
          },
          {
            path: 'purchases/new',
            element: (
              <RequiresModule module="purchases">
                <BillEdit />
              </RequiresModule>
            ),
          },
          {
            path: 'purchases/:billId',
            element: (
              <RequiresModule module="purchases">
                <BillDetail />
              </RequiresModule>
            ),
          },
          {
            path: 'purchases/:billId/edit',
            element: (
              <RequiresModule module="purchases">
                <BillEdit isEdit={true} />
              </RequiresModule>
            ),
          },
          {
            path: 'purchases/vendors',
            element: (
              <RequiresModule module="purchases">
                <Parties role="vendor" />
              </RequiresModule>
            ),
          },
          {
            path: 'purchases/vendors/:partyId',
            element: (
              <RequiresModule module="purchases">
                <PartyDetail role="vendor" />
              </RequiresModule>
            ),
          },
          {
            path: 'reports',
            element: (
              <RequiresModule module="reports">
                <Reports />
              </RequiresModule>
            ),
          },
          {
            path: 'members',
            element: <Members />,
          },
          {
            path: 'profile',
            element: <Profile />,
          },
        ],
      },
    ],
  },
  {
    path: '*',
    element: <NotFound />,
  },
]);

export function App() {
  return <RouterProvider router={router} />;
}

export default App;

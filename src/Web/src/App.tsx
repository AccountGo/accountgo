import React from 'react';
import { createBrowserRouter, RouterProvider, Navigate, useParams } from 'react-router-dom';
import OrgLayout from './layouts/OrgLayout';
import Dashboard from './pages/Dashboard';
import Settings from './pages/Settings';
import Members from './pages/Members';
import Profile from './pages/Profile';
import SelectOrg from './pages/SelectOrg';
import Login from './pages/Login';
import Invitation from './pages/Invitation';
import { Invoices } from './pages/Invoices';
import { InvoiceEdit } from './pages/InvoiceEdit';
import { InvoiceDetail } from './pages/InvoiceDetail';
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
import NotFound from './pages/NotFound';
import ProtectedRoute from './components/ProtectedRoute';
import { PermissionDeniedCard, RequiresModule, useTenantContext } from './components/Gating';
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
  return currentOrg.role === 'Owner' || currentOrg.role === 'Admin'
    ? <Settings />
    : <PermissionDeniedCard permission="Owner or Admin" />;
};

// The dashboard summarises the books, so it needs the accounting module; an invoicing-only workspace opens on its invoices.
const WorkspaceHome: React.FC = () => {
  const { hasModule } = useTenantContext();
  return hasModule('financials') ? <Dashboard /> : <Navigate to="../sales" replace />;
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

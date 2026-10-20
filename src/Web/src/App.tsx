import React from 'react';
import { createBrowserRouter, RouterProvider, Navigate } from 'react-router-dom';
import OrgLayout from './layouts/OrgLayout';
import Dashboard from './pages/Dashboard';
import Settings from './pages/Settings';
import SelectOrg from './pages/SelectOrg';
import Login from './pages/Login';
import PlaceholderView from './pages/PlaceholderView';
import NotFound from './pages/NotFound';
import ProtectedRoute from './components/ProtectedRoute';
import { RequiresModule } from './components/Gating';
import { useAuth } from './auth/AuthContext';
import { getPersistedOrgSlug } from './lib/mockData';
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
            element: <Dashboard />,
          },
          {
            path: 'settings',
            element: <Settings />,
          },
          {
            path: 'financials',
            element: (
              <RequiresModule module="financials">
                <PlaceholderView />
              </RequiresModule>
            ),
          },
          {
            path: 'sales',
            element: (
              <RequiresModule module="sales">
                <PlaceholderView />
              </RequiresModule>
            ),
          },
          {
            path: 'purchases',
            element: (
              <RequiresModule module="purchases">
                <PlaceholderView />
              </RequiresModule>
            ),
          },
          {
            path: 'reports',
            element: (
              <RequiresModule module="reports">
                <PlaceholderView />
              </RequiresModule>
            ),
          },
          {
            path: 'members',
            element: <Settings />,
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

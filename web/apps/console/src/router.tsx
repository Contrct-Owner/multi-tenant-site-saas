import { createRootRoute, createRoute, createRouter, Outlet } from '@tanstack/react-router';
import { Shell } from './shell';
import { AuditPage } from './pages/audit';
import { DashboardPage } from './pages/dashboard';
import { HierarchyPage } from './pages/hierarchy';
import { IngestPage } from './pages/ingest';
import { SitesPage } from './pages/sites';

const rootRoute = createRootRoute({
  component: () => (
    <Shell>
      <Outlet />
    </Shell>
  ),
});

const routes = [
  createRoute({ getParentRoute: () => rootRoute, path: '/', component: DashboardPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/sites', component: SitesPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/hierarchy', component: HierarchyPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/ingest', component: IngestPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/audit', component: AuditPage }),
];

export const router = createRouter({ routeTree: rootRoute.addChildren(routes) });

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}

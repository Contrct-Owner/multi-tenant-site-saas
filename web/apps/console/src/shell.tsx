import { api } from '@premise/api';
import { Badge, Button, cn } from '@premise/ui';
import { useQueryClient } from '@tanstack/react-query';
import { Link, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { can, useMe } from './session';

const NAV = [
  { to: '/', label: 'Dashboard', capability: null },
  { to: '/sites', label: 'Sites', capability: 'sites:read' },
  { to: '/hierarchy', label: 'Hierarchy', capability: 'hierarchy:manage' },
  { to: '/ingest', label: 'Ingest', capability: 'ingest:manage' },
  { to: '/audit', label: 'Audit', capability: 'audit:read' },
] as const;

export function Shell({ children }: { children: ReactNode }) {
  const { data: me, isLoading } = useMe();
  const queryClient = useQueryClient();
  const path = useRouterState({ select: (s) => s.location.pathname });

  if (isLoading) return <div className="p-12 text-muted-foreground">Loading session…</div>;

  if (me?.tier !== 'user') {
    return (
      <main className="flex min-h-screen items-center justify-center bg-background">
        <div className="w-full max-w-sm space-y-4 rounded-lg border bg-card p-8 text-center">
          <h1 className="text-xl font-semibold">Premise Console</h1>
          <p className="text-sm text-muted-foreground">Sign in to manage your organization.</p>
          <Button asChild className="w-full">
            <a href={`/auth/login?returnUrl=${encodeURIComponent(location.pathname)}`}>Sign in</a>
          </Button>
        </div>
      </main>
    );
  }

  const activeOrg = me.organizations.find((o) => o.id === me.activeOrg);
  return (
    <div className="flex min-h-screen bg-background">
      <aside className="flex w-56 flex-col border-r bg-card">
        <div className="border-b p-4">
          <div className="font-semibold">Premise</div>
          <div className="text-xs text-muted-foreground">{activeOrg?.name ?? 'No organization'}</div>
        </div>
        <nav className="flex-1 space-y-1 p-2">
          {NAV.filter((n) => n.capability === null || can(me, n.capability)).map((n) => (
            <Link
              key={n.to}
              to={n.to}
              className={cn(
                'block rounded-md px-3 py-2 text-sm hover:bg-accent',
                path === n.to && 'bg-accent font-medium',
              )}
            >
              {n.label}
            </Link>
          ))}
        </nav>
        <div className="space-y-2 border-t p-3">
          {me.organizations.length > 1 && (
            <select
              className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
              value={me.activeOrg ?? ''}
              onChange={async (e) => {
                await api.post('/auth/switch-org', { orgId: e.target.value });
                await queryClient.invalidateQueries(); // org switch re-resolves everything
              }}
            >
              {me.organizations.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.name}
                </option>
              ))}
            </select>
          )}
          <div className="flex items-center justify-between gap-2">
            <span className="truncate text-xs text-muted-foreground">{me.email}</span>
            <Button
              variant="ghost"
              size="sm"
              onClick={async () => {
                await api.post('/auth/logout');
                await queryClient.invalidateQueries({ queryKey: ['me'] });
              }}
            >
              Sign out
            </Button>
          </div>
        </div>
      </aside>
      <main className="flex-1 overflow-auto p-8">{children}</main>
    </div>
  );
}

export function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'Open' || status === 'Clean' || status === 'Committed'
      ? 'success'
      : status === 'Closed' || status === 'Quarantined'
        ? 'destructive'
        : 'secondary';
  return <Badge variant={variant as never}>{status}</Badge>;
}

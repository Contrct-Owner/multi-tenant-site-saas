import { api, ENTITLEMENTS, type EntitlementCode } from '@premise/api';
import { useQuery } from '@tanstack/react-query';
import { IconTile, Progress } from '@premise/ui';
import { Link } from '@tanstack/react-router';
import { Activity, MapPin, Users } from 'lucide-react';
import { EmptyState, Loading, PageHeader, Panel, Stat } from '../components/page';
import {entitlementLabel, fmtDateTime, eventLabel } from '../lib/format';
import { can, useMe } from '../session';


/** The overview (UX review P2): what needs attention, then the plan. */
export function DashboardPage() {
  const { data: me } = useMe();
  const seesSites = can(me, 'sites:read');
  const seesMembers = can(me, 'roles:manage');
  const seesAudit = can(me, 'audit:read');

  const { data: entitlements } = useQuery({
    queryKey: ['entitlements'],
    queryFn: ({ signal }) => api.get('/api/entitlements', { signal }),
  });
  const { data: sites } = useQuery({
    queryKey: ['sites', 'summary'],
    queryFn: ({ signal }) => api.get('/api/sites', { query: { limit: 1 }, signal }),
    enabled: seesSites,
  });
  const { data: invitations } = useQuery({
    queryKey: ['invitations'],
    queryFn: ({ signal }) => api.get('/api/members/invitations', { signal }),
    enabled: seesMembers,
  });
  const { data: events } = useQuery({
    queryKey: ['audit', 'events', 5],
    queryFn: ({ signal }) => api.get('/api/audit/{kind}', { path: { kind: 'events' }, query: { limit: 5 }, signal }),
    enabled: seesAudit,
  });

  if (me?.tier !== 'user') return null;

  const pending = invitations?.filter((i) => i.state === 'pending').length ?? 0;

  return (
    <div className="max-w-4xl space-y-6">
      <PageHeader title="Dashboard" description="What needs attention, then the plan." />

      <div className="grid gap-4 sm:grid-cols-2">
        {seesSites && (
          <Stat
            to="/sites"
            icon={MapPin}
            label="Sites"
            value={sites === undefined ? '—' : sites.total}
            hint={sites === undefined ? undefined : `${sites.openCount ?? 0} open right now`}
          />
        )}
        {seesMembers && (
          <Stat
            to="/members"
            icon={Users}
            label="Pending invitations"
            value={invitations === undefined ? '—' : pending}
            hint={pending === 0 ? 'Everyone invited has joined' : 'Waiting on a reply'}
          />
        )}
      </div>

      {seesAudit && (
        <Panel
          title="Recent activity"
          actions={
            <Link to="/audit" className="text-sm text-muted-foreground hover:underline">
              All activity →
            </Link>
          }
        >
            {events === undefined ? (
              <Loading text="Loading activity…" rows={3} />
            ) : events.length === 0 ? (
              <EmptyState
                icon={Activity}
                title="Nothing recorded yet"
                description="Changes people make show up here as they happen."
                className="py-6"
              />
            ) : (
              <ul className="divide-y text-sm">
                {events.map((e) => (
                  <li key={e.id} className="flex items-center gap-3 py-2 first:pt-0 last:pb-0">
                    <IconTile variant="soft" size="sm">
                      <Activity />
                    </IconTile>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate">{eventLabel(e.eventName ?? 'unknown')}</span>
                      {e.actorLabel && (
                        <span className="block truncate text-xs text-muted-foreground">{e.actorLabel}</span>
                      )}
                    </span>
                    <span className="shrink-0 text-xs text-muted-foreground">
                      {fmtDateTime(e.occurredAt)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
        </Panel>
      )}

      <Panel title="Plan">
          <div className="grid grid-cols-1 gap-x-8 gap-y-2 text-sm sm:grid-cols-2">
            {entitlements &&
              (Object.keys(ENTITLEMENTS) as EntitlementCode[]).map((code) => {
                const entry = entitlements[code];
                const limit = Number(entry?.value);
                const showBar =
                  entry?.usage != null && Number.isFinite(limit) && limit > 0;
                const ratio = showBar ? Math.min(Number(entry.usage) / limit, 1) : 0;
                return (
                  <div key={code} className="space-y-1 border-b py-1.5">
                    <div className="flex justify-between">
                      <span className="text-muted-foreground" title={code}>
                        {entitlementLabel(code)}
                      </span>
                      <span className="font-medium tabular-nums">
                        {entry?.usage != null
                          ? `${entry.usage} of ${entry.value}`
                          : entry?.value}
                      </span>
                    </div>
                    {showBar && (
                      <Progress
                        value={Math.max(ratio * 100, 2)}
                        aria-label={`${entitlementLabel(code)} usage`}
                        className={ratio >= 1 ? '**:data-[slot=progress-indicator]:bg-destructive' : undefined}
                      />
                    )}
                  </div>
                );
              })}
          </div>
      </Panel>
    </div>
  );
}

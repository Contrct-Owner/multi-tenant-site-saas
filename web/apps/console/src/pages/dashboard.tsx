import { api, ENTITLEMENTS, type EntitlementCode } from '@premise/api';
import { Card, CardContent, CardHeader, CardTitle } from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { entitlementLabel, fmtDateTime } from '../lib/format';
import { can, useMe } from '../session';

type Effective = Record<string, { value: string; shape: string; policy: string }>;
type Site = { id: string; name: string; status: string };
type Invitation = { id: string; state: string };
type AuditEvent = { id: string; eventName: string; actorTier: string; occurredAt: string };

/** The overview (UX review P2): what needs attention, then the plan. */
export function DashboardPage() {
  const { data: me } = useMe();
  const seesSites = can(me, 'sites:read');
  const seesMembers = can(me, 'roles:manage');
  const seesAudit = can(me, 'audit:read');

  const { data: entitlements } = useQuery({
    queryKey: ['entitlements'],
    queryFn: () => api.get<Effective>('/api/entitlements'),
  });
  const { data: sites } = useQuery({
    queryKey: ['sites'],
    queryFn: () => api.get<Site[]>('/api/sites'),
    enabled: seesSites,
  });
  const { data: invitations } = useQuery({
    queryKey: ['invitations'],
    queryFn: () => api.get<Invitation[]>('/api/members/invitations'),
    enabled: seesMembers,
  });
  const { data: events } = useQuery({
    queryKey: ['audit', 'events', 5],
    queryFn: () => api.get<AuditEvent[]>('/api/audit/events?limit=5'),
    enabled: seesAudit,
  });

  if (me?.tier !== 'user') return null;
  const open = sites?.filter((s) => s.status === 'Open').length ?? 0;
  const pending = invitations?.filter((i) => i.state === 'pending').length ?? 0;

  return (
    <div className="max-w-3xl space-y-6">
      <h1 className="text-2xl font-semibold">Dashboard</h1>

      <div className="grid grid-cols-2 gap-4">
        {seesSites && (
          <Card>
            <CardContent className="pt-5">
              <Link to="/sites" className="block">
                <div className="text-3xl font-semibold tabular-nums">
                  {sites === undefined ? '—' : sites.length}
                </div>
                <div className="text-sm text-muted-foreground">
                  sites · {open} open
                </div>
              </Link>
            </CardContent>
          </Card>
        )}
        {seesMembers && (
          <Card>
            <CardContent className="pt-5">
              <Link to="/members" className="block">
                <div className="text-3xl font-semibold tabular-nums">
                  {invitations === undefined ? '—' : pending}
                </div>
                <div className="text-sm text-muted-foreground">pending invitations</div>
              </Link>
            </CardContent>
          </Card>
        )}
      </div>

      {seesAudit && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between">
              Recent activity
              <Link to="/audit" className="text-sm font-normal text-muted-foreground hover:underline">
                All activity →
              </Link>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {events === undefined ? (
              <p className="text-sm text-muted-foreground">Loading…</p>
            ) : events.length === 0 ? (
              <p className="text-sm text-muted-foreground">Nothing recorded yet.</p>
            ) : (
              <ul className="space-y-1.5 text-sm">
                {events.map((e) => (
                  <li key={e.id} className="flex justify-between gap-4">
                    <span className="truncate font-mono text-xs">{e.eventName}</span>
                    <span className="shrink-0 text-xs text-muted-foreground">
                      {fmtDateTime(e.occurredAt)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader><CardTitle>Plan</CardTitle></CardHeader>
        <CardContent>
          <dl className="grid grid-cols-2 gap-x-8 gap-y-2 text-sm">
            {entitlements &&
              (Object.keys(ENTITLEMENTS) as EntitlementCode[]).map((code) => (
                <div key={code} className="flex justify-between border-b py-1.5">
                  <dt className="text-muted-foreground" title={code}>
                    {entitlementLabel(code)}
                  </dt>
                  <dd className="font-medium">{entitlements[code]?.value}</dd>
                </div>
              ))}
          </dl>
        </CardContent>
      </Card>
    </div>
  );
}

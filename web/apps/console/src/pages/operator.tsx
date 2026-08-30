import { api, ENTITLEMENTS, type EntitlementCode } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input } from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { entitlementLabel } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { StatusBadge } from '../shell';

type OperatedOrg = {
  id: string; name: string; slug: string; status: string;
  isPlatform: boolean; createdAt: string;
};
type Effective = Record<string, { value: string; shape: string; policy: string }>;

/** Entitlement custody + lifecycle: operator-set, tenant-read. */
export function OperatorPage() {
  const { data: orgs } = useQuery({
    queryKey: ['operator-orgs'],
    queryFn: () => api.get<OperatedOrg[]>('/api/operator/orgs'),
  });
  const [selected, setSelected] = useState<OperatedOrg | null>(null);

  const transition = useApiMutation({
    mutationFn: (input: { orgId: string; action: 'suspend' | 'reactivate' }) =>
      api.post(`/api/operator/orgs/${input.orgId}/${input.action}`),
    invalidate: [['operator-orgs']],
    success: 'Status updated',
  });
  const exportOrg = useApiMutation({
    mutationFn: (orgId: string) => api.post(`/api/operator/orgs/${orgId}/export`),
    success: "Export queued - it lands in the org's Files",
  });
  const offboard = useApiMutation({
    mutationFn: (orgId: string) => api.post(`/api/operator/orgs/${orgId}/offboard`),
    invalidate: [['operator-orgs']],
    success: 'Offboarding started',
    onSuccess: () => setSelected(null),
  });

  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Operator</h1>
      <div className="grid grid-cols-[280px_1fr] gap-6">
        <Card>
          <CardHeader><CardTitle>Organizations</CardTitle></CardHeader>
          <CardContent className="space-y-1">
            {orgs?.map((org) => (
              <button
                key={org.id}
                type="button"
                onClick={() => setSelected(org)}
                className={`flex w-full items-center justify-between rounded-md px-2 py-1.5 text-left text-sm hover:bg-accent ${
                  selected?.id === org.id ? 'bg-accent' : ''
                }`}
              >
                <span>
                  {org.name}
                  {org.isPlatform && <span className="ml-1 text-xs text-muted-foreground">(platform)</span>}
                </span>
                <StatusBadge status={org.status} />
              </button>
            ))}
          </CardContent>
        </Card>
        {selected && !selected.isPlatform && (
          <div className="space-y-4">
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center justify-between">
                  {selected.name}
                  {selected.status === 'Active' ? (
                    <Button
                      variant="destructive"
                      size="sm"
                      disabled={transition.isPending}
                      onClick={() => transition.mutate({ orgId: selected.id, action: 'suspend' })}
                    >
                      Suspend
                    </Button>
                  ) : (
                    <Button
                      size="sm"
                      disabled={transition.isPending}
                      onClick={() => transition.mutate({ orgId: selected.id, action: 'reactivate' })}
                    >
                      Reactivate
                    </Button>
                  )}
                </CardTitle>
              </CardHeader>
              <CardContent>
                <OrgEntitlements orgId={selected.id} />
              </CardContent>
            </Card>
            <Card>
              <CardHeader><CardTitle>Lifecycle</CardTitle></CardHeader>
              <CardContent className="space-y-3">
                <div className="flex items-center gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={exportOrg.isPending}
                    onClick={() => exportOrg.mutate(selected.id)}
                  >
                    Export data
                  </Button>
                  <span className="text-sm text-muted-foreground">
                    {exportOrg.isSuccess
                      ? "Queued - the archive lands in the org's Files."
                      : 'Full data archive, delivered to the org’s file library.'}
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <ConfirmButton
                    variant="destructive"
                    size="sm"
                    confirmLabel="Purge org data?"
                    disabled={offboard.isPending || selected.status !== 'Suspended'}
                    onConfirm={() => offboard.mutate(selected.id)}
                  >
                    Offboard
                  </ConfirmButton>
                  <span className="text-sm text-muted-foreground">
                    {selected.status === 'Suspended'
                      ? 'Purges all org data. The audit trail and org record remain.'
                      : 'Suspend the org first - offboarding is a deliberate two-step.'}
                  </span>
                </div>
              </CardContent>
            </Card>
          </div>
        )}
      </div>
    </div>
  );
}

function OrgEntitlements({ orgId }: { orgId: string }) {
  const { data: effective } = useQuery({
    queryKey: ['operator-entitlements', orgId],
    queryFn: () => api.get<Effective>(`/api/operator/orgs/${orgId}/entitlements`),
  });
  const [drafts, setDrafts] = useState<Record<string, string>>({});

  const set = useApiMutation({
    mutationFn: (input: { code: string; value: string }) =>
      api.put(`/api/operator/orgs/${orgId}/entitlements/${input.code}`, { value: input.value }),
    invalidate: [['operator-entitlements', orgId]],
    success: 'Entitlement updated',
    errorFallback: 'Update failed',
  });

  return (
    <div className="space-y-2">
      {effective &&
        (Object.keys(ENTITLEMENTS) as EntitlementCode[]).map((code) => {
          const draft = drafts[code] ?? effective[code]?.value ?? '';
          const dirty = draft !== effective[code]?.value;
          return (
            <div key={code} className="flex items-center gap-2 text-sm">
              <span className="w-56 text-muted-foreground" title={code}>
                {entitlementLabel(code)}
              </span>
              <Input
                className="h-8 w-32"
                value={draft}
                onChange={(e) => setDrafts({ ...drafts, [code]: e.target.value })}
              />
              {dirty && (
                <Button size="sm" disabled={set.isPending}
                  onClick={() => set.mutate({ code, value: draft })}>
                  Save
                </Button>
              )}
            </div>
          );
        })}
    </div>
  );
}

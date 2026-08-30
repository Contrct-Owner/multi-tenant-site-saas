import { api, ENTITLEMENTS, type EntitlementCode } from '@premise/api';
import { Card, CardContent, CardHeader, CardTitle } from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { entitlementLabel } from '../lib/format';
import { useMe } from '../session';

type Effective = Record<string, { value: string; shape: string; policy: string }>;

export function DashboardPage() {
  const { data: me } = useMe();
  const { data: entitlements } = useQuery({
    queryKey: ['entitlements'],
    queryFn: () => api.get<Effective>('/api/entitlements'),
  });

  if (me?.tier !== 'user') return null;
  return (
    <div className="max-w-3xl space-y-6">
      <h1 className="text-2xl font-semibold">Dashboard</h1>
      <Card>
        <CardHeader>
          <CardTitle>Plan entitlements</CardTitle>
        </CardHeader>
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

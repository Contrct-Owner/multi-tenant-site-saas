import { api } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from '@premise/ui';
import { useState } from 'react';
import { useApiMutation } from '../lib/mutation';
import { useMe } from '../session';

export function SettingsPage() {
  const { data: me } = useMe();
  const activeOrg =
    me?.tier === 'user' ? me.organizations.find((o) => o.id === me.activeOrg) : undefined;
  const [name, setName] = useState<string | null>(null);

  const rename = useApiMutation({
    mutationFn: (value: string) => api.put('/api/org', { name: value }),
    invalidate: [['me']],
    success: 'Organization renamed',
    onSuccess: () => setName(null),
  });
  const exportData = useApiMutation({
    mutationFn: () => api.post('/api/org/export'),
    success: 'Export queued - check Files shortly',
  });

  if (!activeOrg) return null;
  const draft = name ?? activeOrg.name;
  return (
    <div className="max-w-lg space-y-6">
      <h1 className="text-2xl font-semibold">Organization settings</h1>
      <Card>
        <CardHeader><CardTitle>Profile</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <div className="space-y-1">
            <Label htmlFor="org-rename">Name</Label>
            <Input id="org-rename" value={draft} onChange={(e) => setName(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label>URL slug</Label>
            <Input value={activeOrg.slug} disabled />
          </div>
          <Button
            disabled={draft === activeOrg.name || !draft.trim() || rename.isPending}
            onClick={() => rename.mutate(draft.trim())}
          >
            Save
          </Button>
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Your data</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Take a full archive of this organization&apos;s data - sites, people, roles,
            entitlements, and audit history. The archive is delivered to Files.
          </p>
          <Button
            variant="outline"
            disabled={exportData.isPending}
            onClick={() => exportData.mutate()}
          >
            {exportData.isSuccess ? 'Queued - check Files shortly' : 'Export org data'}
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}

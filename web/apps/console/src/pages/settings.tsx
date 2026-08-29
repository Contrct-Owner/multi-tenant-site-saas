import { api } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from '@premise/ui';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useMe } from '../session';

export function SettingsPage() {
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const activeOrg =
    me?.tier === 'user' ? me.organizations.find((o) => o.id === me.activeOrg) : undefined;
  const [name, setName] = useState<string | null>(null);

  const rename = useMutation({
    mutationFn: (value: string) => api.put('/api/org', { name: value }),
    onSuccess: () => {
      setName(null);
      void queryClient.invalidateQueries({ queryKey: ['me'] });
    },
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
          {rename.isError && (
            <p className="text-sm text-destructive">
              {String((rename.error as { body?: { error?: string } }).body?.error ?? rename.error)}
            </p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

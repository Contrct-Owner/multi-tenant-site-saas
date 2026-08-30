import { api } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@premise/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { can, useMe } from '../session';
import { StatusBadge } from '../shell';

type Site = { id: string; nodeId: string; name: string; timeZone: string; status: string; path: string };
type Hierarchy = { id: string; nodes: { id: string; name: string; depth: number }[] };

export function SitesPage() {
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const { data: sites } = useQuery({
    queryKey: ['sites'],
    queryFn: () => api.get<Site[]>('/api/sites'),
  });
  const { data: hierarchy } = useQuery({
    queryKey: ['hierarchy'],
    queryFn: () => api.get<Hierarchy>('/api/hierarchy'),
  });
  const [name, setName] = useState('');
  const [timeZone, setTimeZone] = useState('America/New_York');
  const [nodeId, setNodeId] = useState('');

  const create = useMutation({
    mutationFn: () => api.post('/api/sites', { nodeId, name, timeZone }),
    onSuccess: () => {
      setName('');
      void queryClient.invalidateQueries({ queryKey: ['sites'] });
    },
  });

  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Sites</h1>
      <Card>
        <CardContent className="pt-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Time zone</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {sites?.map((s) => (
                <TableRow key={s.id}>
                  <TableCell className="font-medium">
                    <Link
                      to="/sites/$siteId"
                      params={{ siteId: s.id }}
                      className="underline-offset-4 hover:underline"
                    >
                      {s.name}
                    </Link>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{s.timeZone}</TableCell>
                  <TableCell><StatusBadge status={s.status} /></TableCell>
                </TableRow>
              ))}
              {sites?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={3} className="text-center text-muted-foreground">
                    No sites in scope.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      {can(me, 'sites:manage') && (
        <Card>
          <CardHeader><CardTitle>New site</CardTitle></CardHeader>
          <CardContent className="space-y-3">
            <div className="grid grid-cols-3 gap-3">
              <div className="space-y-1">
                <Label htmlFor="site-name">Name</Label>
                <Input id="site-name" value={name} onChange={(e) => setName(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="site-tz">IANA time zone</Label>
                <Input id="site-tz" value={timeZone} onChange={(e) => setTimeZone(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="site-node">Hierarchy node</Label>
                <select
                  id="site-node"
                  className="h-9 w-full rounded-md border bg-background px-2 text-sm"
                  value={nodeId}
                  onChange={(e) => setNodeId(e.target.value)}
                >
                  <option value="">Choose…</option>
                  {hierarchy?.nodes.map((n) => (
                    <option key={n.id} value={n.id}>
                      {' '.repeat(n.depth * 2)}{n.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>
            <Button disabled={!name || !nodeId || create.isPending} onClick={() => create.mutate()}>
              Create site
            </Button>
            {create.isError && (
              <p className="text-sm text-destructive">
                {String((create.error as { body?: { error?: string } }).body?.error ?? create.error)}
              </p>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

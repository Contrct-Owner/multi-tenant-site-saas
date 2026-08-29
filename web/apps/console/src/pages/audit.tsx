import { api } from '@premise/api';
import { Button, Card, CardContent, Table, TableBody, TableCell, TableHead,
  TableHeader, TableRow } from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

const KINDS = ['events', 'changes', 'authz', 'access'] as const;
type Kind = (typeof KINDS)[number];
type Row = Record<string, unknown> & { id: string; occurredAt: string; actorTier: string };

export function AuditPage() {
  const [kind, setKind] = useState<Kind>('events');
  const { data: rows } = useQuery({
    queryKey: ['audit', kind],
    queryFn: () => api.get<Row[]>(`/api/audit/${kind}`),
  });

  const detail = (row: Row): string => {
    switch (kind) {
      case 'events':
        return `${String(row.eventName)} ${String(row.payload ?? '')}`;
      case 'changes':
        return `${String(row.operation)} ${String(row.schemaName)}.${String(row.tableName)} ${String(row.diff ?? '')}`;
      case 'authz':
        return `${String(row.action)} → ${String(row.outcome)} (${String(row.scopeSummary)})`;
      case 'access':
        return `${String(row.method)} ${String(row.path)} → ${String(row.statusCode)}`;
    }
  };

  return (
    <div className="max-w-5xl space-y-6">
      <h1 className="text-2xl font-semibold">Audit</h1>
      <div className="flex gap-2">
        {KINDS.map((k) => (
          <Button key={k} size="sm" variant={k === kind ? 'default' : 'outline'} onClick={() => setKind(k)}>
            {k}
          </Button>
        ))}
      </div>
      <Card>
        <CardContent className="pt-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-44">When</TableHead>
                <TableHead className="w-20">Actor</TableHead>
                <TableHead>Detail</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows?.map((row) => (
                <TableRow key={row.id}>
                  <TableCell className="text-xs text-muted-foreground">
                    {new Date(row.occurredAt).toLocaleString()}
                  </TableCell>
                  <TableCell className="text-xs">{row.actorTier}</TableCell>
                  <TableCell className="max-w-xl truncate font-mono text-xs">{detail(row)}</TableCell>
                </TableRow>
              ))}
              {rows?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={3} className="text-center text-muted-foreground">
                    Nothing recorded yet.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}

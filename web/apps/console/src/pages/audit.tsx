import { api, type components } from '@premise/api';
import { Button, type ColumnDef, type DataGridFeatures } from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Grid, PageHeader } from '../components/page';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

const KINDS = ['events', 'changes', 'authz', 'access'] as const;
type Kind = (typeof KINDS)[number];
const KIND_LABELS: Record<Kind, string> = {
  events: 'Events',
  changes: 'Changes',
  authz: 'Access decisions',
  access: 'Request log',
};
type Row = components['schemas']['AuditRowResponse'];

export function AuditPage() {
  const [kind, setKind] = useState<Kind>('events');
  const [limit, setLimit] = useState(50);
  const [expanded, setExpanded] = useState<string | null>(null);
  const { data: rows } = useQuery({
    queryKey: ['audit', kind, limit],
    queryFn: ({ signal }) => api.get('/api/audit/{kind}', { path: { kind }, query: { limit }, signal }),
  });
  const exportTrail = useApiMutation({
    mutationFn: () => api.post('/api/audit/export'),
    success: 'Export queued - check Files shortly',
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

  const expandedRow = rows?.find((row) => row.id === expanded);
  const columns = useMemo<ColumnDef<DataGridFeatures, Row>[]>(
    () => [
      {
        id: 'when',
        accessorKey: 'occurredAt',
        header: 'When',
        cell: ({ row }) => (
          <span className="text-xs text-muted-foreground">{fmtDateTime(row.original.occurredAt)}</span>
        ),
        meta: { headerClassName: 'w-40' },
      },
      {
        id: 'actor',
        header: 'Actor',
        cell: ({ row }) => (
          <span className="block max-w-44 truncate text-xs" title={row.original.actorLabel ?? row.original.actorTier}>
            {row.original.actorLabel ?? row.original.actorTier}
          </span>
        ),
        meta: { headerClassName: 'w-44' },
      },
      {
        id: 'detail',
        header: 'Detail',
        cell: ({ row }) => (
          <span className={`block font-mono text-xs ${expanded === row.original.id ? 'whitespace-pre-wrap break-all' : 'max-w-xl truncate'}`}>
            {detail(row.original)}
          </span>
        ),
      },
    ],
    // `detail` reads `kind`; the expanded row widens its own cell
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [kind, expanded],
  );

  return (
    <div className="space-y-6">
      <PageHeader
        title="Audit"
        description="Events, changes, access decisions and the request log."
        actions={
          <Button variant="outline" size="sm" disabled={exportTrail.isPending} onClick={() => exportTrail.mutate()}>
            Export trail
          </Button>
        }
      />
      <div className="flex gap-2">
        {KINDS.map((k) => (
          <Button key={k} size="sm" variant={k === kind ? 'default' : 'outline'}
            onClick={() => {
              setKind(k);
              setLimit(50);
              setExpanded(null);
            }}>
            {KIND_LABELS[k]}
          </Button>
        ))}
      </div>
      <Grid
        columns={columns}
        rows={rows ?? []}
        getRowId={(row) => row.id}
        isLoading={rows === undefined}
        loadingMessage="Loading…"
        emptyMessage="Nothing recorded yet."
        onRowClick={(row) => setExpanded(expanded === row.id ? null : row.id)}
        footer={
          rows && rows.length >= limit && limit < 500 ? (
            <Button variant="outline" size="sm" onClick={() => setLimit(limit + 100)}>
              Load more
            </Button>
          ) : undefined
        }
      >
        {expandedRow && (
          <div className="border-t bg-muted/40 px-4 py-3">
            <pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all font-mono text-xs">
              {JSON.stringify(expandedRow, null, 2)}
            </pre>
          </div>
        )}
      </Grid>
    </div>
  );
}

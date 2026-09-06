import {
  Button,
  DataGrid,
  DataGridColumnVisibility,
  DataGridContainer,
  DataGridScrollArea,
  DataGridTable,
  DataGridTableRowSelect,
  DataGridTableRowSelectAll,
  dataGridFeatures,
  FormDialog,
  Frame,
  FrameFooter,
  FrameHeader,
  FramePanel,
  Input,
  Label,
  Select,
  TimeZoneSelect,
  useTable,
  type ColumnDef,
  type DataGridFeatures,
  type RowSelectionState,
} from '@premise/ui';
import { Link } from '@tanstack/react-router';
import { Columns3, Plus, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useScope } from '../../../app/scope';
import { useApiMutation } from '../../../lib/mutation';
import { can, useMe } from '../../../session';
import { StatusBadge } from '../../../shell';
import { sitesApi } from '../api';
import { useHierarchy, useSites } from '../hooks';

type SiteRow = ReturnType<typeof useSites>['data'] extends infer D
  ? D extends { pages: { items: (infer R)[] }[] }
    ? R
    : never
  : never;

/**
 * The site library, table view (direction B, step 4): a ReUI Frame holding
 * the Data Grid. Rows are the scoped, searched, offset-paged list the server
 * returns (ADR 49's chain); the grid never sorts or filters client-side,
 * because it only ever holds a page of the fleet. Selection is keyed by site
 * id, not row index, so it survives paging and - when the map view lands -
 * panning (ADR 49). The map toggle arrives with the MapLibre component.
 */
export function SitesPage() {
  const { data: me } = useMe();
  const [filter, setFilter] = useState('');
  const scope = useScope();
  const sitesQuery = useSites(filter, scope.nodeId);
  const sites = useMemo(
    () => sitesQuery.data?.pages.flatMap((p) => p.items) ?? [],
    [sitesQuery.data],
  );
  const first = sitesQuery.data?.pages[0]?.total;
  const total = first === undefined ? undefined : Number(first);
  const { data: hierarchy } = useHierarchy();
  const [rowSelection, setRowSelection] = useState<RowSelectionState>({});
  const selectedCount = Object.values(rowSelection).filter(Boolean).length;

  const nodeName = (id: string) => hierarchy?.nodes.find((n) => n.id === id)?.name ?? '—';

  const columns = useMemo<ColumnDef<DataGridFeatures, SiteRow>[]>(
    () => [
      {
        id: 'select',
        header: () => <DataGridTableRowSelectAll />,
        cell: ({ row }) => <DataGridTableRowSelect row={row} />,
        size: 40,
        enableHiding: false,
        enableResizing: false,
        meta: { headerClassName: 'w-10', cellClassName: 'w-10' },
      },
      {
        id: 'name',
        accessorKey: 'name',
        header: 'Name',
        cell: ({ row }) => (
          <div className="min-w-0">
            <Link
              to="/sites/$siteId"
              params={{ siteId: row.original.id }}
              className="font-medium text-foreground underline-offset-4 hover:underline"
            >
              {row.original.name}
            </Link>
            {row.original.city && (
              <div className="truncate text-xs text-muted-foreground">{row.original.city}</div>
            )}
          </div>
        ),
        size: 260,
        enableHiding: false,
        meta: { headerTitle: 'Name', fillWidth: true },
      },
      {
        id: 'node',
        accessorKey: 'nodeId',
        header: 'Hierarchy node',
        cell: ({ row }) => (
          <span className="text-muted-foreground">{nodeName(row.original.nodeId)}</span>
        ),
        size: 180,
        meta: { headerTitle: 'Hierarchy node' },
      },
      {
        id: 'timeZone',
        accessorKey: 'timeZone',
        header: 'Time zone',
        cell: ({ row }) => (
          <span className="font-mono text-xs text-muted-foreground">{row.original.timeZone}</span>
        ),
        size: 180,
        meta: { headerTitle: 'Time zone' },
      },
      {
        id: 'status',
        accessorKey: 'status',
        header: 'Status',
        cell: ({ row }) => <StatusBadge status={row.original.status} />,
        size: 120,
        meta: { headerTitle: 'Status' },
      },
    ],
    // nodeName reads the hierarchy query; columns rebuild when it arrives
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [hierarchy],
  );

  const table = useTable({
    features: dataGridFeatures,
    columns,
    data: sites,
    getRowId: (row: SiteRow) => row.id,
    state: { rowSelection },
    onRowSelectionChange: setRowSelection,
    enableRowSelection: true,
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
    pageCount: -1,
  });

  if (sitesQuery.isError)
    return <p className="text-sm text-destructive">Could not load sites.</p>;

  const manage = can(me, 'sites:manage');
  const loaded = sites.length;
  const emptyMessage = filter
    ? 'No sites match the search.'
    : `No sites in scope. ${manage ? 'Create one with "New site".' : ''}`;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            Sites
            {total !== undefined && (
              <span className="ml-2 text-base font-medium tabular-nums text-muted-foreground">
                {total}
              </span>
            )}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Every physical location in your scope, on its own clock.
          </p>
        </div>
        {manage && <NewSiteDialog />}
      </div>

      <Frame>
        <FramePanel>
          {/* the header toolbar and the footer read the grid context (column
              visibility, selection), so the grid wraps the whole panel */}
          <DataGrid
            table={table}
            recordCount={total ?? loaded}
            isLoading={sitesQuery.isPending}
            loadingMode="skeleton"
            loadingMessage="Loading sites…"
            emptyMessage={emptyMessage}
            tableLayout={{
              headerBackground: false,
              headerBorder: true,
              rowBorder: true,
              columnsVisibility: true,
            }}
          >
          <FrameHeader className="flex-row flex-wrap items-center gap-2">
            <div className="relative min-w-0 flex-1 sm:max-w-xs">
              <Search
                className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                aria-hidden
              />
              <Input
                className="pl-8"
                aria-label="Search sites"
                placeholder="Search name or city…"
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
              />
            </div>
            <DataGridColumnVisibility
              table={table}
              trigger={
                <Button variant="outline" size="sm" className="ml-auto">
                  <Columns3 className="size-4" aria-hidden />
                  Columns
                </Button>
              }
            />
          </FrameHeader>
            <DataGridContainer>
              <DataGridScrollArea>
                <DataGridTable />
              </DataGridScrollArea>
            </DataGridContainer>
          <FrameFooter className="flex-wrap gap-3">
            <div className="flex items-center gap-2 text-sm" role="status">
              {sitesQuery.isPending ? (
                <span className="text-muted-foreground">Loading sites…</span>
              ) : selectedCount > 0 ? (
                <>
                  <span className="font-medium tabular-nums">{selectedCount} selected</span>
                  <Button variant="ghost" size="sm" onClick={() => setRowSelection({})}>
                    Clear
                  </Button>
                </>
              ) : (
                <span className="text-muted-foreground">
                  {total === undefined ? '' : `Showing ${Math.min(loaded, total)} of ${total}`}
                </span>
              )}
            </div>
            {sitesQuery.hasNextPage && (
              <Button
                variant="outline"
                size="sm"
                disabled={sitesQuery.isFetchingNextPage}
                onClick={() => void sitesQuery.fetchNextPage()}
              >
                Load more ({loaded} of {total})
              </Button>
            )}
          </FrameFooter>
          </DataGrid>
        </FramePanel>
      </Frame>
    </div>
  );
}

function NewSiteDialog() {
  const { data: hierarchy } = useHierarchy();
  const [name, setName] = useState('');
  const [timeZone, setTimeZone] = useState('America/New_York');
  const [nodeId, setNodeId] = useState('');
  const [creating, setCreating] = useState(false);

  const create = useApiMutation({
    mutationFn: () => sitesApi.create({ nodeId, name, timeZone }),
    invalidate: [['sites']],
    success: 'Site created',
    onSuccess: () => {
      setName('');
      setCreating(false);
    },
  });

  return (
    <FormDialog
      open={creating}
      onOpenChange={setCreating}
      trigger={
        <Button>
          <Plus className="size-4" aria-hidden />
          New site
        </Button>
      }
      title="New site"
      description="A site is a physical location, placed on a hierarchy node."
    >
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="site-name">Name</Label>
          <Input id="site-name" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="site-tz">Time zone</Label>
          <TimeZoneSelect
            id="site-tz"
            value={timeZone}
            onChange={(e) => setTimeZone(e.target.value)}
          />
        </div>
        <div className="space-y-1">
          <Label htmlFor="site-node">Hierarchy node</Label>
          <Select id="site-node" value={nodeId} onChange={(e) => setNodeId(e.target.value)}>
            <option value="">Choose…</option>
            {hierarchy?.nodes.map((n) => (
              <option key={n.id} value={n.id}>
                {' '.repeat(Number(n.depth) * 2)}
                {n.name}
              </option>
            ))}
          </Select>
        </div>
        <Button
          className="w-full"
          disabled={!name || !nodeId || create.isPending}
          onClick={() => create.mutate()}
        >
          Create site
        </Button>
      </div>
    </FormDialog>
  );
}

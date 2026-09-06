import {
  Button,
  Checkbox,
  Popover,
  PopoverContent,
  PopoverTrigger,
  cn,
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
  SiteMap,
  TimeZoneSelect,
  useTable,
  type ColumnDef,
  type DataGridFeatures,
  type RowSelectionState,
  type SiteMapOverlay,
  type SiteMapPoint,
} from '@premise/ui';
import { Link } from '@tanstack/react-router';
import { Columns3, Layers, MapPin, Plus, Search, Table2 } from 'lucide-react';
import { useCallback, useMemo, useState } from 'react';
import { useScope } from '../../../app/scope';
import { useTheme } from '../../../app/theme';
import { bboxParam, snapToTileGrid, type Viewport } from '../../../lib/map';
import { useApiMutation } from '../../../lib/mutation';
import { can, useMe } from '../../../session';
import { StatusBadge } from '../../../shell';
import { overlaysApi } from '../../overlays/api';
import { useOverlays } from '../../overlays/hooks';
import { sitesApi } from '../api';
import { useBasemaps, useHierarchy, useSites } from '../hooks';

type SiteRow = ReturnType<typeof useSites>['data'] extends infer D
  ? D extends { pages: { items: (infer R)[] }[] }
    ? R
    : never
  : never;

type View = 'table' | 'map';
const VIEW_KEY = 'premise.sites.view';
const HIDDEN_OVERLAYS_KEY = 'premise.sites.overlays.hidden';
const BASEMAP_KEY = 'premise.map.basemap';

function readBasemapChoice(): string {
  try {
    return localStorage.getItem(BASEMAP_KEY) ?? 'auto';
  } catch {
    return 'auto';
  }
}

function readHiddenOverlays(): Set<string> {
  try {
    const raw = localStorage.getItem(HIDDEN_OVERLAYS_KEY);
    return new Set(raw ? (JSON.parse(raw) as string[]) : []);
  } catch {
    return new Set();
  }
}

const readView = (): View => {
  try {
    return localStorage.getItem(VIEW_KEY) === 'map' ? 'map' : 'table';
  } catch {
    return 'table';
  }
};

const toNumber = (v: unknown): number | null =>
  v === null || v === undefined || v === '' ? null : Number(v);

/**
 * The site library (direction B, steps 4-5): one scoped, searched list in two
 * views. Table: a ReUI Frame holding the Data Grid. Map: the same frame with
 * a MapLibre map and a list of what is INSIDE the viewport - the box is sent
 * to the server (ADR 49), snapped to the tile grid and debounced, and the
 * server applies scope first. Selection is keyed by site id and shared by
 * both views, so it survives paging, panning, and the toggle.
 */
export function SitesPage() {
  const { data: me } = useMe();
  const [filter, setFilter] = useState('');
  const [view, setViewState] = useState<View>(readView);
  const [viewport, setViewport] = useState<Viewport | null>(null);
  const [fitKey, setFitKey] = useState(0);
  const scope = useScope();
  const theme = useTheme();

  const setView = (next: View) => {
    setViewState(next);
    try {
      localStorage.setItem(VIEW_KEY, next);
    } catch {
      // a preference; losing it costs one click
    }
  };

  // the box only exists in the map view: the table is the whole scope
  const box = view === 'map' && viewport ? snapToTileGrid(viewport) : null;
  // the map draws the API's sites tiles (ADR 50 §4): scope and clustering are
  // the server's; the console's chosen node rides along as `under`
  const tiles = useMemo(
    () => ({
      url: `${window.location.origin}/api/tiles/sites/{z}/{x}/{y}${
        scope.nodeId ? `?under=${encodeURIComponent(scope.nodeId)}` : ''
      }`,
      sourceLayer: 'sites',
    }),
    [scope.nodeId],
  );
  // the org's overlays (ADR 50 §3) ride under the sites; each is its own
  // tile source, and the viewer decides which are showing
  const canSeeOverlays = can(me, 'overlays:read');
  const overlaysQuery = useOverlays(canSeeOverlays && view === 'map');
  const [hiddenOverlays, setHiddenOverlays] = useState(readHiddenOverlays);
  const toggleOverlay = useCallback((id: string) => {
    setHiddenOverlays((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      try {
        localStorage.setItem(HIDDEN_OVERLAYS_KEY, JSON.stringify([...next]));
      } catch {
        // storage is a convenience
      }
      return next;
    });
  }, []);
  // the basemap: the theme's own by default, OpenStreetMap, or one the org configured
  const basemapsQuery = useBasemaps(view === 'map');
  const rasters = useMemo(
    () => (basemapsQuery.data?.basemaps ?? []).map((r) => ({ ...r, maxZoom: Number(r.maxZoom) })),
    [basemapsQuery.data],
  );
  const [basemapChoice, setBasemapChoice] = useState(readBasemapChoice);
  const chooseBasemap = useCallback((id: string) => {
    setBasemapChoice(id);
    try {
      localStorage.setItem(BASEMAP_KEY, id);
    } catch {
      // storage is a convenience
    }
  }, []);
  const knownChoice =
    basemapChoice === 'auto' ||
    basemapChoice === 'osm' ||
    !basemapsQuery.data ||
    rasters.some((r) => r.id === basemapChoice);
  const basemap =
    basemapChoice === 'auto' || !knownChoice ? (theme === 'dark' ? 'dark' : 'light') : basemapChoice;
  const basemapChoices = [
    { id: 'auto', name: 'Match theme' },
    { id: 'osm', name: 'OpenStreetMap' },
    ...rasters.map((r) => ({ id: r.id, name: r.name })),
  ];
  const overlayLayers = useMemo(
    () => (overlaysQuery.data?.layers ?? []).filter((l) => Number(l.featureCount) > 0),
    [overlaysQuery.data],
  );
  const overlays = useMemo<SiteMapOverlay[]>(
    () =>
      overlayLayers
        .filter((l) => !hiddenOverlays.has(l.id))
        .map((l) => ({
          id: l.id,
          url: overlaysApi.tiles(l.id),
          sourceLayer: 'overlay',
          style: (l.style ?? null) as SiteMapOverlay['style'],
        })),
    [overlayLayers, hiddenOverlays],
  );
  const sitesQuery = useSites(
    filter,
    scope.nodeId,
    box ? bboxParam(box) : undefined,
    box && viewport ? Math.max(0, Math.floor(viewport.zoom)) : undefined,
  );
  const sites = useMemo(
    () => sitesQuery.data?.pages.flatMap((p) => p.items) ?? [],
    [sitesQuery.data],
  );
  const firstPage = sitesQuery.data?.pages[0];
  const total = firstPage === undefined ? undefined : Number(firstPage.total);
  const withoutCoordinates = toNumber(firstPage?.withoutCoordinates);
  const { data: hierarchy } = useHierarchy();
  const [rowSelection, setRowSelection] = useState<RowSelectionState>({});
  const selectedCount = Object.values(rowSelection).filter(Boolean).length;
  const selectedIds = useMemo(
    () => Object.keys(rowSelection).filter((id) => rowSelection[id]),
    [rowSelection],
  );
  const toggleSelected = useCallback(
    (id: string) =>
      setRowSelection((current) => {
        const next = { ...current };
        if (next[id]) delete next[id];
        else next[id] = true;
        return next;
      }),
    [],
  );

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

  const points = useMemo<SiteMapPoint[]>(
    () =>
      sites.flatMap((s) => {
        const latitude = toNumber(s.latitude);
        const longitude = toNumber(s.longitude);
        if (latitude === null || longitude === null) return [];
        return [
          {
            id: s.id,
            name: s.name,
            latitude,
            longitude,
            subtitle: s.city ?? undefined,
            selected: !!rowSelection[s.id],
          },
        ];
      }),
    [sites, rowSelection],
  );

  if (sitesQuery.isError)
    return <p className="text-sm text-destructive">Could not load sites.</p>;

  const manage = can(me, 'sites:manage');
  const loaded = sites.length;
  const emptyMessage = filter
    ? 'No sites match the search.'
    : `No sites in scope. ${manage ? 'Create one with "New site".' : ''}`;

  const search = (
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
  );

  const selection =
    selectedCount > 0 ? (
      <>
        <span className="font-medium tabular-nums">{selectedCount} selected</span>
        <Button variant="ghost" size="sm" onClick={() => setRowSelection({})}>
          Clear
        </Button>
      </>
    ) : null;

  const loadMore = sitesQuery.hasNextPage && (
    <Button
      variant="outline"
      size="sm"
      disabled={sitesQuery.isFetchingNextPage}
      onClick={() => void sitesQuery.fetchNextPage()}
    >
      Load more ({loaded} of {total})
    </Button>
  );

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            Sites
            {view === 'table' && total !== undefined && (
              <span className="ml-2 text-base font-medium tabular-nums text-muted-foreground">
                {total}
              </span>
            )}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Every physical location in your scope, on its own clock.
          </p>
        </div>
        <div className="flex items-center gap-3">
          {/* the toggle swaps the whole surface, so it sits with the page actions, not in the toolbar */}
          <div
            role="group"
            aria-label="View"
            className="inline-flex gap-0.5 rounded-lg border bg-background p-0.5"
          >
            {(
              [
                ['table', 'Table', Table2],
                ['map', 'Map', MapPin],
              ] as const
            ).map(([key, label, Icon]) => (
              <button
                key={key}
                type="button"
                aria-pressed={view === key}
                onClick={() => setView(key)}
                className={cn(
                  'inline-flex h-7 items-center gap-1.5 rounded-md px-2.5 text-xs font-medium text-muted-foreground',
                  view === key && 'bg-muted text-foreground shadow-xs',
                )}
              >
                <Icon className="size-3.5" aria-hidden />
                {label}
              </button>
            ))}
          </div>
          {manage && <NewSiteDialog />}
        </div>
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
              {search}
              {view === 'table' && (
                <DataGridColumnVisibility
                  table={table}
                  trigger={
                    <Button variant="outline" size="sm" className="ml-auto">
                      <Columns3 className="size-4" aria-hidden />
                      Columns
                    </Button>
                  }
                />
              )}
              {view === 'map' && canSeeOverlays && (
                <Popover>
                  <PopoverTrigger
                    render={
                      <Button variant="outline" size="sm" className="ml-auto">
                        <Layers className="size-4" aria-hidden />
                        Layers
                        {overlays.length > 0 && (
                          <span className="text-muted-foreground">{overlays.length}</span>
                        )}
                      </Button>
                    }
                  />
                  <PopoverContent align="end" className="w-64">
                    <fieldset className="flex flex-col gap-0.5">
                      <legend className="px-1 pb-1 text-xs font-medium text-muted-foreground">Basemap</legend>
                      {basemapChoices.map((c) => (
                        <label
                          key={c.id}
                          className="flex cursor-pointer items-center gap-2 rounded-md px-1 py-1 text-sm hover:bg-muted"
                        >
                          <input
                            type="radio"
                            name="basemap"
                            className="size-3.5 accent-primary"
                            checked={(knownChoice ? basemapChoice : 'auto') === c.id}
                            onChange={() => chooseBasemap(c.id)}
                          />
                          <span className="truncate">{c.name}</span>
                        </label>
                      ))}
                    </fieldset>
                    <p className="px-1 pt-1 text-xs font-medium text-muted-foreground">Overlays</p>
                    {overlayLayers.length === 0 ? (
                      <p className="px-1 text-sm text-muted-foreground">
                        No overlay layers with shapes yet.
                      </p>
                    ) : (
                      <ul className="flex flex-col gap-1">
                        {overlayLayers.map((l) => (
                          <li key={l.id}>
                            <label className="flex cursor-pointer items-center gap-2 rounded-md px-1 py-1 text-sm hover:bg-muted">
                              <Checkbox
                                checked={!hiddenOverlays.has(l.id)}
                                onCheckedChange={() => toggleOverlay(l.id)}
                              />
                              <span
                                className="size-3 shrink-0 rounded-sm border"
                                style={{
                                  background: (l.style as { fill?: string } | null)?.fill ?? '#7c6cf0',
                                }}
                                aria-hidden
                              />
                              <span className="truncate">{l.name}</span>
                              <span className="ml-auto text-xs text-muted-foreground">{l.kind}</span>
                            </label>
                          </li>
                        ))}
                      </ul>
                    )}
                  </PopoverContent>
                </Popover>
              )}
            </FrameHeader>

            {view === 'table' ? (
              <DataGridContainer>
                <DataGridScrollArea>
                  <DataGridTable />
                </DataGridScrollArea>
              </DataGridContainer>
            ) : (
              <div className="grid md:grid-cols-[300px_minmax(0,1fr)]">
                {/* the accessible equivalent of the map: what is in view, with the same selection */}
                <div className="order-2 max-h-[360px] overflow-auto border-t md:order-1 md:max-h-[560px] md:border-r md:border-t-0">
                  <div className="flex items-center justify-between border-b px-3 py-2 text-xs text-muted-foreground">
                    <span>
                      <b className="font-medium text-foreground tabular-nums">{loaded} in view</b>
                      {total !== undefined && loaded < total && ` of ${total}`}
                    </span>
                    <span>List follows the map</span>
                  </div>
                  <ul aria-label="Sites in view">
                    {sites.map((s, i) => {
                      const selected = !!rowSelection[s.id];
                      return (
                        <li
                          key={s.id}
                          className={cn(
                            'flex items-center gap-2.5 border-b px-3 py-2',
                            selected && 'bg-accent/50',
                          )}
                        >
                          <Checkbox
                            checked={selected}
                            onCheckedChange={() => toggleSelected(s.id)}
                            aria-label={`Select ${s.name}`}
                          />
                          <span
                            className={cn(
                              'flex size-5 shrink-0 items-center justify-center rounded-full border text-[11px] font-semibold tabular-nums',
                              selected
                                ? 'border-primary bg-primary text-primary-foreground'
                                : 'bg-muted text-muted-foreground',
                            )}
                            aria-hidden
                          >
                            {i + 1}
                          </span>
                          <span className="min-w-0 flex-1 leading-tight">
                            <Link
                              to="/sites/$siteId"
                              params={{ siteId: s.id }}
                              className="block truncate text-sm font-medium hover:underline"
                            >
                              {s.name}
                            </Link>
                            {s.city && (
                              <span className="block truncate text-xs text-muted-foreground">
                                {s.city}
                              </span>
                            )}
                          </span>
                          <StatusBadge status={s.status} />
                        </li>
                      );
                    })}
                    {!sitesQuery.isPending && sites.length === 0 && (
                      <li className="px-3 py-6 text-center text-sm text-muted-foreground">
                        No sites in this part of the map.
                      </li>
                    )}
                  </ul>
                  {withoutCoordinates !== null && withoutCoordinates > 0 && (
                    <p className="px-3 py-3 text-xs text-muted-foreground">
                      {withoutCoordinates} in scope without coordinates, so never on the map.
                    </p>
                  )}
                </div>
                <SiteMap
                  className="order-1 h-[360px] md:order-2 md:h-[560px]"
                  points={points}
                  tiles={tiles}
                  selectedIds={selectedIds}
                  overlays={overlays}
                  basemap={basemap}
                  basemaps={rasters}
                  fitKey={fitKey}
                  onViewportChange={setViewport}
                  onPointClick={toggleSelected}
                />
              </div>
            )}

            <FrameFooter className="flex-wrap gap-3">
              <div className="flex items-center gap-2 text-sm" role="status">
                {sitesQuery.isPending ? (
                  <span className="text-muted-foreground">Loading sites…</span>
                ) : (
                  selection ?? (
                    <span className="text-muted-foreground">
                      {total === undefined
                        ? ''
                        : view === 'map'
                          ? `${loaded} in view`
                          : `Showing ${Math.min(loaded, total)} of ${total}`}
                    </span>
                  )
                )}
              </div>
              <div className="flex items-center gap-2">
                {view === 'map' && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      // drop the box (the whole scope loads), then fit the map to it
                      setViewport(null);
                      setFitKey((k) => k + 1);
                    }}
                  >
                    Fit to scope
                  </Button>
                )}
                {loadMore}
              </div>
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

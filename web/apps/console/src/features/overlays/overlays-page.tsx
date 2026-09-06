import {
  Button,
  buttonVariants,
  ConfirmButton,
  FormDialog,
  Frame,
  FrameFooter,
  FrameHeader,
  FramePanel,
  Input,
  Label,
  Select,
  Textarea,
  cn,
  toast,
} from '@premise/ui';
import { Link } from '@tanstack/react-router';
import { FileUp, Layers, MapPin, Plus, RotateCcw } from 'lucide-react';
import { useState } from 'react';
import { useApiMutation } from '../../lib/mutation';
import { can, useMe } from '../../session';
import { useHierarchy } from '../sites/hooks';
import { overlaysApi, type OverlayLayer } from './api';
import { useOverlays } from './hooks';

const KINDS = ['territory', 'zone', 'region', 'trade area'] as const;
const DEFAULT_FILL = '#7c6cf0';

const fillOf = (layer: OverlayLayer) =>
  (layer.style as { fill?: string } | null | undefined)?.fill ?? DEFAULT_FILL;

/**
 * The org's overlays (ADR 50 §3): territories, zones, regions - shapes the
 * org owns and edits, drawn under the sites on the map and filtered by scope
 * like everything else. The first version takes GeoJSON uploads of polygons;
 * drawing in place comes later over the same endpoint.
 */
export function OverlaysPage() {
  const { data: me } = useMe();
  const manage = can(me, 'overlays:manage');
  const [tab, setTab] = useState<'active' | 'trash'>('active');
  const query = useOverlays(true, tab === 'trash');
  const layers = query.data?.layers ?? [];

  const remove = useApiMutation({
    mutationFn: (id: string) => overlaysApi.remove(id),
    invalidate: [['overlays']],
    success: 'Layer moved to the trash',
  });
  const restore = useApiMutation({
    mutationFn: (id: string) => overlaysApi.restore(id),
    invalidate: [['overlays']],
    success: 'Layer restored',
  });

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Overlays</h1>
          <p className="text-sm text-muted-foreground">
            Your own shapes on the map, drawn under the sites and filtered by scope.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Link to="/sites" className={buttonVariants({ variant: 'outline' })}>
            <MapPin className="size-4" aria-hidden />
            See the map
          </Link>
          {manage && <NewLayerDialog />}
        </div>
      </div>

      <Frame>
        <FramePanel>
          <FrameHeader className="flex-row items-center gap-2">
            <div role="group" aria-label="Layers" className="inline-flex gap-0.5 rounded-lg border bg-background p-0.5">
              {(
                [
                  ['active', 'Active'],
                  ['trash', 'Trash'],
                ] as const
              ).map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  aria-pressed={tab === key}
                  onClick={() => setTab(key)}
                  className={cn(
                    'inline-flex h-7 items-center rounded-md px-2.5 text-xs font-medium text-muted-foreground',
                    tab === key && 'bg-muted text-foreground shadow-xs',
                  )}
                >
                  {label}
                </button>
              ))}
            </div>
          </FrameHeader>

          {query.isPending && (
            <p role="status" className="px-4 py-6 text-sm text-muted-foreground">
              Loading layers…
            </p>
          )}
          {query.isError && (
            <div role="alert" className="px-4 py-6 text-sm">
              Could not load layers.{' '}
              <Button variant="outline" size="sm" onClick={() => void query.refetch()}>
                Retry
              </Button>
            </div>
          )}
          {query.data && layers.length === 0 && (
            <div className="flex flex-col items-center gap-2 px-4 py-12 text-center">
              <Layers className="size-6 text-muted-foreground" aria-hidden />
              <p className="text-sm text-muted-foreground">
                {tab === 'trash'
                  ? 'The trash is empty.'
                  : manage
                    ? 'No overlay layers yet. Create one, then upload its shapes as GeoJSON.'
                    : 'No overlay layers in your scope yet.'}
              </p>
            </div>
          )}
          {layers.length > 0 && (
            <ul className="divide-y">
              {layers.map((layer) => {
                const count = Number(layer.featureCount);
                return (
                  <li key={layer.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
                    <span
                      className="size-4 shrink-0 rounded-sm border"
                      style={{ background: fillOf(layer) }}
                      aria-hidden
                    />
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-sm font-medium">{layer.name}</div>
                      <div className="text-xs text-muted-foreground">
                        {layer.kind} · {count === 0 ? 'no shapes yet' : `${count} shape${count === 1 ? '' : 's'}`}
                        {' · '}updated {new Date(layer.updatedAt).toLocaleDateString()}
                      </div>
                    </div>
                    {manage && tab === 'active' && (
                      <div className="flex items-center gap-1">
                        <UploadShapesDialog layer={layer} />
                        <ConfirmButton
                          size="sm"
                          confirmLabel="Move to trash?"
                          onConfirm={() => remove.mutate(layer.id)}
                          disabled={remove.isPending}
                        >
                          Delete
                        </ConfirmButton>
                      </div>
                    )}
                    {manage && tab === 'trash' && (
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={restore.isPending}
                        onClick={() => restore.mutate(layer.id)}
                      >
                        <RotateCcw className="size-4" aria-hidden />
                        Restore
                      </Button>
                    )}
                  </li>
                );
              })}
            </ul>
          )}

          <FrameFooter>
            <span className="text-sm text-muted-foreground" role="status">
              {query.data ? `${layers.length} ${tab === 'trash' ? 'in the trash' : 'in scope'}` : ''}
            </span>
          </FrameFooter>
        </FramePanel>
      </Frame>
    </div>
  );
}

function NewLayerDialog() {
  const { data: hierarchy } = useHierarchy();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [kind, setKind] = useState<string>(KINDS[0]);
  const [fill, setFill] = useState(DEFAULT_FILL);
  const [nodeId, setNodeId] = useState('');

  const create = useApiMutation({
    mutationFn: () =>
      overlaysApi.create({
        name,
        kind,
        style: { fill, opacity: 0.2 },
        nodeId: nodeId || null,
      }),
    invalidate: [['overlays']],
    success: 'Layer created - upload its shapes next',
    onSuccess: () => {
      setName('');
      setOpen(false);
    },
  });

  return (
    <FormDialog
      open={open}
      onOpenChange={setOpen}
      trigger={
        <Button>
          <Plus className="size-4" aria-hidden />
          New layer
        </Button>
      }
      title="New overlay layer"
      description="A layer holds shapes of one kind. Anchor it to a hierarchy node and only that subtree's scope sees it."
    >
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="overlay-name">Name</Label>
          <Input id="overlay-name" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="grid grid-cols-[1fr_auto] gap-3">
          <div className="space-y-1">
            <Label htmlFor="overlay-kind">Kind</Label>
            <Select id="overlay-kind" value={kind} onChange={(e) => setKind(e.target.value)}>
              {KINDS.map((k) => (
                <option key={k} value={k}>
                  {k}
                </option>
              ))}
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="overlay-fill">Color</Label>
            <Input
              id="overlay-fill"
              type="color"
              className="h-9 w-14 p-1"
              value={fill}
              onChange={(e) => setFill(e.target.value)}
            />
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="overlay-node">Anchor</Label>
          <Select id="overlay-node" value={nodeId} onChange={(e) => setNodeId(e.target.value)}>
            <option value="">Whole organization</option>
            {hierarchy?.nodes.map((n) => (
              <option key={n.id} value={n.id}>
                {' '.repeat(Number(n.depth) * 2)}
                {n.name}
              </option>
            ))}
          </Select>
        </div>
        <Button className="w-full" disabled={!name.trim() || create.isPending} onClick={() => create.mutate()}>
          Create layer
        </Button>
      </div>
    </FormDialog>
  );
}

function UploadShapesDialog({ layer }: { layer: OverlayLayer }) {
  const [open, setOpen] = useState(false);
  const [text, setText] = useState('');
  const [fileName, setFileName] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const upload = useApiMutation({
    mutationFn: (geoJson: unknown) => overlaysApi.replaceFeatures(layer.id, geoJson),
    invalidate: [['overlays']],
    onSuccess: (result) => {
      setText('');
      setFileName(null);
      setOpen(false);
      const n = Number(result.count);
      // the success line carries the count, which the generic option cannot
      toast.success(`${n} shape${n === 1 ? '' : 's'} uploaded`);
    },
  });

  const submit = () => {
    setProblem(null);
    let parsed: unknown;
    try {
      parsed = JSON.parse(text);
    } catch {
      setProblem('That is not valid JSON.');
      return;
    }
    upload.mutate(parsed);
  };

  const pick = async (file: File | undefined) => {
    if (!file) return;
    setFileName(file.name);
    setText(await file.text());
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setProblem(null);
      }}
      trigger={
        <Button variant="outline" size="sm">
          <FileUp className="size-4" aria-hidden />
          Upload shapes
        </Button>
      }
      title={`Shapes for ${layer.name}`}
      description="A GeoJSON FeatureCollection of polygons. The upload replaces every shape the layer has; feature properties ride along to the map."
    >
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="overlay-file">GeoJSON file</Label>
          <Input
            id="overlay-file"
            type="file"
            accept=".geojson,.json,application/geo+json,application/json"
            onChange={(e) => void pick(e.target.files?.[0])}
          />
          {fileName && <p className="text-xs text-muted-foreground">{fileName}</p>}
        </div>
        <div className="space-y-1">
          <Label htmlFor="overlay-geojson">Or paste it</Label>
          <Textarea
            id="overlay-geojson"
            rows={6}
            className="font-mono text-xs"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder='{"type":"FeatureCollection","features":[…]}'
          />
        </div>
        {problem && (
          <p role="alert" className="text-sm text-destructive">
            {problem}
          </p>
        )}
        <Button className="w-full" disabled={!text.trim() || upload.isPending} onClick={submit}>
          {upload.isPending ? 'Uploading…' : 'Replace shapes'}
        </Button>
      </div>
    </FormDialog>
  );
}

import { Button, Input, Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@premise/ui';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useRef, useState } from 'react';
import { useApiMutation } from '../../lib/mutation';
import { can, useMe } from '../../session';
import { reportsApi, type ReportSubmission } from './api';

const inputStyle = 'rounded-md border bg-background px-3 py-2 text-sm';

export function GenerateSiteReports({ sites }: { sites: Record<string, string> }) {
  const { data: me } = useMe();
  const [captured, setCaptured] = useState<Record<string, string>>();
  if (!can(me, 'reports:generate')) return null;
  return <>
    <Button disabled={Object.keys(sites).length === 0} onClick={() => setCaptured({ ...sites })}>Generate report</Button>
    <ReportGenerationDialog sites={captured} onClose={() => setCaptured(undefined)} />
  </>;
}

export function ReportGenerationDialog({ sites, onClose }: { sites?: Record<string, string>; onClose: () => void }) {
  const navigate = useNavigate();
  return <Dialog open={!!sites} onOpenChange={open => { if (!open) onClose(); }}>
      <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader><DialogTitle>Generate site reports</DialogTitle>
          <DialogDescription>Choose the report for the sites selected in the library.</DialogDescription></DialogHeader>
        {sites && <ReportRequestForm selectedSites={sites} onCreated={id => {
          onClose(); void navigate({ to: '/reports/$runId', params: { runId: id } });
        }} />}
      </DialogContent>
    </Dialog>;
}

export function ReportRequestForm({ selectedSites, onCreated }: { selectedSites: Record<string, string>; onCreated: (id: string) => void }) {
  const { data: me } = useMe();
  const canReadFiles = can(me, 'files:read');
  const canReadOverlays = can(me, 'overlays:read');
  const allowance = useQuery({ queryKey: ['reports', 'quota'], queryFn: ({ signal }) => reportsApi.quota(signal) });
  const quota = allowance.data;
  const types = useQuery({ queryKey: ['reports', 'types'], queryFn: ({ signal }) => reportsApi.types(signal) });
  const basemaps = useQuery({ queryKey: ['reports', 'basemaps'], queryFn: ({ signal }) => reportsApi.basemaps(signal) });
  const overlays = useQuery({ queryKey: ['reports', 'overlays'], queryFn: ({ signal }) => reportsApi.overlays(signal), enabled: canReadOverlays });
  const [typeId, setTypeId] = useState('site');
  const type = types.data?.find(x => x.id === typeId);
  const defaultMode = Object.keys(selectedSites).length === 1 ? 'single' : 'bulk';
  const [mode, setMode] = useState(defaultMode);
  const [provider, setProvider] = useState('');
  const [zoom, setZoom] = useState(14);
  const [overlayIds, setOverlayIds] = useState<string[]>([]);
  const [photos, setPhotos] = useState<Record<string, string[]>>({});
  const [customOptions, setCustomOptions] = useState('{}');
  const [validation, setValidation] = useState('');
  const lastRequest = useRef<{ body: string; key: string } | undefined>(undefined);
  const files = useInfiniteQuery({ queryKey: ['reports', 'photos'],
    queryFn: ({ pageParam, signal }) => reportsApi.files(pageParam, signal), initialPageParam: 0,
    getNextPageParam: last => last.nextOffset ?? undefined, enabled: canReadFiles && !type?.aggregate });
  const submit = useApiMutation({ mutationFn: (body: ReportSubmission) => {
    const serialized = JSON.stringify(body);
    if (lastRequest.current?.body !== serialized) lastRequest.current = { body: serialized, key: crypto.randomUUID() };
    return reportsApi.submit(body, lastRequest.current.key);
  }, invalidate: [['reports', 'list'], ['reports', 'quota']], success: 'Report request accepted', onSuccess: result => { lastRequest.current = undefined; onCreated(result.id); } });
  const reference = typeId === 'site' || typeId === 'sites-summary';
  return <>
    <form className="space-y-4" onSubmit={event => {
      event.preventDefault(); setValidation('');
      try {
        const options: unknown = reference ? { ...(provider ? { mapProvider: provider } : {}), mapZoom: zoom, overlayIds,
          ...(type?.aggregate ? {} : { photos: Object.fromEntries(Object.entries(photos).filter(([id]) => id in selectedSites)) }) } : JSON.parse(customOptions);
        if (!type) throw new Error('Choose an available report type.');
        if (!options || typeof options !== 'object' || Array.isArray(options)) throw new Error('Report options must be an object.');
        if (!type.aggregate && mode === 'single' && Object.keys(selectedSites).length !== 1) throw new Error('Select exactly one site for a single report.');
        if (Object.keys(selectedSites).length === 0) throw new Error('Select at least one site.');
        submit.mutate({ reportType: typeId, mode: type.aggregate ? 'aggregate' : mode, selection: 'selected',
          siteIds: Object.keys(selectedSites), options });
      } catch (error) { setValidation(error instanceof Error ? error.message : 'Check the report options.'); }
    }}>
      <div className="flex flex-wrap gap-4">
        <label className="grid gap-1">Report type<select className={inputStyle} value={typeId} onChange={e => {
          setTypeId(e.target.value); setMode(types.data?.find(t => t.id === e.target.value)?.aggregate ? 'aggregate' : defaultMode);
        }}>{types.data?.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
        {!type?.aggregate && <label className="grid gap-1">Output<select className={inputStyle} value={mode} onChange={e => setMode(e.target.value)}>
          <option value="single" disabled={Object.keys(selectedSites).length !== 1}>One site PDF</option><option value="bulk">Separate PDFs and ZIP</option>
        </select></label>}
      </div>
      <fieldset><legend>Selected sites ({Object.keys(selectedSites).length})</legend>
        <ul>{Object.entries(selectedSites).map(([id, name]) => <li key={id}>{name}</li>)}</ul>
      </fieldset>
      <p className="text-sm text-muted-foreground">This run includes the sites selected in the library. Oversized requests are rejected as a whole.</p>
      {allowance.isPending && <p role="status">Loading report allowance…</p>}
      {allowance.error && <p role="alert">Report allowance could not load. Try reopening this dialog.</p>}
      {quota && <p>{quota.enabled ? `${quota.remaining} PDFs available this UTC month` : 'Report generation is not included in your organization’s plan.'}</p>}
      {reference ? <>
        <div className="flex flex-wrap gap-4"><label className="grid gap-1">Basemap<select className={inputStyle} value={provider} onChange={e => setProvider(e.target.value)}>
          <option value="">No basemap (report includes a warning)</option>{basemaps.data?.map(b => <option key={b.id} value={b.id}>{b.id}</option>)}
        </select></label><label className="grid gap-1">Maximum map zoom<Input type="number" min={0} max={18} required value={zoom} onChange={e => setZoom(Number(e.target.value))} /></label></div>
        {provider && <p className="text-sm">{basemaps.data?.find(b => b.id === provider)?.attribution}</p>}
        {canReadOverlays && <fieldset><legend>Map overlays (up to 10)</legend>{overlays.data?.layers.map(layer => <label key={layer.id} className="flex gap-2">
          <input type="checkbox" checked={overlayIds.includes(layer.id)} disabled={!overlayIds.includes(layer.id) && overlayIds.length >= 10} onChange={e => setOverlayIds(previous => e.target.checked ? [...previous, layer.id] : previous.filter(id => id !== layer.id))} />{layer.name}
        </label>)}</fieldset>}
        {!type?.aggregate && canReadFiles && Object.entries(selectedSites).map(([siteId, name]) => <fieldset key={siteId}>
          <legend>Photographs for {name} (up to 10)</legend>
          <div className="max-h-36 overflow-auto">{files.data?.pages.flatMap(p => p.items).filter(f => f.status === 'Clean' && ['image/jpeg', 'image/png'].includes(f.contentType)).map(file => {
            const chosen = photos[siteId] ?? [];
            return <label key={file.id} className="flex gap-2"><input type="checkbox" checked={chosen.includes(file.id)} disabled={!chosen.includes(file.id) && chosen.length >= 10} onChange={e => setPhotos(previous => ({ ...previous, [siteId]: e.target.checked ? [...chosen, file.id] : chosen.filter(id => id !== file.id) }))} />{file.name}</label>;
          })}</div>
        </fieldset>)}
        {canReadFiles && files.hasNextPage && <Button type="button" variant="outline" disabled={files.isFetchingNextPage} onClick={() => void files.fetchNextPage()}>More photographs</Button>}
      </> : <label className="grid gap-1">Report options<textarea className={inputStyle} rows={4} value={customOptions} onChange={e => setCustomOptions(e.target.value)} /></label>}
      {(types.error || basemaps.error || overlays.error || files.error) && <p role="alert">Some report choices could not load. Refresh the page to try again.</p>}
      {validation && <p role="alert">{validation}</p>}{submit.error && <p role="alert">{submit.error.message}</p>}
      <Button type="submit" disabled={submit.isPending || !type || !can(me, 'reports:generate') || !quota?.enabled || quota.remaining === 0}>{submit.isPending ? 'Submitting…' : 'Generate report'}</Button>
    </form>
  </>;
}

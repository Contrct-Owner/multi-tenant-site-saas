import { Button } from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { PageHeader, Panel } from '../../components/page';
import { useApiMutation } from '../../lib/mutation';
import { can, useMe } from '../../session';
import { reportsApi } from './api';

type Quota = Awaited<ReturnType<typeof reportsApi.quota>>;

const active = (state: string) => state === 'Queued' || state === 'Running';

export function ReportRunPage() {
  const { runId } = useParams({ strict: false }) as { runId: string };
  return <ReportsPage runId={runId} />;
}

export function ReportsPage({ runId }: { runId?: string }) {
  const { data: me, isPending } = useMe();
  if (isPending) return <p role="status">Loading reports…</p>;
  if (!can(me, 'files:read')) return <p role="alert">You do not have permission to read report files.</p>;
  return <Reports runId={runId} />;
}

function Reports({ runId }: { runId?: string }) {
  const { data: me } = useMe();
  const generate = can(me, 'reports:generate');
  const quota = useQuery({ queryKey: ['reports', 'quota'], queryFn: ({ signal }) => reportsApi.quota(signal), refetchInterval: 5000, enabled: generate });
  const list = useQuery({ queryKey: ['reports', 'list'], queryFn: ({ signal }) => reportsApi.list(signal),
    refetchInterval: query => query.state.data?.some(x => active(x.state)) ? 2000 : false });
  return <div className="space-y-6">
    <PageHeader title="Reports" description="Review report runs, progress, results and downloads. Start a new run by selecting sites in the site library." />
    {generate && <Panel title="Organization report allowance">
      {quota.isPending && <p role="status">Loading report allowance…</p>}
      {quota.error && <p role="alert">Report allowance could not load. <Button onClick={() => void quota.refetch()}>Reload allowance</Button></p>}
      {quota.data && <>
        <p>{quota.data.enabled ? `${quota.data.remaining} of ${quota.data.limit} PDFs available` : 'Report generation is not included in your organization’s plan.'}</p>
        <p className="text-sm">{quota.data.consumed} generated · {quota.data.reserved} reserved · UTC month starting {quota.data.periodMonth}</p>
        <p className="text-sm text-muted-foreground">A single or aggregate PDF uses one report. Bulk generation uses one per site. ZIPs and downloads do not use additional reports. Failed or canceled PDFs release their reservations.</p>
      </>}
    </Panel>}
    <Link to="/sites" className="underline">Choose sites to generate reports</Link>
    <Panel title="Report runs" description="Latest 50 runs. Each run keeps its selection, progress and results together.">
      {list.isPending && <p role="status">Loading runs…</p>}
      {list.error && <p role="alert">{list.error.message} <Button onClick={() => void list.refetch()}>Try again</Button></p>}
      {list.data?.length === 0 && <p>No reports requested yet.</p>}
      <ul className="space-y-2">{list.data?.map(report => <li key={report.id}>
        <Link to="/reports/$runId" params={{ runId: report.id }} className="block rounded border p-3 hover:underline" aria-current={runId === report.id ? 'page' : undefined}>
          Run {report.id} · {report.reportType} · {report.mode} · {new Date(report.createdAt).toLocaleString()} · {report.state}
        </Link>
      </li>)}</ul>
    </Panel>
    {runId && <ReportDetails key={runId} id={runId} quota={quota.data} />}
  </div>;
}

function ReportDetails({ id, quota }: { id: string; quota?: Quota }) {
  const { data: me } = useMe();
  const report = useQuery({ queryKey: ['reports', id], queryFn: ({ signal }) => reportsApi.get(id, signal), refetchInterval: query => active(query.state.data?.state ?? '') || query.state.data?.artifacts.some(a => a.contentType === 'application/pdf' && !a.fileId) ? 1000 : false });
  const cancel = useApiMutation({ mutationFn: () => reportsApi.cancel(id), invalidate: [['reports']], success: 'Cancellation requested' });
  const retry = useApiMutation({ mutationFn: () => reportsApi.retry(id), invalidate: [['reports']], success: 'Failed items queued for retry' });
  const download = useApiMutation({ mutationFn: (artifactId: string) => reportsApi.download(id, artifactId), onSuccess: result => { window.location.assign(result.url); } });
  const job = report.data;
  const modify = !!job?.canModify && can(me, 'reports:generate');
  const expired = !!job?.expiresAt && new Date(job.expiresAt).getTime() <= Date.now();
  return <Panel title={<h2>Report run {id}</h2>}>
    {report.isPending && <p role="status">Loading report…</p>}{report.error && <p role="alert">{report.error.message}</p>}
    {job && <div className="space-y-3">
      <p role="status">{job.state} · {job.items.filter(i => i.state === 'Succeeded').length} of {job.items.length} PDFs ready</p>
      <p className="text-sm">Run {job.id}{job.expiresAt && job.mode === 'bulk' && ` · ZIP expires ${new Date(job.expiresAt).toLocaleString()}`}</p>
      {job.errorCode && <p role="alert">{job.errorCode}</p>}
      {modify && active(job.state) && <Button disabled={cancel.isPending} onClick={() => cancel.mutate()}>Cancel report</Button>}
      {modify && !expired && ['Failed', 'CompletedWithErrors'].includes(job.state) && <Button disabled={retry.isPending || !quota?.enabled || quota.remaining < job.items.filter(item => item.state === 'Failed').length} onClick={() => retry.mutate()}>Retry failed items</Button>}
      <p className="text-sm text-muted-foreground">Retry keeps successful PDFs and their original data. Submit a new request to regenerate everything.</p>
      <ul className="space-y-2">{job.items.map(item => <li key={item.id} className="rounded border p-3">
        <p>{item.state} · Attempt {item.attempt}</p>
        <ul>{item.siteIds.map(siteId => <li key={siteId}>
          <Link to="/sites/$siteId" params={{ siteId }} className="underline">Site {siteId}</Link>
        </li>)}</ul>
        {item.generatedAt && <p>Generated {new Date(item.generatedAt).toLocaleString()}</p>}
        {item.errorCode && <p>{item.errorCode}</p>}{item.warnings.map((warning, i) => <p key={i}>{warning}</p>)}
      </li>)}</ul>
      {job.artifacts.map(artifact => <Button className="mr-2" key={artifact.id} variant="outline" disabled={download.isPending || (artifact.contentType === 'application/zip' ? expired : !artifact.fileId)} onClick={() => download.mutate(artifact.id)}>
        {artifact.contentType === 'application/pdf' && !artifact.fileId ? 'Publishing' : 'Download'} {artifact.contentType === 'application/zip' ? 'ZIP and manifest' : `PDF ${job.items.findIndex(i => i.id === artifact.itemId) + 1}`}
      </Button>)}
      {expired && job.mode === 'bulk' && <p>The run’s ZIP has expired. Published PDFs remain in site files.</p>}
      {(cancel.error || retry.error || download.error) && <p role="alert">{(cancel.error || retry.error || download.error)?.message}</p>}
    </div>}
  </Panel>;
}

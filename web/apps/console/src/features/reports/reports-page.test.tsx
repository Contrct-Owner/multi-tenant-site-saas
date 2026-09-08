// @vitest-environment jsdom
import '../../test/dom';
import type { ReactNode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, expect, it, vi } from 'vitest';
import { ApiError } from '@premise/api';
import { reportsApi, type ReportJob, validateSubmission } from './api';
import { ReportsPage } from './reports-page';
import { GenerateSiteReports, ReportRequestForm } from './generate-site-reports';

const session = vi.hoisted(() => ({ allowed: true, generate: true, navigate: vi.fn() }));
vi.mock('@tanstack/react-router', async importOriginal => ({
  ...await importOriginal<typeof import('@tanstack/react-router')>(),
  useNavigate: () => session.navigate,
  Link: ({ to, params, children, ...props }: { to: string; params?: { runId?: string; siteId?: string }; children: ReactNode }) =>
    <a href={to.replace('$runId', params?.runId ?? '').replace('$siteId', params?.siteId ?? '')} {...props}>{children}</a>,
}));
vi.mock('../../session', async importOriginal => ({
  ...await importOriginal<typeof import('../../session')>(),
  useMe: () => ({ data: { tier: 'user', capabilities: session.allowed ? ['files:read', 'sites:read', ...(session.generate ? ['reports:generate'] : [])] : [] }, isPending: false }),
}));
const id = '01990000-0000-7000-8000-000000000001';
const second = '01990000-0000-7000-8000-000000000002';
const report: ReportJob = { id, canModify: true, reportType: 'site', mode: 'single', selection: 'selected', state: 'CompletedWithErrors', errorCode: null,
  createdAt: '2026-09-07T10:00:00Z', expiresAt: '2099-01-01T00:00:00Z', artifacts: [],
  items: [{ id, state: 'Failed', errorCode: 'generation_failed', generatedAt: null, attempt: 1, siteIds: [id], warnings: ['Map unavailable'] }] };

beforeEach(() => {
  vi.restoreAllMocks(); session.allowed = true; session.generate = true; session.navigate.mockReset();
  vi.spyOn(reportsApi, 'quota').mockResolvedValue({ enabled: true, limit: 1000, consumed: 20, reserved: 5, remaining: 975, periodMonth: '2026-09-01' });
  vi.spyOn(reportsApi, 'types').mockResolvedValue([{ id: 'site', name: 'Site report', version: 1, aggregate: false }, { id: 'sites-summary', name: 'Summary', version: 1, aggregate: true }]);
  vi.spyOn(reportsApi, 'basemaps').mockResolvedValue([]);
  vi.spyOn(reportsApi, 'files').mockResolvedValue({ items: [], total: 0, nextOffset: null });
  vi.spyOn(reportsApi, 'list').mockResolvedValue([report]);
  vi.spyOn(reportsApi, 'get').mockResolvedValue(report);
  vi.spyOn(reportsApi, 'submit').mockResolvedValue({ id, state: 'Queued' });
  vi.spyOn(reportsApi, 'retry').mockResolvedValue({ id, state: 'Queued' });
});

function mount(content: ReactNode = <ReportsPage />) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{content}</QueryClientProvider>);
}

it('does not load report data without permission', () => {
  session.allowed = false; mount();
  expect(screen.getByRole('alert').textContent).toContain('permission');
  expect(reportsApi.list).not.toHaveBeenCalled();
});

it('shows run IDs and permanent run links instead of another site selection form', async () => {
  mount(<ReportsPage runId={id} />);
  const run = await screen.findByRole('link', { name: new RegExp(`Run ${id}`) });
  expect(run.getAttribute('href')).toBe(`/reports/${id}`);
  expect(screen.getByRole('link', { name: 'Choose sites to generate reports' }).getAttribute('href')).toBe('/sites');
  expect(screen.queryByRole('button', { name: 'Generate report' })).toBeNull();
  expect(reportsApi.types).not.toHaveBeenCalled();
  expect((await screen.findByRole('link', { name: `Site ${id}` })).getAttribute('href')).toBe(`/sites/${id}`);
});

it('captures the library selection and navigates to the accepted run', async () => {
  mount(<GenerateSiteReports sites={{ [id]: 'Riverside', [second]: 'North' }} />);
  await userEvent.click(screen.getByRole('button', { name: 'Generate report' }));
  await screen.findByText('975 PDFs available this UTC month');
  expect(screen.getByText('Riverside')).toBeTruthy();
  expect(screen.getByLabelText('Output')).toHaveProperty('value', 'bulk');
  await userEvent.click(screen.getAllByRole('button', { name: 'Generate report' }).at(-1)!);
  await waitFor(() => expect(reportsApi.submit).toHaveBeenCalledWith(expect.objectContaining({ mode: 'bulk', selection: 'selected', siteIds: [id, second] }), expect.any(String)));
  await waitFor(() => expect(session.navigate).toHaveBeenCalledWith({ to: '/reports/$runId', params: { runId: id } }));
});

it('keeps the same submission key when the request outcome is unknown', async () => {
  vi.mocked(reportsApi.submit).mockRejectedValue(new ApiError(0, { error: 'Connection interrupted' }, undefined, true));
  mount(<ReportRequestForm selectedSites={{ [id]: 'Riverside' }} onCreated={vi.fn()} />);
  await screen.findByText('975 PDFs available this UTC month');
  await userEvent.click(screen.getByRole('button', { name: 'Generate report' }));
  await screen.findByRole('alert');
  await userEvent.click(screen.getByRole('button', { name: 'Generate report' }));
  await waitFor(() => expect(reportsApi.submit).toHaveBeenCalledTimes(2));
  expect(vi.mocked(reportsApi.submit).mock.calls[0]?.[1]).toBe(vi.mocked(reportsApi.submit).mock.calls[1]?.[1]);
});

it('generates one aggregate for the exact library selection', async () => {
  mount(<ReportRequestForm selectedSites={{ [id]: 'Riverside', [second]: 'North' }} onCreated={vi.fn()} />);
  await screen.findByText('975 PDFs available this UTC month');
  await userEvent.selectOptions(screen.getByLabelText('Report type'), 'sites-summary');
  await userEvent.click(screen.getByRole('button', { name: 'Generate report' }));
  await waitFor(() => expect(reportsApi.submit).toHaveBeenCalledWith(expect.objectContaining({ mode: 'aggregate', selection: 'selected', siteIds: [id, second] }), expect.any(String)));
});

it('allows active cancellation from a run and displays server conflicts', async () => {
  vi.mocked(reportsApi.get).mockResolvedValue({ ...report, state: 'Running' });
  vi.spyOn(reportsApi, 'cancel').mockRejectedValue(new ApiError(409, { error: 'Report already finished' }));
  mount(<ReportsPage runId={id} />);
  await userEvent.click(await screen.findByRole('button', { name: 'Cancel report' }));
  expect((await screen.findByRole('alert')).textContent).toContain('already finished');
});

it('retries failed items from the run', async () => {
  mount(<ReportsPage runId={id} />);
  await screen.findByText('Map unavailable');
  await userEvent.click(screen.getByRole('button', { name: 'Retry failed items' }));
  await waitFor(() => expect(reportsApi.retry).toHaveBeenCalledWith(id));
});

it('refuses invalid single and duplicate selections at the API boundary', () => {
  expect(() => validateSubmission({ reportType: 'site', mode: 'single', selection: 'accessible', siteIds: [], options: {} })).toThrow('exactly one');
  expect(() => validateSubmission({ reportType: 'site', mode: 'bulk', selection: 'selected', siteIds: [id, id], options: {} })).toThrow('only once');
});

it('surfaces current-access failures when downloading from a run', async () => {
  vi.mocked(reportsApi.get).mockResolvedValue({ ...report, state: 'Completed', artifacts: [{ id, itemId: id, name: 'site.pdf', contentType: 'application/pdf', bytes: 1000, fileId: id }] });
  vi.spyOn(reportsApi, 'download').mockRejectedValue(new ApiError(403, { error: 'Current access is required' }));
  mount(<ReportsPage runId={id} />);
  await userEvent.click(await screen.findByRole('button', { name: 'Download PDF 1' }));
  expect((await screen.findByRole('alert')).textContent).toContain('Current access');
});

it('keeps published PDFs downloadable after the run ZIP expires', async () => {
  vi.mocked(reportsApi.get).mockResolvedValue({ ...report, expiresAt: '2000-01-01T00:00:00Z', artifacts: [{ id, itemId: id, name: 'site.pdf', contentType: 'application/pdf', bytes: 1000, fileId: id }] });
  mount(<ReportsPage runId={id} />);
  expect(await screen.findByRole('button', { name: 'Download PDF 1' })).toHaveProperty('disabled', false);
  expect(screen.queryByRole('button', { name: 'Retry failed items' })).toBeNull();
});

it.each([false, true])('blocks generation when disabled or out of quota (enabled=%s)', async enabled => {
  vi.mocked(reportsApi.quota).mockResolvedValue({ enabled, limit: 1000, consumed: 1000, reserved: 0, remaining: 0, periodMonth: '2026-09-01' });
  mount(<ReportRequestForm selectedSites={{ [id]: 'Riverside' }} onCreated={vi.fn()} />);
  await screen.findByText(enabled ? '0 PDFs available this UTC month' : 'Report generation is not included in your organization’s plan.');
  expect(screen.getByRole('button', { name: 'Generate report' })).toHaveProperty('disabled', true);
});

it('allows site-file readers to view runs without loading generation allowance', async () => {
  session.generate = false;
  mount(<ReportsPage runId={id} />);
  await screen.findByRole('link', { name: `Site ${id}` });
  expect(reportsApi.quota).not.toHaveBeenCalled();
  expect(screen.queryByText('Organization report allowance')).toBeNull();
});

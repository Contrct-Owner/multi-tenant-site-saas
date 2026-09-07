import { api, type components } from '@premise/api';

export type ReportJob = components['schemas']['JobResponse'];
export type ReportSubmission = components['schemas']['SubmitRequest'];

export function validateSubmission(value: ReportSubmission): ReportSubmission {
  if (!value.reportType || !['single', 'bulk', 'aggregate'].includes(value.mode)
    || !['selected', 'accessible', 'organization'].includes(value.selection)) throw new Error('Choose a report and selection.');
  if (value.selection === 'selected' && value.siteIds.length === 0) throw new Error('Select at least one site.');
  if (value.mode === 'single' && (value.selection !== 'selected' || value.siteIds.length !== 1)) throw new Error('A single report requires exactly one selected site.');
  if (new Set(value.siteIds).size !== value.siteIds.length) throw new Error('Select each site only once.');
  if (!value.options || typeof value.options !== 'object' || Array.isArray(value.options)) throw new Error('Report options must be an object.');
  return value;
}

function job(value: ReportJob): ReportJob {
  if (!value || typeof value.id !== 'string' || typeof value.state !== 'string' || !Array.isArray(value.items)
    || !Array.isArray(value.artifacts) || value.items.some(x => !x || typeof x.id !== 'string' || typeof x.state !== 'string')
    || value.artifacts.some(x => !x || typeof x.id !== 'string' || typeof x.name !== 'string')) throw new Error('Invalid report response.');
  return value;
}

export const reportsApi = {
  quota: (signal?: AbortSignal) => api.get('/api/reports/quota', { signal }),
  list: async (signal?: AbortSignal) => (await api.get('/api/reports', { signal })).map(job),
  get: async (id: string, signal?: AbortSignal) => job(await api.get('/api/reports/{id}', { path: { id }, signal })),
  types: (signal?: AbortSignal) => api.get('/api/reports/types', { signal }),
  basemaps: (signal?: AbortSignal) => api.get('/api/reports/basemaps', { signal }),
  overlays: (signal?: AbortSignal) => api.get('/api/overlays', { signal }),
  files: (offset: number, signal?: AbortSignal) => api.get('/api/files', { query: { limit: 50, offset }, signal }),
  submit: (body: ReportSubmission, idempotencyKey: string) => api.post('/api/reports', validateSubmission(body), { idempotencyKey }),
  cancel: (id: string) => api.post('/api/reports/{id}/cancel', undefined, { path: { id } }),
  retry: (id: string) => api.post('/api/reports/{id}/retry', undefined, { path: { id } }),
  download: (id: string, artifactId: string) => api.get('/api/reports/{id}/artifacts/{artifactId}/download', { path: { id, artifactId } }),
};

import { api, type components } from '@premise/api';
import { Button, ConfirmButton, type ColumnDef, type DataGridFeatures } from '@premise/ui';
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Grid, PageHeader } from '../components/page';
import { FileDropzone } from '../components/file-dropzone';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { uploadFile } from '../lib/uploads';
import { can, useMe } from '../session';
import { StatusBadge } from '../shell';

type FileRow = components['schemas']['FileSummary'];

export function FilesPage() {
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const manage = can(me, 'files:manage');
  const [phase, setPhase] = useState('');
  const [trash, setTrash] = useState(false);

  const filesQuery = useInfiniteQuery({
    queryKey: ['files', 'list', trash],
    queryFn: ({ pageParam, signal }) =>
      (api.get('/api/files', {
        query: { limit: 50, offset: pageParam, trash: trash ? true : undefined },
        signal,
      })),
    initialPageParam: 0,
    getNextPageParam: (last) =>
      last.nextOffset ?? undefined,
  });
  const files = filesQuery.data?.pages.flatMap((p) => p.items);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['files'] });

  const upload = useMutation({
    mutationFn: (file: File) =>
      uploadFile(file, file.type || 'application/octet-stream', setPhase),
    onSettled: () => {
      setPhase('');
      refresh();
    },
  });
  const hold = useApiMutation({
    mutationFn: (input: { id: string; hold: boolean }) =>
      api.post('/api/files/{id}/hold', { hold: input.hold }, { path: { id: input.id } }),
    invalidate: [['files']],
    success: 'Legal hold updated',
  });
  const erase = useApiMutation({
    mutationFn: (id: string) => api.del('/api/files/{id}', { path: { id } }),
    invalidate: [['files']],
    success: 'Moved to trash - restorable for 30 days',
    errorFallback: 'Delete failed',
  });
  const restore = useApiMutation({
    mutationFn: (id: string) => api.post('/api/files/{id}/restore', undefined, { path: { id } }),
    invalidate: [['files']],
    success: 'File restored',
  });

  const download = async (id: string) => {
    const { url } = await api.get('/api/files/{id}/download', { path: { id } });
    window.open(url, '_blank');
  };

  const columns = useMemo<ColumnDef<DataGridFeatures, FileRow>[]>(
    () => [
      {
        id: 'name',
        accessorKey: 'name',
        header: 'Name',
        cell: ({ row }) => (
          <div className="min-w-0">
            <div className="font-medium">{row.original.name}</div>
            <div className="text-xs text-muted-foreground">{row.original.contentType}</div>
          </div>
        ),
      },
      {
        id: 'status',
        accessorKey: 'status',
        header: 'Status',
        cell: ({ row }) => (
          <>
            <StatusBadge status={row.original.status} />
            {row.original.legalHold && <span className="ml-2 text-xs text-muted-foreground">⚖ hold</span>}
          </>
        ),
      },
      {
        id: 'uploaded',
        accessorKey: 'createdAt',
        header: 'Uploaded',
        cell: ({ row }) => <span className="text-muted-foreground">{fmtDateTime(row.original.createdAt)}</span>,
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) => {
          const f = row.original;
          return (
            <div className="space-x-1 text-right">
              {f.status === 'Clean' && (
                <Button variant="ghost" size="sm" onClick={() => void download(f.id)}>
                  Download
                </Button>
              )}
              {manage && f.status === 'Deleted' && (
                <Button variant="outline" size="sm" disabled={restore.isPending} onClick={() => restore.mutate(f.id)}>
                  Restore
                </Button>
              )}
              {manage && f.status !== 'Erased' && f.status !== 'Deleted' && (
                <>
                  <Button variant="ghost" size="sm" disabled={hold.isPending}
                    onClick={() => hold.mutate({ id: f.id, hold: !f.legalHold })}>
                    {f.legalHold ? 'Release hold' : 'Hold'}
                  </Button>
                  <ConfirmButton size="sm" disabled={erase.isPending} onConfirm={() => erase.mutate(f.id)}>
                    Delete
                  </ConfirmButton>
                </>
              )}
            </div>
          );
        },
        meta: { headerClassName: 'w-64' },
      },
    ],
    // the mutations are stable hooks; only `manage` decides a cell
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [manage],
  );

  return (
    <div className="space-y-6">
      <PageHeader
        title="Files"
        description="Uploads scanned before anyone can download them; the trash keeps deletions for 30 days."
        actions={<>
          <Button variant={trash ? 'default' : 'ghost'} size="sm"
            onClick={() => setTrash(!trash)}>
            Trash
          </Button>
        </>}
      />
      {manage && !trash && (
        <FileDropzone
          onFile={(file) => upload.mutate(file)}
          busy={upload.isPending}
          phase={phase}
          error={upload.isError ? String(upload.error) : undefined}
          label="Drop a file here, or choose one"
          hint="Scanned before anyone can download it."
          buttonLabel="Upload file"
        />
      )}
      <Grid
        columns={columns}
        rows={files ?? []}
        getRowId={(f) => f.id}
        isLoading={files === undefined}
        loadingMessage="Loading…"
        emptyMessage={`No files yet.${manage ? ' Upload one to get started.' : ''}`}
        onFetchMore={() => void filesQuery.fetchNextPage()}
        hasMore={filesQuery.hasNextPage}
        isFetchingMore={filesQuery.isFetchingNextPage}
      />
    </div>
  );
}

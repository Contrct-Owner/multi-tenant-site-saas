import { api } from '@premise/api';
import { Alert, AlertDescription, AlertTitle, Button, Card, CardContent, CardHeader,
  CardTitle, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@premise/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { uploadFile } from '../lib/uploads';

type Counts = { create: number; update: number; close: number; unchanged: number; invalid: number };
type Preview = {
  id: string;
  status: string;
  counts: Counts;
  rows: { externalId: string; name: string; nodePath: string; action: string; errors: string[]; changes: string[] }[];
};

export function IngestPage() {
  const queryClient = useQueryClient();
  const [batchId, setBatchId] = useState<string | null>(null);
  const [phase, setPhase] = useState<string>('');

  const stage = useMutation({
    mutationFn: async (file: File) => {
      const fileId = await uploadFile(file, 'text/csv', setPhase);
      setPhase('Computing diff…');
      const staged = await api.post<{ batchId: string }>('/api/ingest/uploads', { fileId });
      return staged.batchId;
    },
    onSuccess: (id) => {
      setBatchId(id);
      setPhase('');
    },
    onError: () => setPhase(''),
  });

  const { data: preview } = useQuery({
    queryKey: ['ingest-batch', batchId],
    queryFn: () => api.get<Preview>(`/api/ingest/batches/${batchId}`),
    enabled: batchId !== null,
  });

  const commit = useMutation({
    mutationFn: () => api.post<{ applied: number }>(`/api/ingest/batches/${batchId}/commit`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['ingest-batch', batchId] });
      void queryClient.invalidateQueries({ queryKey: ['sites'] });
    },
  });

  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Site ingest</h1>
      <Card>
        <CardHeader>
          <CardTitle>Upload CSV</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <p className="text-sm text-muted-foreground">
            Columns: external_id, name, time_zone, node, status (open|closed). Nothing is applied
            until you review the diff and commit.
          </p>
          <input
            type="file"
            accept=".csv,text/csv"
            className="text-sm"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) stage.mutate(file);
            }}
          />
          {phase && <p className="text-sm text-muted-foreground">{phase}</p>}
          {stage.isError && (
            <Alert variant="destructive">
              <AlertTitle>Staging failed</AlertTitle>
              <AlertDescription>{String(stage.error)}</AlertDescription>
            </Alert>
          )}
        </CardContent>
      </Card>

      {preview && (
        <Card>
          <CardHeader>
            <CardTitle>
              Diff preview — {preview.counts.create} new, {preview.counts.update} updated,{' '}
              {preview.counts.close} closing, {preview.counts.unchanged} unchanged,{' '}
              {preview.counts.invalid} invalid
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>External id</TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead>Node</TableHead>
                  <TableHead>Action</TableHead>
                  <TableHead>Detail</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {preview.rows.map((r) => (
                  <TableRow key={r.externalId + r.name}>
                    <TableCell className="font-mono text-xs">{r.externalId}</TableCell>
                    <TableCell>{r.name}</TableCell>
                    <TableCell className="text-muted-foreground">{r.nodePath}</TableCell>
                    <TableCell>{r.action}</TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {r.errors.join('; ') || r.changes.join('; ')}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            {preview.status === 'Staged' ? (
              <Button disabled={commit.isPending} onClick={() => commit.mutate()}>
                Commit {preview.counts.create + preview.counts.update + preview.counts.close} changes
              </Button>
            ) : (
              <p className="text-sm text-muted-foreground">Batch is {preview.status}.</p>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

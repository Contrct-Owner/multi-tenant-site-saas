import { api, ApiError } from '@premise/api';
import { Button, FormDialog,
  Input, Label, Select } from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Loading, PageHeader, Panel } from '../components/page';
import { HierarchyTree } from '../features/hierarchy/hierarchy-tree';
import { useApiMutation } from '../lib/mutation';

export function HierarchyPage() {
  const { data, isPending, isError, error } = useQuery({
    queryKey: ['hierarchy'],
    queryFn: ({ signal }) => api.get('/api/hierarchy', { signal }),
    retry: false,
  });
  const [levels, setLevels] = useState('Region, Market');
  const [nodeName, setNodeName] = useState('');
  const [parentId, setParentId] = useState('');
  const [adding, setAdding] = useState(false);

  const provision = useApiMutation({
    mutationFn: () =>
      api.post('/api/hierarchy', {
        name: 'Organization',
        levels: levels.split(',').map((l) => l.trim()).filter(Boolean),
      }),
    invalidate: [['hierarchy']],
    success: 'Hierarchy created',
  });
  const addNode = useApiMutation({
    mutationFn: () => api.post('/api/hierarchy/nodes', { parentId, name: nodeName }),
    invalidate: [['hierarchy']],
    success: 'Node added',
    onSuccess: () => {
      setNodeName('');
      setAdding(false);
    },
  });
  const rename = useApiMutation({
    mutationFn: (input: { id: string; name: string }) =>
      api.put('/api/hierarchy/nodes/{id}', { name: input.name }, { path: { id: input.id } }),
    invalidate: [['hierarchy']],
    success: 'Node renamed',
  });
  const removeNode = useApiMutation({
    mutationFn: (id: string) => api.del('/api/hierarchy/nodes/{id}', { path: { id } }),
    invalidate: [['hierarchy']],
    success: 'Node deleted',
    errorFallback: 'Delete failed',
  });

  if (isPending)
    return <Loading text="Loading hierarchy…" />;
  if (isError && !(error instanceof ApiError && error.status === 404))
    return <p className="text-sm text-destructive">Could not load hierarchy.</p>;

  if (!data) {
    return (
      <div className="max-w-lg space-y-6">
        <PageHeader title="Hierarchy" description="The rollup structure every site sits in." />
        <Panel title="Provision the org hierarchy" bodyClassName="space-y-3">
            <div className="space-y-1">
              <Label htmlFor="levels">Level names (root-first, comma-separated)</Label>
              <Input id="levels" value={levels} onChange={(e) => setLevels(e.target.value)} />
            </div>
            <Button disabled={provision.isPending} onClick={() => provision.mutate()}>
              Create hierarchy
            </Button>
            {provision.isError && (
              <p className="text-sm text-destructive">
                {String((provision.error as { body?: { error?: string } }).body?.error ?? 'failed')}
              </p>
            )}
        </Panel>
      </div>
    );
  }

  return (
    <div className="max-w-2xl space-y-6">
      <PageHeader
        title="Hierarchy"
        description={<>Levels: {data.levels.join(' → ')}</>}
        actions={
        <FormDialog
          open={adding}
          onOpenChange={setAdding}
          trigger={<Button>Add node</Button>}
          title="Add node"
          description="A new branch under an existing node."
        >
          <div className="space-y-3">
            <div className="space-y-1">
              <Label htmlFor="node-name">Name</Label>
              <Input id="node-name" value={nodeName}
                onChange={(e) => setNodeName(e.target.value)} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="node-parent">Parent</Label>
              <Select id="node-parent" value={parentId}
                onChange={(e) => setParentId(e.target.value)}>
                <option value="">Choose…</option>
                {data.nodes.map((n) => (
                  <option key={n.id} value={n.id}>
                    {' '.repeat(Number(n.depth) * 2)}{n.name}
                  </option>
                ))}
              </Select>
            </div>
            <Button className="w-full" disabled={!nodeName || !parentId || addNode.isPending}
              onClick={() => addNode.mutate()}>
              Add node
            </Button>
          </div>
        </FormDialog>
        }
      />
      <Panel>
        <HierarchyTree
          nodes={data.nodes.map((n) => ({
            id: n.id,
            name: n.name,
            depth: Number(n.depth),
            parentId: n.parentId ?? null,
          }))}
          busy={rename.isPending || removeNode.isPending}
          onRename={(id, name) => rename.mutate({ id, name })}
          onDelete={(id) => removeNode.mutate(id)}
        />
      </Panel>
    </div>
  );
}

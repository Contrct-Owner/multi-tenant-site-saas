import { api } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from '@premise/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

type Node = { id: string; parentId?: string; name: string; depth: number; path: string };
type Hierarchy = { id: string; name: string; levels: string[]; nodes: Node[] };

export function HierarchyPage() {
  const queryClient = useQueryClient();
  const { data, isError } = useQuery({
    queryKey: ['hierarchy'],
    queryFn: () => api.get<Hierarchy>('/api/hierarchy'),
    retry: false,
  });
  const [levels, setLevels] = useState('Region, Market');
  const [nodeName, setNodeName] = useState('');
  const [parentId, setParentId] = useState('');
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['hierarchy'] });

  const provision = useMutation({
    mutationFn: () =>
      api.post('/api/hierarchy', {
        name: 'Organization',
        levels: levels.split(',').map((l) => l.trim()).filter(Boolean),
      }),
    onSuccess: invalidate,
  });
  const addNode = useMutation({
    mutationFn: () => api.post('/api/hierarchy/nodes', { parentId, name: nodeName }),
    onSuccess: () => {
      setNodeName('');
      void invalidate();
    },
  });
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const rename = useMutation({
    mutationFn: (input: { id: string; name: string }) =>
      api.put(`/api/hierarchy/nodes/${input.id}`, { name: input.name }),
    onSuccess: () => {
      setEditingId(null);
      void invalidate();
    },
  });
  const removeNode = useMutation({
    mutationFn: (id: string) => api.del(`/api/hierarchy/nodes/${id}`),
    onSuccess: invalidate,
    onError: (e) =>
      alert(String((e as { body?: { error?: string } }).body?.error ?? 'delete failed')),
  });

  if (isError || !data) {
    return (
      <div className="max-w-lg space-y-6">
        <h1 className="text-2xl font-semibold">Hierarchy</h1>
        <Card>
          <CardHeader><CardTitle>Provision the org hierarchy</CardTitle></CardHeader>
          <CardContent className="space-y-3">
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
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="max-w-2xl space-y-6">
      <h1 className="text-2xl font-semibold">Hierarchy</h1>
      <p className="text-sm text-muted-foreground">Levels: {data.levels.join(' → ')}</p>
      <Card>
        <CardContent className="pt-4">
          <ul className="space-y-1 text-sm">
            {data.nodes.map((n) => {
              const isLeaf =
                !data.nodes.some((c) => c.parentId === n.id) && n.depth > 0;
              return (
                <li
                  key={n.id}
                  className="group flex items-center gap-2"
                  style={{ paddingLeft: `${n.depth * 1.25}rem` }}
                >
                  {editingId === n.id ? (
                    <>
                      <Input
                        className="h-7 w-48"
                        value={editName}
                        autoFocus
                        onChange={(e) => setEditName(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' && editName.trim())
                            rename.mutate({ id: n.id, name: editName.trim() });
                          if (e.key === 'Escape') setEditingId(null);
                        }}
                      />
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={!editName.trim() || rename.isPending}
                        onClick={() => rename.mutate({ id: n.id, name: editName.trim() })}
                      >
                        Save
                      </Button>
                    </>
                  ) : (
                    <>
                      <span className="font-mono">
                        {n.depth > 0 ? '└ ' : ''}{n.name}
                      </span>
                      <span className="invisible flex gap-1 group-hover:visible">
                        <Button
                          variant="ghost"
                          size="sm"
                          className="h-6 px-2 text-xs"
                          onClick={() => {
                            setEditingId(n.id);
                            setEditName(n.name);
                          }}
                        >
                          Rename
                        </Button>
                        {isLeaf && (
                          <Button
                            variant="ghost"
                            size="sm"
                            className="h-6 px-2 text-xs"
                            disabled={removeNode.isPending}
                            onClick={() => {
                              if (window.confirm(`Delete node ${n.name}?`))
                                removeNode.mutate(n.id);
                            }}
                          >
                            Delete
                          </Button>
                        )}
                      </span>
                    </>
                  )}
                </li>
              );
            })}
          </ul>
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Add node</CardTitle></CardHeader>
        <CardContent className="flex items-end gap-3">
          <div className="flex-1 space-y-1">
            <Label htmlFor="node-name">Name</Label>
            <Input id="node-name" value={nodeName} onChange={(e) => setNodeName(e.target.value)} />
          </div>
          <div className="flex-1 space-y-1">
            <Label htmlFor="node-parent">Parent</Label>
            <select
              id="node-parent"
              className="h-9 w-full rounded-md border bg-background px-2 text-sm"
              value={parentId}
              onChange={(e) => setParentId(e.target.value)}
            >
              <option value="">Choose…</option>
              {data.nodes.map((n) => (
                <option key={n.id} value={n.id}>{n.name}</option>
              ))}
            </select>
          </div>
          <Button disabled={!nodeName || !parentId || addNode.isPending} onClick={() => addNode.mutate()}>
            Add
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}

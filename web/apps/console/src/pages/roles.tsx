import { api, CAPABILITIES } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@premise/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

type Grant = { domain: string; action: string };
type Role = { id: string; name: string; grants: Grant[]; assignedCount: number };
type Member = { userId: string; email: string; roles: string[] };
type Node = { id: string; name: string; depth: number; path: string };
type Hierarchy = { nodes: Node[] };
type GrantException = {
  id: string; email: string; domain: string; action: string;
  scopePath: string | null; reason: string; expiresAt: string;
};

const WILDCARD = '*:*';
// grantable pairs come from codegen (ADR 16); platform:operate is the
// operator wall - an org role can hold it, but it only bites in the platform org
const GRANTABLE = CAPABILITIES.filter((c) => c !== 'public:read' && c !== 'platform:operate');

const grantKey = (g: Grant) => `${g.domain}:${g.action}`;

/** The role editor: what a role grants, who holds it and where, and the exceptions. */
export function RolesPage() {
  const queryClient = useQueryClient();
  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: () => api.get<Role[]>('/api/roles'),
  });
  const { data: members } = useQuery({
    queryKey: ['members'],
    queryFn: () => api.get<Member[]>('/api/members'),
  });
  const { data: hierarchy } = useQuery({
    queryKey: ['hierarchy'],
    queryFn: () => api.get<Hierarchy>('/api/hierarchy'),
    retry: false,
  });

  const [editing, setEditing] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['roles'] });
  const reset = () => {
    setEditing(null);
    setName('');
    setPicked(new Set());
  };

  const save = useMutation({
    mutationFn: () => {
      const grants = [...picked].map((key) => {
        const [domain = '', action = ''] = key.split(':');
        return { domain, action };
      });
      return editing
        ? api.put(`/api/roles/${editing}`, { name: name.trim(), grants })
        : api.post('/api/roles', { name: name.trim(), grants });
    },
    onSuccess: () => {
      reset();
      refresh();
    },
    onError: (e) =>
      alert(String((e as { body?: { error?: string } }).body?.error ?? 'save failed')),
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.del(`/api/roles/${id}`),
    onSuccess: refresh,
    onError: (e) =>
      alert(String((e as { body?: { error?: string } }).body?.error ?? 'delete failed')),
  });

  const toggle = (key: string) => {
    const next = new Set(picked);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    setPicked(next);
  };

  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Roles</h1>

      <Card>
        <CardHeader><CardTitle>Roles</CardTitle></CardHeader>
        <CardContent>
          {roles && roles.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Role</TableHead>
                  <TableHead>Grants</TableHead>
                  <TableHead>Held by</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {roles.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="font-medium">{r.name}</TableCell>
                    <TableCell>
                      <div className="flex max-w-md flex-wrap gap-1">
                        {r.grants.map((g) => (
                          <span
                            key={grantKey(g)}
                            className="rounded bg-accent px-1.5 py-0.5 font-mono text-xs"
                          >
                            {grantKey(g)}
                          </span>
                        ))}
                      </div>
                    </TableCell>
                    <TableCell className="text-muted-foreground">{r.assignedCount}</TableCell>
                    <TableCell className="space-x-1 text-right">
                      <Button variant="ghost" size="sm"
                        onClick={() => {
                          setEditing(r.id);
                          setName(r.name);
                          setPicked(new Set(r.grants.map(grantKey)));
                        }}>
                        Edit
                      </Button>
                      <Button variant="ghost" size="sm" disabled={remove.isPending}
                        onClick={() => {
                          if (window.confirm(`Delete role ${r.name}?`)) remove.mutate(r.id);
                        }}>
                        Delete
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <p className="text-sm text-muted-foreground">No roles yet.</p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{editing ? 'Edit role' : 'Create role'}</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="space-y-1">
            <Label htmlFor="role-name">Name</Label>
            <Input id="role-name" className="max-w-xs" value={name}
              onChange={(e) => setName(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label>Grants</Label>
            <div className="grid grid-cols-3 gap-1.5">
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={picked.has(WILDCARD)}
                  onChange={() => toggle(WILDCARD)} />
                <span className="font-mono">*:* (everything)</span>
              </label>
              {GRANTABLE.map((c) => (
                <label key={c} className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={picked.has(c)} onChange={() => toggle(c)} />
                  <span className="font-mono">{c}</span>
                </label>
              ))}
            </div>
          </div>
          <div className="flex gap-2">
            <Button size="sm" disabled={!name.trim() || picked.size === 0 || save.isPending}
              onClick={() => save.mutate()}>
              {editing ? 'Save' : 'Create'}
            </Button>
            {editing && (
              <Button variant="ghost" size="sm" onClick={reset}>Cancel</Button>
            )}
          </div>
        </CardContent>
      </Card>

      <AssignCard roles={roles ?? []} members={members ?? []} nodes={hierarchy?.nodes ?? []} />
      <ExceptionsCard members={members ?? []} nodes={hierarchy?.nodes ?? []} />
    </div>
  );
}

/** Scoped assignment is the point: a role can apply to the whole org or one subtree. */
function AssignCard({ roles, members, nodes }: { roles: Role[]; members: Member[]; nodes: Node[] }) {
  const queryClient = useQueryClient();
  const [roleId, setRoleId] = useState('');
  const [userId, setUserId] = useState('');
  const [scopePath, setScopePath] = useState('');

  const assign = useMutation({
    mutationFn: () =>
      api.post(`/api/roles/${roleId}/assign`, {
        userId,
        scopePath: scopePath || null,
      }),
    onSuccess: () => {
      setUserId('');
      setScopePath('');
      void queryClient.invalidateQueries({ queryKey: ['roles'] });
      void queryClient.invalidateQueries({ queryKey: ['members'] });
    },
  });

  return (
    <Card>
      <CardHeader><CardTitle>Assign a role</CardTitle></CardHeader>
      <CardContent className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <Label htmlFor="assign-role">Role</Label>
          <select id="assign-role"
            className="h-9 w-40 rounded-md border bg-background px-2 text-sm"
            value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            <option value="">Choose…</option>
            {roles.map((r) => (
              <option key={r.id} value={r.id}>{r.name}</option>
            ))}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="assign-member">Member</Label>
          <select id="assign-member"
            className="h-9 w-56 rounded-md border bg-background px-2 text-sm"
            value={userId} onChange={(e) => setUserId(e.target.value)}>
            <option value="">Choose…</option>
            {members.map((m) => (
              <option key={m.userId} value={m.userId}>{m.email}</option>
            ))}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="assign-scope">Scope</Label>
          <select id="assign-scope"
            className="h-9 w-48 rounded-md border bg-background px-2 text-sm"
            value={scopePath} onChange={(e) => setScopePath(e.target.value)}>
            <option value="">Entire org</option>
            {nodes.map((n) => (
              <option key={n.id} value={n.path}>
                {' '.repeat(n.depth * 2)}{n.name} subtree
              </option>
            ))}
          </select>
        </div>
        <Button size="sm" disabled={!roleId || !userId || assign.isPending}
          onClick={() => assign.mutate()}>
          Assign
        </Button>
        {assign.isSuccess && <span className="text-sm text-muted-foreground">Assigned.</span>}
      </CardContent>
    </Card>
  );
}

/** Time-boxed additive exceptions (never deny): first-class and auditable. */
function ExceptionsCard({ members, nodes }: { members: Member[]; nodes: Node[] }) {
  const queryClient = useQueryClient();
  const { data: exceptions } = useQuery({
    queryKey: ['grant-exceptions'],
    queryFn: () => api.get<GrantException[]>('/api/grant-exceptions'),
  });
  const [userId, setUserId] = useState('');
  const [capability, setCapability] = useState('');
  const [reason, setReason] = useState('');
  const [days, setDays] = useState('7');
  const [scopePath, setScopePath] = useState('');
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['grant-exceptions'] });

  const grant = useMutation({
    mutationFn: () => {
      const [domain = '', action = ''] = capability.split(':');
      return api.post('/api/grant-exceptions', {
        userId,
        domain,
        action,
        reason: reason.trim(),
        expiresAt: new Date(Date.now() + Number(days) * 86400_000).toISOString(),
        scopePath: scopePath || null,
      });
    },
    onSuccess: () => {
      setUserId('');
      setCapability('');
      setReason('');
      refresh();
    },
  });
  const revoke = useMutation({
    mutationFn: (id: string) => api.del(`/api/grant-exceptions/${id}`),
    onSuccess: refresh,
  });

  return (
    <Card>
      <CardHeader><CardTitle>Grant exceptions</CardTitle></CardHeader>
      <CardContent className="space-y-4">
        {exceptions && exceptions.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Member</TableHead>
                <TableHead>Grant</TableHead>
                <TableHead>Reason</TableHead>
                <TableHead>Expires</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {exceptions.map((x) => (
                <TableRow key={x.id}>
                  <TableCell>{x.email}</TableCell>
                  <TableCell className="font-mono text-xs">
                    {x.domain}:{x.action}
                    {x.scopePath && <span className="text-muted-foreground"> (scoped)</span>}
                  </TableCell>
                  <TableCell className="max-w-48 truncate text-muted-foreground">
                    {x.reason}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {new Date(x.expiresAt).toLocaleDateString()}
                  </TableCell>
                  <TableCell className="text-right">
                    <Button variant="ghost" size="sm" disabled={revoke.isPending}
                      onClick={() => revoke.mutate(x.id)}>
                      Revoke
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        <div className="flex flex-wrap items-end gap-3 rounded-md border p-3">
          <div className="space-y-1">
            <Label htmlFor="exc-member">Member</Label>
            <select id="exc-member"
              className="h-9 w-52 rounded-md border bg-background px-2 text-sm"
              value={userId} onChange={(e) => setUserId(e.target.value)}>
              <option value="">Choose…</option>
              {members.map((m) => (
                <option key={m.userId} value={m.userId}>{m.email}</option>
              ))}
            </select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="exc-cap">Capability</Label>
            <select id="exc-cap"
              className="h-9 w-44 rounded-md border bg-background px-2 text-sm"
              value={capability} onChange={(e) => setCapability(e.target.value)}>
              <option value="">Choose…</option>
              {GRANTABLE.map((c) => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="exc-scope">Scope</Label>
            <select id="exc-scope"
              className="h-9 w-40 rounded-md border bg-background px-2 text-sm"
              value={scopePath} onChange={(e) => setScopePath(e.target.value)}>
              <option value="">Entire org</option>
              {nodes.map((n) => (
                <option key={n.id} value={n.path}>
                  {' '.repeat(n.depth * 2)}{n.name} subtree
                </option>
              ))}
            </select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="exc-reason">Reason</Label>
            <Input id="exc-reason" className="w-48" value={reason}
              onChange={(e) => setReason(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="exc-days">Days</Label>
            <Input id="exc-days" type="number" min="1" className="w-20" value={days}
              onChange={(e) => setDays(e.target.value)} />
          </div>
          <Button size="sm"
            disabled={!userId || !capability || !reason.trim() || !Number(days) || grant.isPending}
            onClick={() => grant.mutate()}>
            Grant
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

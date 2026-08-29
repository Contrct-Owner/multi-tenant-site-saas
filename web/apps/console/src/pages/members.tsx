import { api } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@premise/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useMe } from '../session';

type Member = { userId: string; email: string; name?: string; joinedAt: string; roles: string[] };
type Role = { id: string; name: string };
type Invitation = { id: string; email: string; state: string; expiresAt: string; role?: string };

export function MembersPage() {
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const { data: members } = useQuery({
    queryKey: ['members'],
    queryFn: () => api.get<Member[]>('/api/members'),
  });
  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: () => api.get<Role[]>('/api/roles'),
  });
  const { data: invitations } = useQuery({
    queryKey: ['invitations'],
    queryFn: () => api.get<Invitation[]>('/api/members/invitations'),
  });
  const [email, setEmail] = useState('');
  const [roleId, setRoleId] = useState('');

  const invite = useMutation({
    mutationFn: () => api.post('/api/members/invitations', { email, roleId }),
    onSuccess: () => {
      setEmail('');
      void queryClient.invalidateQueries({ queryKey: ['invitations'] });
    },
  });
  const revoke = useMutation({
    mutationFn: (id: string) => api.del(`/api/members/invitations/${id}`),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['invitations'] }),
  });
  const remove = useMutation({
    mutationFn: (userId: string) => api.del(`/api/members/${userId}`),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['members'] }),
    onError: (e) =>
      alert(String((e as { body?: { error?: string } }).body?.error ?? 'removal failed')),
  });
  const unassign = useMutation({
    mutationFn: (input: { roleName: string; userId: string }) => {
      const role = roles?.find((r) => r.name === input.roleName);
      if (!role) throw new Error('unknown role');
      return api.del(`/api/roles/${role.id}/assign/${input.userId}`);
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['members'] }),
    onError: (e) =>
      alert(String((e as { body?: { error?: string } }).body?.error ?? 'unassign failed')),
  });

  const self = me?.tier === 'user' ? me.userId : undefined;

  return (
    <div className="max-w-4xl space-y-6">
      <h1 className="text-2xl font-semibold">Members</h1>

      <Card>
        <CardContent className="pt-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Member</TableHead>
                <TableHead>Roles</TableHead>
                <TableHead>Joined</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {members?.map((m) => (
                <TableRow key={m.userId}>
                  <TableCell>
                    <div className="font-medium">{m.name ?? m.email}</div>
                    <div className="text-xs text-muted-foreground">{m.email}</div>
                  </TableCell>
                  <TableCell>
                    {m.roles.length === 0
                      ? '—'
                      : m.roles.map((roleName) => (
                          <button
                            key={roleName}
                            type="button"
                            title="Click to unassign"
                            className="mr-1 rounded-full bg-secondary px-2 py-0.5 text-xs hover:bg-destructive/15"
                            onClick={() => unassign.mutate({ roleName, userId: m.userId })}
                          >
                            {roleName} ×
                          </button>
                        ))}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {new Date(m.joinedAt).toLocaleDateString()}
                  </TableCell>
                  <TableCell className="text-right">
                    {m.userId !== self && (
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={remove.isPending}
                        onClick={() => remove.mutate(m.userId)}
                      >
                        Remove
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Invite a member</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <div className="flex items-end gap-3">
            <div className="flex-1 space-y-1">
              <Label htmlFor="invite-email">Email</Label>
              <Input
                id="invite-email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </div>
            <div className="w-56 space-y-1">
              <Label htmlFor="invite-role">Role</Label>
              <select
                id="invite-role"
                className="h-9 w-full rounded-md border bg-background px-2 text-sm"
                value={roleId}
                onChange={(e) => setRoleId(e.target.value)}
              >
                <option value="">Choose…</option>
                {roles?.map((r) => (
                  <option key={r.id} value={r.id}>{r.name}</option>
                ))}
              </select>
            </div>
            <Button
              disabled={!email.includes('@') || !roleId || invite.isPending}
              onClick={() => invite.mutate()}
            >
              Send invite
            </Button>
          </div>
          {invite.isError && (
            <p className="text-sm text-destructive">
              {String((invite.error as { body?: { error?: string } }).body?.error ?? invite.error)}
            </p>
          )}
          {invitations && invitations.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Pending</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>State</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {invitations.map((inv) => (
                  <TableRow key={inv.id}>
                    <TableCell>{inv.email}</TableCell>
                    <TableCell>{inv.role ?? '—'}</TableCell>
                    <TableCell className="text-muted-foreground">{inv.state}</TableCell>
                    <TableCell className="text-right">
                      {inv.state === 'pending' && (
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={revoke.isPending}
                          onClick={() => revoke.mutate(inv.id)}
                        >
                          Revoke
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

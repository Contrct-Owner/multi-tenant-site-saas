import { api, type components } from '@premise/api';
import { Button, ConfirmButton, FormDialog, Input, Label, Select, type ColumnDef,
  type DataGridFeatures } from '@premise/ui';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Grid, PageHeader } from '../components/page';
import { fmtDate } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { useMe } from '../session';

type MemberRow = components['schemas']['MemberSummary'];
type InvitationRow = components['schemas']['InvitationResponse'];
type ContactRow = components['schemas']['ContactResponse'];

export function MembersPage() {
  const { data: me } = useMe();
  const membersQuery = useInfiniteQuery({
    queryKey: ['members', 'list'],
    queryFn: ({ pageParam, signal }) =>
      api.get('/api/members', { query: { limit: 50, offset: pageParam }, signal }),
    initialPageParam: 0,
    getNextPageParam: (last) =>
      last.nextOffset == null ? undefined : Number(last.nextOffset),
  });
  const members = membersQuery.data?.pages.flatMap((p) => p.items);
  const { data: roles } = useQuery({
    queryKey: ['roles'],
    queryFn: ({ signal }) => api.get('/api/roles', { signal }),
  });
  const { data: invitations } = useQuery({
    queryKey: ['invitations'],
    queryFn: ({ signal }) => api.get('/api/members/invitations', { signal }),
  });
  const { data: contacts } = useQuery({
    queryKey: ['contacts'],
    queryFn: ({ signal }) => api.get('/api/contacts', { signal }),
  });
  const [email, setEmail] = useState('');
  const [roleId, setRoleId] = useState('');
  const [inviting, setInviting] = useState(false);
  const [contactEmail, setContactEmail] = useState('');
  const [invitingContact, setInvitingContact] = useState(false);

  const invite = useApiMutation({
    mutationFn: () => api.post('/api/members/invitations', { email, roleId }),
    invalidate: [['invitations']],
    success: 'Invitation sent',
    onSuccess: () => {
      setEmail('');
      setInviting(false);
    },
  });
  const revoke = useApiMutation({
    mutationFn: (id: string) => api.del('/api/members/invitations/{invitationId}', { path: { invitationId: id } }),
    invalidate: [['invitations']],
    success: 'Invitation revoked',
  });
  const remove = useApiMutation({
    mutationFn: (userId: string) => api.del('/api/members/{userId}', { path: { userId } }),
    invalidate: [['members']],
    success: 'Member removed',
    errorFallback: 'Removal failed',
  });
  const unassign = useApiMutation({
    mutationFn: (input: { roleName: string; userId: string }) => {
      const role = roles?.find((r) => r.name === input.roleName);
      if (!role) throw new Error('unknown role');
      return api.del('/api/roles/{id}/assign/{userId}', { path: { id: role.id, userId: input.userId } });
    },
    invalidate: [['members']],
    success: 'Role unassigned',
    errorFallback: 'Unassign failed',
  });

  // closes the review's P2-flagged gap while we're here: the contact journey
  // can finally START from the console
  const inviteContact = useApiMutation({
    mutationFn: () => api.post('/contact-links', { email: contactEmail }),
    invalidate: [['contacts']],
    success: 'Contact link sent',
    onSuccess: () => {
      setContactEmail('');
      setInvitingContact(false);
    },
  });
  const revokeContact = useApiMutation({
    mutationFn: (id: string) => api.del('/api/contacts/{id}', { path: { id } }),
    invalidate: [['contacts']],
    success: 'Contact revoked',
  });

  const self = me?.tier === 'user' ? me.userId : undefined;

  const memberColumns = useMemo<ColumnDef<DataGridFeatures, MemberRow>[]>(
    () => [
      {
        id: 'member',
        accessorKey: 'email',
        header: 'Member',
        cell: ({ row }) => (
          <div className="min-w-0">
            <div className="font-medium">{row.original.name ?? row.original.email}</div>
            <div className="text-xs text-muted-foreground">{row.original.email}</div>
          </div>
        ),
      },
      {
        id: 'roles',
        header: 'Roles',
        cell: ({ row }) =>
          row.original.roles.length === 0
            ? '—'
            : row.original.roles.map((roleName) => (
                <span key={roleName}
                  className="mr-1 inline-flex items-center gap-1 rounded-full bg-secondary px-2 py-0.5 text-xs">
                  {roleName}
                  <ConfirmButton
                    size="sm"
                    variant="ghost"
                    className="h-4 px-1 text-xs"
                    confirmLabel="Unassign?"
                    disabled={unassign.isPending}
                    onConfirm={() => unassign.mutate({ roleName, userId: row.original.userId })}
                  >
                    ×
                  </ConfirmButton>
                </span>
              )),
      },
      {
        id: 'joined',
        accessorKey: 'joinedAt',
        header: 'Joined',
        cell: ({ row }) => <span className="text-muted-foreground">{fmtDate(row.original.joinedAt)}</span>,
        meta: { headerClassName: 'w-36' },
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) =>
          row.original.userId !== self ? (
            <div className="text-right">
              <ConfirmButton size="sm" disabled={remove.isPending} onConfirm={() => remove.mutate(row.original.userId)}>
                Remove
              </ConfirmButton>
            </div>
          ) : null,
        meta: { headerClassName: 'w-28' },
      },
    ],
    // the mutations are stable hooks; only `self` decides a cell
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [self],
  );
  const invitationColumns = useMemo<ColumnDef<DataGridFeatures, InvitationRow>[]>(
    () => [
      { id: 'email', accessorKey: 'email', header: 'Pending' },
      { id: 'role', header: 'Role', cell: ({ row }) => row.original.role ?? '—' },
      {
        id: 'state',
        accessorKey: 'state',
        header: 'State',
        cell: ({ row }) => <span className="text-muted-foreground">{row.original.state}</span>,
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) =>
          row.original.state === 'pending' ? (
            <div className="text-right">
              <ConfirmButton size="sm" disabled={revoke.isPending} onConfirm={() => revoke.mutate(row.original.id)}>
                Revoke
              </ConfirmButton>
            </div>
          ) : null,
        meta: { headerClassName: 'w-28' },
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );
  const contactColumns = useMemo<ColumnDef<DataGridFeatures, ContactRow>[]>(
    () => [
      { id: 'email', accessorKey: 'email', header: 'Email' },
      {
        id: 'since',
        accessorKey: 'createdAt',
        header: 'Since',
        cell: ({ row }) => <span className="text-muted-foreground">{fmtDate(row.original.createdAt)}</span>,
      },
      {
        id: 'status',
        header: 'Status',
        cell: ({ row }) => (
          <span className="text-muted-foreground">{row.original.revoked ? 'Revoked' : 'Active'}</span>
        ),
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) =>
          !row.original.revoked ? (
            <div className="text-right">
              <ConfirmButton size="sm" disabled={revokeContact.isPending} onConfirm={() => revokeContact.mutate(row.original.id)}>
                Revoke
              </ConfirmButton>
            </div>
          ) : null,
        meta: { headerClassName: 'w-28' },
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  return (
    <div className="space-y-6">
      <PageHeader
        title="Members"
        description="Who works in this organization, and the contacts with a link to its public pages."
        actions={<>
          <FormDialog
            open={invitingContact}
            onOpenChange={setInvitingContact}
            trigger={<Button variant="outline">Invite a contact</Button>}
            title="Invite a contact"
            description="Contacts get an identified link to your public pages - no account, revocable any time."
          >
            <div className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="contact-email">Email</Label>
                <Input id="contact-email" type="email" value={contactEmail}
                  onChange={(e) => setContactEmail(e.target.value)} />
              </div>
              <Button className="w-full"
                disabled={!contactEmail.includes('@') || inviteContact.isPending}
                onClick={() => inviteContact.mutate()}>
                Send contact link
              </Button>
            </div>
          </FormDialog>
          <FormDialog
            open={inviting}
            onOpenChange={setInviting}
            trigger={<Button>Invite a member</Button>}
            title="Invite a member"
            description="They join with the role you pick, delivered by your identity provider."
          >
            <div className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="invite-email">Email</Label>
                <Input id="invite-email" type="email" value={email}
                  onChange={(e) => setEmail(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="invite-role">Role</Label>
                <Select id="invite-role" value={roleId}
                  onChange={(e) => setRoleId(e.target.value)}>
                  <option value="">Choose…</option>
                  {roles?.map((r) => (
                    <option key={r.id} value={r.id}>{r.name}</option>
                  ))}
                </Select>
              </div>
              <Button className="w-full"
                disabled={!email.includes('@') || !roleId || invite.isPending}
                onClick={() => invite.mutate()}>
                Send invite
              </Button>
            </div>
          </FormDialog>
        </>}
      />

      <Grid
        columns={memberColumns}
        rows={members ?? []}
        getRowId={(m) => m.userId}
        isLoading={members === undefined}
        loadingMessage="Loading…"
        emptyMessage="No members yet."
        footer={
          membersQuery.hasNextPage ? (
            <Button variant="outline" size="sm"
              disabled={membersQuery.isFetchingNextPage}
              onClick={() => void membersQuery.fetchNextPage()}>
              Load more
            </Button>
          ) : undefined
        }
      />

      {invitations && invitations.length > 0 && (
        <Grid
          title="Pending invitations"
          columns={invitationColumns}
          rows={invitations}
          getRowId={(inv) => inv.id}
        />
      )}

      <Grid
        title="Contacts"
        description="People given identified access to your public pages via contact links. Revoking cuts off live sessions and unexpired links at once."
        columns={contactColumns}
        rows={contacts ?? []}
        getRowId={(c) => c.id}
        isLoading={contacts === undefined}
        emptyMessage="No contacts yet."
      />
    </div>
  );
}

import { api } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from '@premise/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useMe } from '../session';

type Session = { id: string; userAgent: string | null; createdAt: string; current: boolean };

/** The user acting on themselves: profile, credentials entry points, sessions, deletion. */
export function AccountPage() {
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const [name, setName] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const { data: sessions } = useQuery({
    queryKey: ['sessions'],
    queryFn: () => api.get<Session[]>('/auth/sessions'),
  });

  const rename = useMutation({
    mutationFn: (value: string) => api.put('/auth/profile', { name: value }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
  const passwordReset = useMutation({
    mutationFn: () => api.post('/auth/password-reset'),
  });
  const revoke = useMutation({
    mutationFn: (id: string) => api.del(`/auth/sessions/${id}`),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['sessions'] }),
  });
  const revokeOthers = useMutation({
    mutationFn: () => api.post('/auth/sessions/revoke-others'),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['sessions'] }),
  });
  const deleteAccount = useMutation({
    mutationFn: () => api.del('/auth/account'),
    onSuccess: () => {
      location.href = '/';
    },
    onError: (e) => {
      const body = (e as { body?: { code?: string; organizations?: string[] } }).body;
      setDeleteError(
        body?.code === 'last_manager'
          ? `You are the last manager of: ${(body.organizations ?? []).join(', ')}. Transfer management or offboard first.`
          : 'Deletion failed.',
      );
    },
  });

  if (!me || me.tier !== 'user') return null;
  const draft = name ?? me.name ?? '';
  return (
    <div className="max-w-lg space-y-6">
      <h1 className="text-2xl font-semibold">Account</h1>
      <Card>
        <CardHeader><CardTitle>Profile</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <div className="space-y-1">
            <Label>Email</Label>
            <Input value={me.email} disabled />
          </div>
          <div className="space-y-1">
            <Label htmlFor="account-name">Name</Label>
            <Input id="account-name" value={draft} onChange={(e) => setName(e.target.value)} />
          </div>
          <Button
            disabled={draft === (me.name ?? '') || !draft.trim() || rename.isPending}
            onClick={() => rename.mutate(draft.trim())}
          >
            Save
          </Button>
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Sign-in &amp; security</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Your password and multi-factor sign-in are managed by your identity provider.
          </p>
          <Button
            variant="outline"
            disabled={passwordReset.isPending}
            onClick={() => passwordReset.mutate()}
          >
            {passwordReset.isSuccess ? 'Reset email sent' : 'Send password reset email'}
          </Button>
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Sessions</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          {sessions?.map((s) => (
            <div key={s.id} className="flex items-center justify-between gap-2 text-sm">
              <div className="min-w-0">
                <div className="truncate">{s.userAgent ?? 'Unknown browser'}</div>
                <div className="text-xs text-muted-foreground">
                  {new Date(s.createdAt).toLocaleString()}
                  {s.current && ' · this session'}
                </div>
              </div>
              {!s.current && (
                <Button variant="ghost" size="sm" disabled={revoke.isPending}
                  onClick={() => revoke.mutate(s.id)}>
                  Revoke
                </Button>
              )}
            </div>
          ))}
          {sessions && sessions.length > 1 && (
            <Button variant="outline" size="sm" disabled={revokeOthers.isPending}
              onClick={() => revokeOthers.mutate()}>
              Sign out other sessions
            </Button>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle className="text-destructive">Danger zone</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Deleting your account removes your access everywhere and your identity provider
            record. Organizations you manage alone must be handed over or offboarded first.
          </p>
          {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
          <Button
            variant="destructive"
            disabled={deleteAccount.isPending}
            onClick={() => {
              if (window.confirm('Delete your account? This cannot be undone.'))
                deleteAccount.mutate();
            }}
          >
            Delete account
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}

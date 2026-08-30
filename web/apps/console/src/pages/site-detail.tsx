import { api } from '@premise/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input, Label,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@premise/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDayInZone, fmtTimeInZone } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { weeklySchedule, type DayCode } from '../lib/schedule';
import { can, useMe } from '../session';
import { StatusBadge } from '../shell';

type Site = { id: string; name: string; timeZone: string; status: string };
type Schedule = {
  id: string; name: string; rRule: string;
  anchorDate: string; opens: string; closes: string; exDates: string[];
};
type Window = { startsAtUtc: string; endsAtUtc: string; localDate: string };

const DAYS = [
  { code: 'MO', label: 'Mon' }, { code: 'TU', label: 'Tue' }, { code: 'WE', label: 'Wed' },
  { code: 'TH', label: 'Thu' }, { code: 'FR', label: 'Fri' }, { code: 'SA', label: 'Sat' },
  { code: 'SU', label: 'Sun' },
] as const;

function describeRule(rrule: string): string {
  const byday = /BYDAY=([A-Z,]+)/.exec(rrule)?.[1];
  if (rrule.includes('FREQ=DAILY')) return 'Every day';
  if (byday) {
    const codes = byday.split(',');
    if (codes.length === 7) return 'Every day';
    if (codes.join(',') === 'MO,TU,WE,TH,FR') return 'Weekdays';
    if (codes.join(',') === 'SA,SU') return 'Weekends';
    const labels = codes.map((code) => DAYS.find((d) => d.code === code)?.label ?? code);
    return labels.join(', ');
  }
  return rrule;
}

export function SiteDetailPage() {
  const { siteId } = useParams({ strict: false }) as { siteId: string };
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const manage = can(me, 'sites:manage');

  const { data: site } = useQuery({
    queryKey: ['site', siteId],
    queryFn: () => api.get<Site>(`/api/sites/${siteId}`),
  });
  const { data: schedules } = useQuery({
    queryKey: ['schedules', siteId],
    queryFn: () => api.get<Schedule[]>(`/api/sites/${siteId}/schedules`),
  });
  const { data: windows } = useQuery({
    queryKey: ['windows', siteId],
    queryFn: () => api.get<Window[]>(`/api/sites/${siteId}/windows?days=7`),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['site', siteId] });
    void queryClient.invalidateQueries({ queryKey: ['schedules', siteId] });
    // the projection rebuilds async: refresh the preview shortly after
    setTimeout(
      () => void queryClient.invalidateQueries({ queryKey: ['windows', siteId] }),
      1500,
    );
  };

  const update = useApiMutation({
    mutationFn: (body: Partial<{ name: string; timeZone: string; status: string }>) =>
      api.post(`/api/sites/${siteId}`, body),
    onSuccess: invalidate,
  });
  const addSchedule = useApiMutation({
    mutationFn: (body: object) => api.post(`/api/sites/${siteId}/schedules`, body),
    success: 'Hours added',
    onSuccess: invalidate,
  });
  const removeSchedule = useApiMutation({
    mutationFn: (scheduleId: string) =>
      api.del(`/api/sites/${siteId}/schedules/${scheduleId}`),
    success: 'Hours removed',
    onSuccess: invalidate,
  });

  const [days, setDays] = useState<string[]>(['MO', 'TU', 'WE', 'TH', 'FR']);
  const [opens, setOpens] = useState('09:00');
  const [closes, setCloses] = useState('17:00');
  const [scheduleName, setScheduleName] = useState('Regular hours');

  if (!site) return <div className="text-muted-foreground">Loading…</div>;
  const closed = site.status === 'Closed';

  return (
    <div className="max-w-3xl space-y-6">
      <Link to="/sites" className="text-sm text-muted-foreground hover:underline">
        ← All sites
      </Link>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{site.name}</h1>
          <StatusBadge status={site.status} />
        </div>
        {manage && (
          <Button
            variant={closed ? 'default' : 'destructive'}
            size="sm"
            disabled={update.isPending}
            onClick={() => update.mutate({ status: closed ? 'Open' : 'Closed' })}
          >
            {closed ? 'Reopen site' : 'Close site'}
          </Button>
        )}
      </div>
      <p className="text-sm text-muted-foreground">Time zone: {site.timeZone}</p>

      <Card>
        <CardHeader><CardTitle>Operating hours</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Days</TableHead>
                <TableHead>Hours (local)</TableHead>
                {manage && <TableHead className="w-28" />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {schedules?.map((s) => (
                <TableRow key={s.id}>
                  <TableCell>{s.name}</TableCell>
                  <TableCell className="text-muted-foreground">{describeRule(s.rRule)}</TableCell>
                  <TableCell>{s.opens.slice(0, 5)} – {s.closes.slice(0, 5)}</TableCell>
                  {manage && (
                    <TableCell className="w-28 text-right">
                      <ConfirmButton size="sm" disabled={removeSchedule.isPending}
                        onConfirm={() => removeSchedule.mutate(s.id)}>
                        Remove
                      </ConfirmButton>
                    </TableCell>
                  )}
                </TableRow>
              ))}
              {schedules?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    No hours defined.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>

          {manage && (
            <div className="space-y-3 border-t pt-4">
              <div className="flex gap-1">
                {DAYS.map((d) => (
                  <Button
                    key={d.code}
                    size="sm"
                    variant={days.includes(d.code) ? 'default' : 'outline'}
                    onClick={() =>
                      setDays(days.includes(d.code)
                        ? days.filter((x) => x !== d.code)
                        : [...days, d.code])
                    }
                  >
                    {d.label}
                  </Button>
                ))}
              </div>
              <div className="flex items-end gap-3">
                <div className="flex-1 space-y-1">
                  <Label htmlFor="sched-name">Name</Label>
                  <Input id="sched-name" value={scheduleName}
                    onChange={(e) => setScheduleName(e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="sched-opens">Opens</Label>
                  <Input id="sched-opens" type="time" value={opens}
                    onChange={(e) => setOpens(e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="sched-closes">Closes</Label>
                  <Input id="sched-closes" type="time" value={closes}
                    onChange={(e) => setCloses(e.target.value)} />
                </div>
                <Button
                  disabled={days.length === 0 || !scheduleName || addSchedule.isPending}
                  onClick={() =>
                    addSchedule.mutate({
                      name: scheduleName,
                      ...weeklySchedule([...days] as DayCode[], Date.now()),
                      opens,
                      closes,
                    })
                  }
                >
                  Add hours
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Open this week</CardTitle></CardHeader>
        <CardContent>
          {windows?.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No open windows in the next 7 days.
            </p>
          ) : (
            <ul className="space-y-1 text-sm">
              {windows?.map((w) => (
                <li key={w.startsAtUtc} className="flex justify-between">
                  <span>{fmtDayInZone(w.startsAtUtc, site.timeZone)}</span>
                  <span className="text-muted-foreground">
                    {/* the SITE's clock, not the viewer's (UX review P0) */}
                    {fmtTimeInZone(w.startsAtUtc, site.timeZone)}
                    {' – '}
                    {fmtTimeInZone(w.endsAtUtc, site.timeZone)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

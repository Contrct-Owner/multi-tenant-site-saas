import { createFileRoute, Link } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { publicApi, type PublicSiteDetail } from '../api';

const fetchSite = createServerFn({ method: 'GET' })
  .inputValidator((siteId: string) => siteId)
  .handler(({ data }) => publicApi<PublicSiteDetail | null>(`/public/sites/${data}`, null));

export const Route = createFileRoute('/sites/$siteId')({
  loader: ({ params }) => fetchSite({ data: params.siteId }),
  component: SitePage,
});

function SitePage() {
  const site = Route.useLoaderData();
  if (!site) {
    return (
      <main className="mx-auto max-w-2xl space-y-4 px-6 py-16">
        <h1 className="text-2xl font-semibold">Location not found</h1>
        <Link to="/" className="text-sm underline underline-offset-4">
          Back to all locations
        </Link>
      </main>
    );
  }

  // group this week's windows by local date for a readable hours card
  const byDate = new Map<string, { start: string; end: string }[]>();
  for (const w of site.windows) {
    const list = byDate.get(w.localDate) ?? [];
    list.push({
      start: new Date(w.startsAtUtc).toLocaleTimeString([], {
        hour: '2-digit',
        minute: '2-digit',
        timeZone: site.timeZone,
      }),
      end: new Date(w.endsAtUtc).toLocaleTimeString([], {
        hour: '2-digit',
        minute: '2-digit',
        timeZone: site.timeZone,
      }),
    });
    byDate.set(w.localDate, list);
  }

  return (
    <main className="mx-auto max-w-2xl space-y-8 px-6 py-16">
      <Link to="/" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
        ← All locations
      </Link>
      <div className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">{site.name}</h1>
        <p className={site.openNow ? 'font-medium text-primary' : 'text-muted-foreground'}>
          {site.status === 'ComingSoon' ? 'Coming soon' : site.openNow ? 'Open now' : 'Closed'}
        </p>
        {(site.addressLine1 || site.city) && (
          <p className="text-sm text-muted-foreground">
            {[site.addressLine1, site.city, site.postalCode].filter(Boolean).join(', ')}
          </p>
        )}
      </div>
      <section className="space-y-3">
        <h2 className="text-lg font-medium">Hours this week</h2>
        {byDate.size === 0 ? (
          <p className="text-sm text-muted-foreground">Hours are not published.</p>
        ) : (
          <ul className="divide-y rounded-lg border bg-card text-sm">
            {[...byDate.entries()].map(([date, spans]) => (
              <li key={date} className="flex justify-between px-4 py-2.5">
                <span>
                  {new Date(`${date}T00:00:00`).toLocaleDateString([], {
                    weekday: 'long',
                    month: 'short',
                    day: 'numeric',
                  })}
                </span>
                <span className="text-muted-foreground">
                  {spans.map((s) => `${s.start} – ${s.end}`).join(', ')}
                </span>
              </li>
            ))}
          </ul>
        )}
        <p className="text-xs text-muted-foreground">
          All times local to the location ({site.timeZone}).
        </p>
      </section>
    </main>
  );
}

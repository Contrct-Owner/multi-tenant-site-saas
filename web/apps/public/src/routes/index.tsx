import { createFileRoute, Link } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { publicApi, type PublicMe, type PublicSite } from '../api';

const fetchLocator = createServerFn({ method: 'GET' }).handler(async () => ({
  sites: await publicApi<PublicSite[]>('/public/sites', []),
  me: await publicApi<PublicMe>('/me', { tier: 'guest' }),
}));

export const Route = createFileRoute('/')({
  loader: () => fetchLocator(),
  component: Locator,
});

function Locator() {
  const { sites, me } = Route.useLoaderData();
  return (
    <main className="mx-auto max-w-2xl space-y-8 px-6 py-16">
      {me.tier === 'contact' && me.email && (
        <p className="rounded-md border bg-card px-4 py-2 text-sm text-muted-foreground">
          You&apos;re viewing as <span className="font-medium text-foreground">{me.email}</span>
        </p>
      )}
      <div className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">Our locations</h1>
        <p className="text-muted-foreground">
          {sites.length === 0
            ? 'No locations to show for this address.'
            : 'Find a location and check today’s hours.'}
        </p>
      </div>
      <ul className="divide-y rounded-lg border bg-card">
        {sites.map((site) => (
          <li key={site.id}>
            <Link
              to="/sites/$siteId"
              params={{ siteId: site.id }}
              className="flex items-center justify-between px-4 py-3 hover:bg-accent"
            >
              <span>
                <span className="font-medium">{site.name}</span>
                {site.city && (
                  <span className="ml-2 text-sm text-muted-foreground">{site.city}</span>
                )}
              </span>
              <span
                className={`text-sm ${site.openNow ? 'text-primary' : 'text-muted-foreground'}`}
              >
                {site.status === 'ComingSoon'
                  ? 'Coming soon'
                  : site.openNow
                    ? 'Open now'
                    : 'Closed'}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </main>
  );
}

import { createFileRoute } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';

// The GUEST surface (ADRs 7/15): server-rendered, org derived from the request
// host by the API's guest pipeline. SSR fetch happens server-side against the
// internal API address.
const fetchOpenSites = createServerFn({ method: 'GET' }).handler(async () => {
  const apiBase = process.env.PREMISE_API ?? 'http://localhost:5293';
  try {
    const response = await fetch(`${apiBase}/api/sites/open-now`);
    if (!response.ok) return [];
    return (await response.json()) as { id: string; name: string; timeZone: string }[];
  } catch {
    return []; // API down: the public page still renders
  }
});

export const Route = createFileRoute('/')({
  loader: () => fetchOpenSites(),
  component: Home,
});

function Home() {
  const openSites = Route.useLoaderData();
  return (
    <main className="mx-auto max-w-2xl space-y-8 px-6 py-16">
      <div className="space-y-3">
        <h1 className="text-3xl font-semibold tracking-tight">Premise</h1>
        <p className="text-muted-foreground">
          The public surface: server-rendered for guests, org-scoped by host. Replace this page
          with your storefront, locator, or booking flow.
        </p>
      </div>
      <section className="space-y-2">
        <h2 className="text-lg font-medium">Open right now</h2>
        {openSites.length === 0 ? (
          <p className="text-sm text-muted-foreground">No locations are open at the moment.</p>
        ) : (
          <ul className="divide-y rounded-lg border bg-card">
            {openSites.map((site) => (
              <li key={site.id} className="flex justify-between px-4 py-3 text-sm">
                <span className="font-medium">{site.name}</span>
                <span className="text-muted-foreground">{site.timeZone}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  );
}

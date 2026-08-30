import { createFileRoute } from '@tanstack/react-router';
import { getRequestHeader } from '@tanstack/react-start/server';
import { publicApi, type PublicSite } from '../api';

/** The one surface where SEO is table stakes: the org's locator + site pages. */
export const Route = createFileRoute('/sitemap.xml')({
  server: {
    handlers: {
      GET: async () => {
        const host = getRequestHeader('host') ?? 'localhost';
        const sites = await publicApi<PublicSite[]>('/public/sites', []);
        const urls = [
          `https://${host}/`,
          ...sites.map((s) => `https://${host}/sites/${s.id}`),
        ];
        const xml =
          '<?xml version="1.0" encoding="UTF-8"?>\n' +
          '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
          urls.map((u) => `  <url><loc>${u}</loc></url>`).join('\n') +
          '\n</urlset>\n';
        return new Response(xml, { headers: { 'Content-Type': 'application/xml' } });
      },
    },
  },
});

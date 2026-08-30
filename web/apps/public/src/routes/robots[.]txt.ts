import { createFileRoute } from '@tanstack/react-router';
import { getRequestHeader } from '@tanstack/react-start/server';

export const Route = createFileRoute('/robots.txt')({
  server: {
    handlers: {
      GET: () => {
        const host = getRequestHeader('host') ?? 'localhost';
        return new Response(
          `User-agent: *\nAllow: /\nSitemap: https://${host}/sitemap.xml\n`,
          { headers: { 'Content-Type': 'text/plain' } },
        );
      },
    },
  },
});

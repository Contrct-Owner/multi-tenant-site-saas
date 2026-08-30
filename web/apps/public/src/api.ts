import { getRequestHeader } from '@tanstack/react-start/server';

/**
 * Server-side fetch to the API, forwarding the browser's host so the guest
 * pipeline derives the org from {org-slug}.domain (ADR 7). API down or org
 * unknown degrades to empty - the public page always renders.
 */
export async function publicApi<T>(path: string, fallback: T): Promise<T> {
  const apiBase = process.env.PREMISE_API ?? 'http://localhost:5293';
  try {
    const host = getRequestHeader('host');
    // the browser's cookie rides along: an identified contact stays
    // identified through the SSR hop
    const cookie = getRequestHeader('cookie');
    const response = await fetch(`${apiBase}${path}`, {
      headers: {
        ...(host ? { 'X-Forwarded-Host': host } : {}),
        ...(cookie ? { cookie } : {}),
      },
    });
    if (!response.ok) return fallback;
    return (await response.json()) as T;
  } catch {
    return fallback;
  }
}

export type PublicSite = {
  id: string;
  name: string;
  city?: string;
  timeZone: string;
  status: string;
  openNow: boolean;
};

export type PublicSiteDetail = PublicSite & {
  addressLine1?: string;
  postalCode?: string;
  countryCode?: string;
  windows: { startsAtUtc: string; endsAtUtc: string; localDate: string }[];
};

export type PublicMe = { tier: string; email?: string };

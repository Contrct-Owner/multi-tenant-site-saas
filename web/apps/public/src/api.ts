import { getRequestHeader, setCookie } from '@tanstack/react-start/server';

/** Revoke upstream before relaying cookie deletions to this public host. */
export async function publicSignOut(): Promise<{ ok: boolean; error?: string }> {
  const cookie = getRequestHeader('cookie');
  try {
    const response = await fetch(`${process.env.PREMISE_API ?? 'http://localhost:5293'}/auth/logout`, {
      method: 'POST',
      signal: AbortSignal.timeout(30_000),
      headers: cookie ? { cookie } : {},
      redirect: 'manual',
    });
    const deletions = response.headers.getSetCookie();
    if (response.status !== 204 || deletions.length === 0) throw new Error('Logout not confirmed');
    for (const raw of deletions) {
      const pair = raw.split(';')[0] ?? '';
      const eq = pair.indexOf('=');
      if (eq > 0) setCookie(pair.slice(0, eq), '', { path: '/', maxAge: 0 });
    }
  } catch {
    // A lost response is not proof of revocation, nor of local cookie removal.
    return { ok: false, error: 'We could not confirm sign-out. Your session may still be active. Please try again.' };
  }
  return { ok: true };
}

/**
 * Server-side fetch to the API, forwarding the browser's host so the guest
 * pipeline derives the org from {org-slug}.domain (ADR 7). API down or org
 * unknown degrades to empty - the public page always renders.
 */
export async function publicApi<T>(path: string, fallback: T): Promise<T> {
  const value = await publicApiMaybe<T>(path);
  return value === undefined ? fallback : value;
}

export type PublicSite = {
  id: string;
  name: string;
  city?: string;
  timeZone: string;
  status: string;
  openNow: boolean;
  lat?: number | null;
  lng?: number | null;
  distanceKm?: number | null;
};

export type PublicSiteDetail = PublicSite & {
  attributes: { key: string; label: string; value: string | number | boolean }[];
  addressLine1?: string;
  postalCode?: string;
  countryCode?: string;
  windows: { startsAtUtc: string; endsAtUtc: string; localDate: string }[];
  closures: string[];
};

/**
 * Like publicApi, but undefined when the API is unreachable or refuses -
 * callers that must tell "org has nothing" from "backend is down" use this.
 */
export async function publicApiMaybe<T>(path: string): Promise<T | undefined> {
  const apiBase = process.env.PREMISE_API ?? 'http://localhost:5293';
  try {
    const host = getRequestHeader('host');
    const cookie = getRequestHeader('cookie');
    const response = await fetch(`${apiBase}${path}`, {
      signal: AbortSignal.timeout(30_000),
      headers: {
        ...(host ? { 'X-Forwarded-Host': host } : {}),
        ...(cookie ? { cookie } : {}),
      },
    });
    if (!response.ok) return undefined;
    return (await response.json()) as T;
  } catch {
    return undefined;
  }
}

export type PublicMe = { tier: string; email?: string };

/** A page of the locator: the sites and the cursor for the next page (null on a nearest-first page and at the end). */
export type PublicSiteList = { items: PublicSite[]; next: string | null };

const locatorPath = (near?: string, after?: string) => {
  const query = new URLSearchParams();
  if (near) query.set('near', near);
  if (after) query.set('after', after);
  const text = query.toString();
  return text ? `/public/sites?${text}` : '/public/sites';
};

/** Independent locator reads share a 30-second concurrent upstream budget. */
export async function publicLocator(near?: string) {
  const [page, me] = await Promise.all([
    publicApiMaybe<PublicSiteList>(locatorPath(near)),
    publicApi<PublicMe>('/me', { tier: 'guest' }),
  ]);
  return { sites: page?.items, next: page?.next ?? null, me };
}

/** The next page of the alphabetical list; empty when the API is away. */
export const publicLocatorPage = (after: string) =>
  publicApi<PublicSiteList>(locatorPath(undefined, after), { items: [], next: null });

/** Every public site, page by page, for the sitemap; capped at the 50,000 URLs a sitemap may hold. */
export async function publicSitesAll(): Promise<PublicSite[]> {
  const all: PublicSite[] = [];
  let after: string | undefined;
  do {
    const page = await publicApi<PublicSiteList>(`${locatorPath(undefined, after)}${after ? '&' : '?'}limit=200`, { items: [], next: null });
    all.push(...page.items);
    after = page.next ?? undefined;
  } while (after && all.length < 50_000);
  return all;
}

export type PublicOrg = { name: string; slug: string; brandColor?: string | null };

import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { sitesApi } from './api';

/** Site edits and hours/closure edits can all rebuild the async windows projection. */
export function useRefreshSite(siteId: string) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['site', siteId] });
    void queryClient.invalidateQueries({ queryKey: ['schedules', siteId] });
    void queryClient.invalidateQueries({ queryKey: ['windows', siteId] });
  };
}

export function useSites(
  filter: string,
  under: string | null = null,
  bbox?: string,
  zoom?: number,
) {
  return useInfiniteQuery({
    queryKey: ['sites', 'list', filter, under, bbox ?? null, zoom ?? null],
    queryFn: ({ pageParam, signal }) =>
      sitesApi.list(50, pageParam, filter || undefined, under ?? undefined, bbox, zoom, signal),
    initialPageParam: 0,
    getNextPageParam: (last) =>
      last.nextOffset == null ? undefined : Number(last.nextOffset),
  });
}

// a picker, not the management page: the shell has usually just read this
// tree (same key), and a hierarchy edit invalidates it - no refetch per mount
export const useHierarchy = () =>
  useQuery({
    queryKey: ['hierarchy'],
    queryFn: ({ signal }) => sitesApi.hierarchy(signal),
    staleTime: 5 * 60_000,
  });

// an org setting, edited on the settings page: fresh enough for a session
export const useBasemaps = (enabled = true) =>
  useQuery({
    queryKey: ['basemaps'],
    queryFn: ({ signal }) => sitesApi.basemaps(signal),
    enabled,
    staleTime: 5 * 60_000,
  });

export const useSite = (id: string) =>
  useQuery({ queryKey: ['site', id], queryFn: ({ signal }) => sitesApi.get(id, signal) });

export const useSiteSchedules = (id: string) =>
  useQuery({ queryKey: ['schedules', id], queryFn: ({ signal }) => sitesApi.schedules(id, signal) });

export const useSiteWindows = (id: string) =>
  useQuery({
    queryKey: ['windows', id],
    queryFn: ({ signal }) => sitesApi.windows(id, signal),
    // Async rebuilds can finish after invalidation; an empty result is valid too.
    // ponytail: 30 reads/min per visible preview; use projection notifications
    // if measured traffic warrants replacing foreground polling.
    refetchInterval: 2000,
  });

export const useSiteAttributes = () =>
  useQuery({ queryKey: ['site-attributes'], queryFn: ({ signal }) => sitesApi.attributes(signal) });

export const useSiteClosures = (id: string) =>
  useQuery({ queryKey: ['closures', id], queryFn: ({ signal }) => sitesApi.closures(id, signal) });

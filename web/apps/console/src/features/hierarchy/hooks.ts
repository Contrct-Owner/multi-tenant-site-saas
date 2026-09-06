import { api } from '@premise/api';
import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { persisted } from '../../app/persisted';
import { can, useMe } from '../../session';

export type Hierarchy = Awaited<ReturnType<typeof fetchHierarchy>>;
const fetchHierarchy = (signal?: AbortSignal) => api.get('/api/hierarchy', { signal });

/**
 * The org's hierarchy: one query for the shell's Scope tree, the pickers,
 * and the Hierarchy page (code review, 2026-09 - four definitions of the
 * same key disagreed about freshness). 404 means the org has none yet, so
 * there is no retry; a hierarchy edit invalidates the key. Remembered per
 * tab and org, so a reload starts from what the tab already knows.
 */
export function useHierarchy(options: { enabled?: boolean } = {}) {
  const { data: me } = useMe();
  const org = me?.tier === 'user' ? (me.activeOrg ?? 'none') : 'none';
  const store = persisted<Hierarchy>(`hierarchy.${org}`);
  const stored = store.read();
  const query = useQuery({
    queryKey: ['hierarchy'],
    queryFn: ({ signal }) => fetchHierarchy(signal),
    enabled: (options.enabled ?? true) && can(me, 'sites:read'),
    retry: false,
    staleTime: 5 * 60_000,
    initialData: stored?.data,
    initialDataUpdatedAt: stored?.at,
  });
  useEffect(() => {
    if (query.data && query.isFetched) store.write(query.data);
    // the store is derived from the org id; writing on every data change is the point
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query.data, query.isFetched, org]);
  return query;
}

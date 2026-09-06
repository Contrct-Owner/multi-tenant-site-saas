/**
 * A tab-scoped cache for reads the shell makes on EVERY full load and that
 * change rarely: the org's hierarchy behind the Scope panel, the API
 * version in the footer. Fed to React Query as initialData with a real
 * timestamp, so staleness is still the query's decision and invalidation
 * (a hierarchy edit) still refetches - this only stops a reload from paying
 * for what the tab already knows. sessionStorage: one tab, one session,
 * nothing outlives the browser window. Keyed by org so a switch never reads
 * another tenant's tree.
 */
type Stored<T> = { data: T; at: number };

const MAX_BYTES = 256 * 1024;

export function persisted<T>(key: string) {
  const fullKey = `premise.cache.${key}`;
  return {
    read(): Stored<T> | null {
      try {
        const raw = sessionStorage.getItem(fullKey);
        if (!raw) return null;
        const parsed = JSON.parse(raw) as Stored<T>;
        return typeof parsed.at === 'number' ? parsed : null;
      } catch {
        return null;
      }
    },
    write(data: T) {
      try {
        const json = JSON.stringify({ data, at: Date.now() });
        // a large tree is cheaper to fetch than to parse from storage on every
        // load, and sessionStorage quotas are small: past this it is not kept
        if (json.length > MAX_BYTES) {
          sessionStorage.removeItem(fullKey);
          return;
        }
        sessionStorage.setItem(fullKey, json);
      } catch {
        // a full or blocked store only costs the next reload a request
      }
    },
  };
}

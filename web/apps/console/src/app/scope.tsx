import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

/**
 * The console's global scope (direction B): a hierarchy node the whole app
 * reads under, chosen once in the shell's Scope panel. Null means the entire
 * grant. This narrows what pages ASK for (`under=` on the site list, ADR 49);
 * it never widens anything - gate 3 on the server still filters every query.
 * Per org, remembered in this browser only: a preference, not data.
 */
type Scope = { nodeId: string | null; setNodeId: (id: string | null) => void };

const ScopeContext = createContext<Scope | null>(null);

const storageKey = (orgId: string) => `premise.scope.${orgId}`;

function readStored(orgId: string): string | null {
  try {
    return localStorage.getItem(storageKey(orgId));
  } catch {
    return null;
  }
}

export function ScopeProvider({ orgId, children }: { orgId: string; children: ReactNode }) {
  const [nodeId, setState] = useState<string | null>(() => readStored(orgId));
  const setNodeId = useCallback(
    (id: string | null) => {
      setState(id);
      try {
        if (id === null) localStorage.removeItem(storageKey(orgId));
        else localStorage.setItem(storageKey(orgId), id);
      } catch {
        // storage is a convenience; the choice still holds for this session
      }
    },
    [orgId],
  );
  const value = useMemo(() => ({ nodeId, setNodeId }), [nodeId, setNodeId]);
  return <ScopeContext.Provider value={value}>{children}</ScopeContext.Provider>;
}

export function useScope(): Scope {
  const scope = useContext(ScopeContext);
  if (!scope) throw new Error('useScope outside ScopeProvider');
  return scope;
}

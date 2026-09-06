import { Button, Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, Kbd, KbdGroup } from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { MapPin, Search } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { sitesApi } from '../features/sites/api';
import { StatusBadge } from '../shell';

/**
 * Command-K search (the app-shell block's search menu, wired to sites):
 * open with the shortcut or the header button, type, pick a site, land on
 * it. Searches by name or city through the same list endpoint the Sites
 * page uses, so scope applies exactly as it does everywhere else.
 */
export function CommandSearch() {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const [active, setActive] = useState(0);
  const inputId = useId();
  const navigate = useNavigate();

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        setOpen(true);
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  const term = q.trim();
  const results = useQuery({
    queryKey: ['sites', 'search', term],
    queryFn: ({ signal }) => sitesApi.list(8, 0, term, undefined, undefined, undefined, signal),
    enabled: open && term.length > 0,
    staleTime: 30_000,
  });
  const items = results.data?.items ?? [];

  const go = (id: string) => {
    setOpen(false);
    setQ('');
    void navigate({ to: '/sites/$siteId', params: { siteId: id } });
  };

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label="Search sites"
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen(true)}
      >
        <Search className="size-4.5" aria-hidden />
      </Button>

      <Dialog
        open={open}
        onOpenChange={(next) => {
          setOpen(next);
          if (!next) setQ('');
        }}
      >
        <DialogContent className="max-w-md gap-0 p-0 sm:max-w-md" showCloseButton={false}>
          <DialogHeader className="sr-only">
            <DialogTitle>Search sites</DialogTitle>
            <DialogDescription>Type a site name or city, then pick one to open it.</DialogDescription>
          </DialogHeader>
          <div className="flex items-center gap-3 border-b px-4">
            <Search aria-hidden className="pointer-events-none size-4 opacity-60" />
            <input
              id={inputId}
              className="h-12 w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
              autoFocus
              placeholder="Search sites by name or city…"
              aria-label="Search sites"
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                setActive(0);
              }}
              onKeyDown={(e) => {
                if (e.key === 'ArrowDown') {
                  e.preventDefault();
                  setActive((i) => Math.min(i + 1, items.length - 1));
                } else if (e.key === 'ArrowUp') {
                  e.preventDefault();
                  setActive((i) => Math.max(i - 1, 0));
                } else if (e.key === 'Enter' && items[active]) {
                  go(items[active].id);
                }
              }}
            />
            <KbdGroup className="hidden sm:flex">
              <Kbd>Esc</Kbd>
            </KbdGroup>
          </div>
          <ul role="listbox" aria-label="Matching sites" className="max-h-80 overflow-auto p-1">
            {term.length === 0 && (
              <li className="px-3 py-6 text-center text-sm text-muted-foreground">
                Start typing to search the sites in your scope.
              </li>
            )}
            {term.length > 0 && results.isPending && (
              <li className="px-3 py-6 text-center text-sm text-muted-foreground">Searching…</li>
            )}
            {term.length > 0 && results.data && items.length === 0 && (
              <li className="px-3 py-6 text-center text-sm text-muted-foreground">No sites match.</li>
            )}
            {items.map((s, i) => (
              <li key={s.id} role="option" aria-selected={i === active}>
                <button
                  type="button"
                  className={`flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm hover:bg-muted ${i === active ? 'bg-muted' : ''}`}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => go(s.id)}
                >
                  <MapPin className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate font-medium">{s.name}</span>
                    {s.city && <span className="block truncate text-xs text-muted-foreground">{s.city}</span>}
                  </span>
                  <StatusBadge status={s.status} />
                </button>
              </li>
            ))}
          </ul>
          <div className="flex items-center gap-3 border-t px-4 py-2 text-xs text-muted-foreground">
            <KbdGroup>
              <Kbd>↑</Kbd>
              <Kbd>↓</Kbd>
            </KbdGroup>
            to move
            <KbdGroup>
              <Kbd>↵</Kbd>
            </KbdGroup>
            to open
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}

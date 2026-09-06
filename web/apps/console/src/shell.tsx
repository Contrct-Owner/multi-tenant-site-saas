import { api, ApiError, type Capability } from '@premise/api';
import {
  Alert,
  AlertDescription,
  Badge,
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
  cn,
  Field,
  FieldLabel,
  Input,
  Select,
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
  Toaster,
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@premise/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useRouterState } from '@tanstack/react-router';
import {
  Building2,
  Check,
  ChevronDown,
  ChevronRight,
  Code2,
  FolderOpen,
  Home,
  Layers,
  ListChecks,
  LogOut,
  MapPin,
  MoonStar,
  MoreHorizontal,
  Network,
  ScrollText,
  Server,
  Settings,
  Shield,
  Sun,
  Upload,
  UserRound,
  Users,
} from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type ComponentType, type CSSProperties, type ReactNode } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { can, parseMe, useMe, type Me } from './session';
import { persisted } from './app/persisted';
import { CommandSearch } from './components/command-search';
import { UserMenu } from './components/user-menu';
import { ScopeProvider, useScope } from './app/scope';
import { useSessionTransition } from './app/session-boundary';
import { currentTheme, toggleTheme, useTheme } from './app/theme';

/**
 * Direction B (2026-09-05): a 76px icon rail for the areas, a 240px inner
 * panel that holds SCOPE - the hierarchy node the whole console reads under -
 * and the page. Scope is chosen once here and every list asks under it
 * (`under=`, ADR 49); the server's gate 3 still filters on top. On a phone the
 * rail becomes a bottom tab bar (four areas plus More) and the scope panel
 * becomes a chip that opens a sheet.
 */
type NavItem = {
  to: string;
  label: string;
  capability: Capability | null;
  icon: ComponentType<{ className?: string }>;
};

// grouped by rhythm of use: daily operations, then org administration,
// then the operator wall (UX review P2)
const NAV_GROUPS: { label: string | null; items: NavItem[] }[] = [
  {
    label: null,
    items: [
      { to: '/', label: 'Dashboard', capability: null, icon: Home },
      { to: '/sites', label: 'Sites', capability: 'sites:read', icon: MapPin },
      { to: '/overlays', label: 'Overlays', capability: 'overlays:read', icon: Layers },
      { to: '/hierarchy', label: 'Hierarchy', capability: 'hierarchy:manage', icon: Network },
      { to: '/checklists', label: 'Checklists', capability: 'checklists:complete', icon: ListChecks },
      { to: '/files', label: 'Files', capability: 'files:read', icon: FolderOpen },
      { to: '/ingest', label: 'Ingest', capability: 'ingest:manage', icon: Upload },
    ],
  },
  {
    label: 'Administer',
    items: [
      { to: '/members', label: 'Members', capability: 'roles:manage', icon: Users },
      { to: '/roles', label: 'Roles', capability: 'roles:manage', icon: Shield },
      { to: '/developers', label: 'Developers', capability: 'org:manage', icon: Code2 },
      { to: '/settings', label: 'Settings', capability: 'org:manage', icon: Settings },
      { to: '/audit', label: 'Audit', capability: 'audit:read', icon: ScrollText },
    ],
  },
  {
    label: 'Platform',
    items: [{ to: '/operator', label: 'Operator', capability: 'platform:operate', icon: Server }],
  },
];

/** The four areas that earn a slot on the phone's tab bar; the rest live under More. */
const TAB_BAR = ['/', '/sites', '/checklists', '/files'];

type User = Extract<Me, { tier: 'user' }>;

const visibleGroups = (me: User) =>
  NAV_GROUPS.map((g) => ({
    ...g,
    items: g.items.filter((n) => n.capability === null || can(me, n.capability)),
  })).filter((g) => g.items.length > 0);

const isActive = (path: string, to: string) =>
  to === '/' ? path === '/' : path === to || path.startsWith(to + '/');

const pageTitle = (path: string) => {
  if (path.startsWith('/account')) return 'Account';
  const hit = NAV_GROUPS.flatMap((g) => g.items).find((n) => isActive(path, n.to));
  return hit?.label ?? 'Premise';
};

export function Shell({ children }: { children: ReactNode }) {
  const theme = useTheme();
  const { data: me, isLoading, error, refetch } = useMe();

  if (isLoading) return <div className="p-12 text-muted-foreground">Loading session…</div>;

  if (error) return (
    <main className="p-12 space-y-4">
      <p role="alert">Could not verify your session. {error.message}</p>
      <Button onClick={() => { void refetch(); }}>Retry session verification</Button>
    </main>
  );

  if (me?.tier !== 'user') {
    return <SignInScreen />;
  }

  if (me.organizations.length === 0) {
    return <CreateOrgScreen />;
  }

  const activeOrg = me.organizations.find((o) => o.id === me.activeOrg);
  if (activeOrg && (activeOrg as { status?: string }).status === 'Suspended') {
    return (
      <main className="flex min-h-screen items-center justify-center bg-background p-4">
        <Card className="w-full max-w-sm text-center">
          <CardHeader>
            <CardTitle role="heading" aria-level={1}>{activeOrg.name} is suspended</CardTitle>
            <CardDescription>Contact support to restore access. Your data is retained.</CardDescription>
          </CardHeader>
        </Card>
      </main>
    );
  }

  return (
    <TooltipProvider>
      <ScopeProvider orgId={me.activeOrg ?? 'none'}>
        {/* shadcn Sidebar (the app-shell block's layout): the sidebar is fixed
            and scrolls itself, the page scrolls on its own, Cmd/Ctrl-B folds
            the Scope panel away to the icon rail */}
        <SidebarProvider
          className="bg-background"
          style={{ '--sidebar-width': '316px', '--sidebar-width-icon': '76px' } as CSSProperties}
        >
          <AppSidebar me={me} orgName={activeOrg?.name ?? 'No organization'} />
          <SidebarInset className="min-w-0">
            {me.impersonationExpiresAt && (
              <ImpersonationBanner
                orgName={activeOrg?.name ?? 'organization'}
                expiresAt={me.impersonationExpiresAt}
              />
            )}
            <Topbar me={me} />
            {/* SidebarInset IS the page's <main>; this is only its padding */}
            <div className="min-w-0 flex-1 p-4 pb-24 md:p-8 md:pb-8">{children}</div>
          </SidebarInset>
          <TabBar me={me} />
          <Toaster theme={theme} />
        </SidebarProvider>
      </ScopeProvider>
    </TooltipProvider>
  );
}

/* ----------------------------------------------------------------- desktop */

/**
 * Direction B on the shadcn Sidebar's double-sidebar pattern: one
 * icon-collapsible sidebar holding the 76px area rail and the 240px Scope
 * panel. Collapsed, only the rail stays; the trigger in the top bar and
 * Cmd/Ctrl-B toggle it. Nothing here is drawn by hand - rows are
 * SidebarMenuButtons, the org picker is the barrel Select.
 */
function AppSidebar({ me, orgName }: { me: User; orgName: string }) {
  const path = useRouterState({ select: (s) => s.location.pathname });
  const groups = visibleGroups(me);
  return (
    <Sidebar
      collapsible="icon"
      className="hidden overflow-hidden md:flex *:data-[sidebar=sidebar]:flex-row"
    >
      {/* the area rail: always icons, a tooltip names each */}
      <Sidebar collapsible="none" className="w-[calc(var(--sidebar-width-icon)+1px)]! border-r">
        <SidebarHeader className="items-center pt-3">
          <Link
            to="/"
            aria-label="Premise home"
            className="flex size-8 items-center justify-center rounded-lg bg-foreground text-sm font-extrabold text-background"
          >
            P
          </Link>
        </SidebarHeader>
        <SidebarContent>
          <nav aria-label="Areas" className="contents">
            {groups.map((group, i) => (
              <SidebarGroup key={group.label ?? 'operate'} className={cn('items-center px-0', i > 0 && 'border-t')}>
                <SidebarGroupContent>
                  <SidebarMenu className="items-center gap-1">
                    {group.items.map((n) => (
                      <RailLink key={n.to} item={n} active={isActive(path, n.to)} />
                    ))}
                  </SidebarMenu>
                </SidebarGroupContent>
              </SidebarGroup>
            ))}
          </nav>
        </SidebarContent>
      </Sidebar>
      {/* the Scope panel: what every list reads under */}
      <Sidebar
        collapsible="none"
        role="complementary"
        aria-label="Scope"
        className="hidden min-w-0 flex-1 md:flex"
      >
        <SidebarHeader className="gap-2 border-b p-3">
          <div className="flex items-center gap-2 px-1">
            <span className="flex size-6 items-center justify-center rounded-md bg-primary text-xs font-bold text-primary-foreground">
              {orgName.charAt(0).toUpperCase()}
            </span>
            <span className="truncate text-sm font-semibold">{orgName}</span>
          </div>
          <OrgSwitcher me={me} />
        </SidebarHeader>
        <SidebarContent className="p-2">
          <ScopeTree me={me} />
        </SidebarContent>
        <SidebarFooter className="border-t p-3">
          <div className="flex items-center justify-between gap-2">
            <span className="min-w-0">
              <Link
                to="/account"
                className="block truncate text-xs text-muted-foreground hover:text-foreground hover:underline"
              >
                {me.email}
              </Link>
              <ApiVersion />
            </span>
            <SignOutButton />
          </div>
        </SidebarFooter>
      </Sidebar>
    </Sidebar>
  );
}

function RailLink({ item, active }: { item: NavItem; active: boolean }) {
  const Icon = item.icon;
  return (
    <SidebarMenuItem>
      <Tooltip>
        <TooltipTrigger
          render={
            <SidebarMenuButton
              isActive={active}
              size="lg"
              className="size-11 justify-center rounded-[10px] border border-transparent p-0 data-[active=true]:border-sidebar-border"
              render={<Link to={item.to} aria-current={active ? 'page' : undefined} />}
            />
          }
        >
          <Icon className="size-[18px]" aria-hidden />
          <span className="sr-only">{item.label}</span>
        </TooltipTrigger>
        <TooltipContent side="right">{item.label}</TooltipContent>
      </Tooltip>
    </SidebarMenuItem>
  );
}

function Topbar({ me }: { me: User }) {
  const path = useRouterState({ select: (s) => s.location.pathname });
  const scope = useScope();
  const { data: hierarchy } = useHierarchyForScope(me);
  const scopeName =
    scope.nodeId === null
      ? 'All sites'
      : hierarchy?.nodes.find((n) => n.id === scope.nodeId)?.name ?? 'All sites';
  const title = pageTitle(path);
  return (
    <header className="sticky top-0 z-10 flex h-[52px] shrink-0 items-center gap-3 border-b bg-background px-4 md:px-5">
      {/* desktop: the panel toggle, then the breadcrumb - scope first, because it is what every list reads under */}
      <div className="hidden min-w-0 items-center gap-2 md:flex">
        <SidebarTrigger className="-ml-1" />
        <Breadcrumb>
          <BreadcrumbList>
            <BreadcrumbItem>
              <span className="truncate">{scopeName}</span>
            </BreadcrumbItem>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbPage>{title}</BreadcrumbPage>
            </BreadcrumbItem>
          </BreadcrumbList>
        </Breadcrumb>
      </div>
      {/* phone: the page, then the scope chip */}
      <div className="flex min-w-0 flex-1 items-center gap-2 md:hidden">
        <span className="shrink-0 text-base font-semibold">{title}</span>
        <ScopeSheet me={me} scopeName={scopeName} />
      </div>
      <div className="ml-auto flex items-center gap-1">
        <CommandSearch />
        <ThemeToggle />
        <UserMenu email={me.email} name={me.name} canManageOrg={can(me, 'org:manage')} />
      </div>
    </header>
  );
}

/* ------------------------------------------------------------------- phone */

function TabBar({ me }: { me: User }) {
  const path = useRouterState({ select: (s) => s.location.pathname });
  const items = visibleGroups(me).flatMap((g) => g.items);
  const tabs = items.filter((n) => TAB_BAR.includes(n.to));
  const more = items.filter((n) => !TAB_BAR.includes(n.to));
  return (
    <nav
      aria-label="Areas"
      className="fixed inset-x-0 bottom-0 z-40 flex border-t bg-sidebar pb-[env(safe-area-inset-bottom)] md:hidden"
    >
      {tabs.map((n) => {
        const Icon = n.icon;
        const active = isActive(path, n.to);
        return (
          <Link
            key={n.to}
            to={n.to}
            aria-current={active ? 'page' : undefined}
            className={cn(
              'flex flex-1 flex-col items-center gap-1 py-2 text-[10px] font-medium text-muted-foreground',
              active && 'text-foreground',
            )}
          >
            <span
              className={cn(
                'flex h-7 w-11 items-center justify-center rounded-full',
                active && 'bg-accent text-primary',
              )}
            >
              <Icon className="size-[18px]" />
            </span>
            {n.label}
          </Link>
        );
      })}
      <Sheet>
        <SheetTrigger
          render={
            <button
              type="button"
              className="flex flex-1 flex-col items-center gap-1 py-2 text-[10px] font-medium text-muted-foreground"
            />
          }
        >
          <span className="flex h-7 w-11 items-center justify-center rounded-full">
            <MoreHorizontal className="size-[18px]" />
          </span>
          More
        </SheetTrigger>
        <SheetContent side="bottom" className="max-h-[85vh] overflow-auto rounded-t-2xl pb-8">
          <SheetHeader className="pb-0">
            <SheetTitle>{me.organizations.find((o) => o.id === me.activeOrg)?.name}</SheetTitle>
          </SheetHeader>
          <div className="px-4">
            <OrgSwitcher me={me} />
          </div>
          <nav aria-label="More areas" className="flex flex-col">
            {more.map((n) => {
              const Icon = n.icon;
              return (
                <Link
                  key={n.to}
                  to={n.to}
                  className="flex h-12 items-center gap-3 border-b px-4 text-[15px] last:border-b-0"
                >
                  <Icon className="size-[18px] text-muted-foreground" />
                  <span className="flex-1">{n.label}</span>
                </Link>
              );
            })}
          </nav>
          <div className="flex items-center gap-3 px-4 pt-2">
            <span className="flex size-9 items-center justify-center rounded-full border bg-card text-xs font-semibold">
              {initials(me)}
            </span>
            <span className="min-w-0 flex-1 leading-tight">
              <Link to="/account" className="block truncate text-sm font-medium hover:underline">
                {me.name ?? me.email}
              </Link>
              <span className="block truncate text-xs text-muted-foreground">{me.email}</span>
            </span>
            <SignOutButton />
          </div>
        </SheetContent>
      </Sheet>
    </nav>
  );
}

function ScopeSheet({ me, scopeName }: { me: User; scopeName: string }) {
  // controlled so a pick closes the sheet: on a phone the choice IS the action
  const [open, setOpen] = useState(false);
  if (!can(me, 'sites:read')) return null;
  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger
        render={
          <Button variant="outline" size="sm" className="min-w-0 flex-1 justify-start gap-2" />
        }
      >
        <MapPin className="size-4 text-muted-foreground" />
        <span className="text-muted-foreground">Scope</span>
        <span className="truncate font-semibold">{scopeName}</span>
        <ChevronDown className="ml-auto size-4 text-muted-foreground" />
      </SheetTrigger>
      <SheetContent side="bottom" className="max-h-[85vh] overflow-auto rounded-t-2xl pb-8">
        <SheetHeader>
          <SheetTitle>Scope</SheetTitle>
        </SheetHeader>
        <div className="px-2">
          <ScopeTree
            me={me}
            rowClass="h-11 text-[15px]"
            rowHeight={46}
            className="max-h-[65vh]"
            onPick={() => setOpen(false)}
          />
        </div>
      </SheetContent>
    </Sheet>
  );
}

/* ------------------------------------------------------------------ shared */

type Hierarchy = Awaited<ReturnType<typeof fetchHierarchy>>;
const fetchHierarchy = (signal?: AbortSignal) => api.get('/api/hierarchy', { signal });

/**
 * The hierarchy behind the Scope panel. 404 means the org has not provisioned
 * one yet (the Hierarchy page does that); every other error is a real one.
 * Remembered per tab and org: the shell reads this on every full load, so a
 * reload starts from what the tab already knows and only refetches when the
 * data is stale or a hierarchy edit invalidates it (same query key as the
 * Hierarchy page).
 */
function useHierarchyForScope(me: User) {
  const store = persisted<Hierarchy>(`hierarchy.${me.activeOrg ?? 'none'}`);
  const stored = store.read();
  const query = useQuery({
    queryKey: ['hierarchy'],
    queryFn: ({ signal }) => fetchHierarchy(signal),
    enabled: can(me, 'sites:read'),
    retry: false,
    staleTime: 5 * 60_000,
    initialData: stored?.data,
    initialDataUpdatedAt: stored?.at,
  });
  useEffect(() => {
    if (query.data && query.isFetched) store.write(query.data);
    // store is derived from the org id; writing on every data change is the point
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query.data, query.isFetched, me.activeOrg]);
  return query;
}

type ScopeRow = {
  id: string | null;
  name: string;
  depth: number;
  icon: ComponentType<{ className?: string }>;
  hasChildren: boolean;
  expanded: boolean;
};

/**
 * The scope picker: the org's tree with the top level open and everything
 * below it closed until asked for, rendered through a virtualizer so the
 * rows on screen, not the nodes in the org, decide the cost (ADR 51's
 * console half). The chosen node's ancestors stay open so the choice is
 * always visible.
 */
function ScopeTree({
  me,
  rowClass,
  rowHeight = 32,
  className,
  onPick,
}: {
  me: User;
  rowClass?: string;
  rowHeight?: number;
  className?: string;
  onPick?: () => void;
}) {
  const scope = useScope();
  const { data: hierarchy, isError, error } = useHierarchyForScope(me);
  // nodes whose default (open at the top, closed below) has been flipped
  const [toggled, setToggled] = useState<Set<string>>(() => new Set());
  const rootIdRef = useRef<string | null>(null);
  const nodes = hierarchy?.nodes ?? [];
  const rows = useMemo<ScopeRow[]>(() => {
    const children = new Map<string | null, typeof nodes>();
    const byId = new Map(nodes.map((n) => [n.id, n]));
    for (const n of nodes) children.set(n.parentId ?? null, [...(children.get(n.parentId ?? null) ?? []), n]);
    const ancestors = new Set<string>();
    for (let cursor = scope.nodeId ? byId.get(scope.nodeId) : undefined; cursor?.parentId; cursor = byId.get(cursor.parentId))
      ancestors.add(cursor.parentId);
    const isOpen = (n: (typeof nodes)[number]) =>
      ancestors.has(n.id) || (Number(n.depth) === 0) !== toggled.has(n.id);
    const rootId = nodes.find((n) => Number(n.depth) === 0)?.id ?? null;
    // the root node IS "all sites": one row, selected as the whole org
    const out: ScopeRow[] = [];
    const visit = (parent: string | null) => {
      for (const n of children.get(parent) ?? []) {
        const depth = Number(n.depth);
        const kids = children.get(n.id)?.length ?? 0;
        const expanded = kids > 0 && isOpen(n);
        out.push({
          id: depth === 0 ? null : n.id,
          name: n.name,
          depth,
          icon: depth === 0 ? Building2 : depth === 1 ? Network : MapPin,
          hasChildren: kids > 0,
          expanded,
        });
        if (expanded) visit(n.id);
      }
    };
    visit(null);
    rootIdRef.current = rootId;
    if (out.length === 0) out.push({ id: null, name: 'All sites', depth: 0, icon: Building2, hasChildren: false, expanded: false });
    return out;
  }, [nodes, toggled, scope.nodeId]);
  const scroller = useRef<HTMLDivElement>(null);
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 12,
  });
  if (!can(me, 'sites:read')) return null;
  const notProvisioned = error instanceof ApiError && error.status === 404;
  return (
    <div className="flex min-h-0 flex-col">
      <div className="px-2 pb-1 pt-2 text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
        Scope
      </div>
      <div ref={scroller} className={cn('min-h-0 flex-1 overflow-auto', className)}>
        <div
          role="group"
          aria-label="Scope"
          className="relative w-full"
          style={{ height: virtualizer.getTotalSize() }}
        >
          {virtualizer.getVirtualItems().map((item) => {
            const row = rows[item.index]!;
            const selected = scope.nodeId === row.id;
            const Icon = row.icon;
            return (
              <div
                key={row.id ?? 'all'}
                className="absolute left-0 top-0 flex w-full items-center"
                style={{ height: item.size, transform: `translateY(${item.start}px)` }}
              >
                {row.hasChildren ? (
                  <button
                    type="button"
                    aria-label={`${row.expanded ? 'Collapse' : 'Expand'} ${row.name}`}
                    aria-expanded={row.expanded}
                    onClick={() =>
                      setToggled((prev) => {
                        const key = row.id ?? rootIdRef.current ?? '';
                        const next = new Set(prev);
                        if (next.has(key)) next.delete(key);
                        else next.add(key);
                        return next;
                      })
                    }
                    style={{ marginLeft: `${row.depth * 14}px` }}
                    className="flex size-5 shrink-0 items-center justify-center rounded text-muted-foreground hover:bg-sidebar-accent"
                  >
                    <ChevronRight
                      className={cn('size-3.5 transition-transform', row.expanded && 'rotate-90')}
                      aria-hidden
                    />
                  </button>
                ) : (
                  <span className="size-5 shrink-0" style={{ marginLeft: `${row.depth * 14}px` }} />
                )}
                <button
                  type="button"
                  aria-pressed={selected}
                  onClick={() => {
                    scope.setNodeId(row.id);
                    onPick?.();
                  }}
                  className={cn(
                    'flex h-[30px] min-w-0 flex-1 items-center gap-2 rounded-md px-1.5 text-left text-sm text-foreground/80 hover:bg-sidebar-accent',
                    selected && 'bg-sidebar-accent font-medium text-foreground shadow-xs',
                    rowClass,
                  )}
                >
                  <Icon className="size-[14px] shrink-0 text-muted-foreground" />
                  <span className="min-w-0 flex-1 truncate">{row.name}</span>
                  {selected && <Check className="size-4 text-primary" aria-hidden />}
                </button>
              </div>
            );
          })}
        </div>
      </div>
      {isError && !notProvisioned && (
        <p className="px-2 pt-2 text-xs text-muted-foreground">Scope could not be loaded.</p>
      )}
      {(!isError || notProvisioned) && nodes.length === 0 && (
        <p className="px-2 pt-2 text-xs text-muted-foreground">
          No hierarchy yet. Scope applies to every screen; it filters, it never blocks.
        </p>
      )}
    </div>
  );
}

function OrgSwitcher({ me }: { me: User }) {
  const changeSession = useSessionTransition();
  if (me.organizations.length <= 1) return null;
  return (
    <Select
      aria-label="Active organization"
      className="w-full"
      value={me.activeOrg ?? ''}
      onChange={async (e) => {
        const orgId = e.target.value;
        await changeSession(() => api.post('/auth/switch-org', { orgId }));
      }}
    >
      {me.organizations.map((o) => (
        <option key={o.id} value={o.id}>
          {o.name}
        </option>
      ))}
    </Select>
  );
}

function SignOutButton() {
  const changeSession = useSessionTransition();
  return (
    <Button
      variant="ghost"
      size="sm"
      onClick={async () => {
        await changeSession(() => api.post('/auth/logout'));
      }}
    >
      <LogOut className="size-4" aria-hidden />
      Sign out
    </Button>
  );
}

function ThemeToggle() {
  const [theme, setTheme] = useState(() => currentTheme());
  const next = theme === 'dark' ? 'light' : 'dark';
  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label={`Switch to ${next} theme`}
      onClick={() => setTheme(toggleTheme())}
    >
      {theme === 'dark' ? <Sun className="size-4" /> : <MoonStar className="size-4" />}
    </Button>
  );
}

const initials = (me: User) =>
  (me.name ?? me.email)
    .split(/[\s@._-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('') || <UserRound className="size-4" />;

/** "What version are you running?" - answerable from any screenshot (maturity review, hole 4). */
function ApiVersion() {
  // one read per tab: the version changes on deploy, and a deploy reloads the tab
  const store = persisted<{ version?: string }>('healthz');
  const stored = store.read();
  const { data, isFetched } = useQuery({
    queryKey: ['healthz'],
    queryFn: ({ signal }) => api.get('/healthz', { signal }),
    staleTime: Infinity,
    initialData: stored?.data,
    initialDataUpdatedAt: stored?.at,
  });
  useEffect(() => {
    if (data && isFetched) store.write(data);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, isFetched]);
  if (!data?.version) return null;
  // muted at full opacity and 12px: the 10px/70% version of this failed contrast on every page (axe)
  return <span className="block truncate text-xs text-muted-foreground">{data.version}</span>;
}

function ImpersonationBanner({ orgName, expiresAt }: { orgName: string; expiresAt: string }) {
  const changeSession = useSessionTransition();
  const ends = new Date(expiresAt).toLocaleTimeString(undefined, {
    hour: 'numeric',
    minute: '2-digit',
  });
  return (
    <div className="flex items-center justify-between gap-3 bg-warning px-4 py-2 text-sm text-warning-foreground">
      <span>
        <span className="font-semibold">Support session:</span> impersonating {orgName} · ends{' '}
        {ends}
      </span>
      <Button
        variant="outline"
        size="sm"
        onClick={async () => {
          await changeSession(() => api.post('/auth/impersonation/stop'));
        }}
      >
        Stop impersonating
      </Button>
    </div>
  );
}

function SignInScreen() {
  const authError = new URLSearchParams(location.search).get('authError');
  const [signupEmail, setSignupEmail] = useState<string | null>(null);
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background p-4">
      {/* the auth block's shape: a wordmark header, one card, one primary action */}
      <div className="flex items-center gap-2">
        <span className="flex size-8 items-center justify-center rounded-lg bg-foreground text-sm font-extrabold text-background">
          P
        </span>
        <span className="text-lg font-semibold">Premise</span>
      </div>
      <Card className="w-full max-w-sm">
        <CardHeader className="text-center">
          <CardTitle role="heading" aria-level={1}>Premise Console</CardTitle>
          <CardDescription>Sign in to manage your organization.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {authError && (
            <Alert variant="destructive">
              <AlertDescription>
                {authError === 'user_not_found'
                  ? 'No account for that email yet - use Create account below.'
                  : `Sign-in didn't complete (${authError.replaceAll('_', ' ')}). Try again.`}
              </AlertDescription>
            </Alert>
          )}
          <Button
            className="w-full"
            render={<a href={`/auth/login?returnUrl=${encodeURIComponent(location.pathname)}`} />}
          >
            Sign in
          </Button>
          {signupEmail !== null && (
            <Field>
              <FieldLabel htmlFor="signup-email">Email for your new account</FieldLabel>
              <Input
                id="signup-email"
                type="email"
                value={signupEmail}
                onChange={(e) => setSignupEmail(e.target.value)}
              />
              <Button className="w-full" variant="secondary" render={<a href={`/auth/signup?email=${encodeURIComponent(signupEmail)}`} aria-disabled={!signupEmail.includes('@')} />}>
                Create account
              </Button>
            </Field>
          )}
        </CardContent>
        {signupEmail === null && (
          <CardFooter className="justify-center">
            <Button variant="link" size="sm" onClick={() => setSignupEmail('')}>
              Create account
            </Button>
          </CardFooter>
        )}
      </Card>
    </main>
  );
}

function CreateOrgScreen() {
  const changeSession = useSessionTransition();
  const signOut = async () => {
    await changeSession(() => api.post('/auth/logout'));
  };
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const create = async () => {
    setCreating(true);
    setError(null);
    try {
      const { orgId } = await api.post('/api/orgs', { name, slug });
      // founder membership arrives via the outbox: poll, then switch in
      for (let attempt = 0; attempt < 50; attempt++) {
        const me = parseMe(await api.get('/me'));
        if (me.tier === 'user' && me.organizations.some((o) => o.id === orgId)) break;
        await new Promise((resolve) => setTimeout(resolve, 200));
      }
      await changeSession(() => api.post('/auth/switch-org', { orgId }));
    } catch (e) {
      setError(
        String((e as { body?: { error?: string } }).body?.error ?? 'could not create organization'),
      );
      setCreating(false);
    }
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle role="heading" aria-level={1}>Create your organization</CardTitle>
          <CardDescription>You&apos;re signed in but don&apos;t belong to an organization yet.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
        <Field>
          <FieldLabel htmlFor="org-name">Organization name</FieldLabel>
          <Input
            id="org-name"
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              setSlug(
                e.target.value
                  .toLowerCase()
                  .replace(/[^a-z0-9]+/g, '-')
                  .replace(/^-|-$/g, ''),
              );
            }}
          />
        </Field>
        <Field>
          <FieldLabel htmlFor="org-slug">URL slug</FieldLabel>
          <Input id="org-slug" value={slug} onChange={(e) => setSlug(e.target.value)} />
        </Field>
        <Button className="w-full" disabled={!name || slug.length < 3 || creating} onClick={create}>
          {creating ? 'Setting up…' : 'Create organization'}
        </Button>
        {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
        </CardContent>
        <CardFooter className="justify-center">
          <Button variant="link" size="sm" onClick={() => void signOut()}>
            Sign out
          </Button>
        </CardFooter>
      </Card>
    </main>
  );
}

/**
 * Lifecycle status as a ReUI Badge: the light variants carry the status
 * colour as a tint with readable ink, one per family - live, pending,
 * paused, gone - so every module's statuses read the same way.
 */
export function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'Open' || status === 'Clean' || status === 'Committed' || status === 'Active'
      ? 'success-light'
      : status === 'ComingSoon' || status === 'Staged' || status === 'Pending' || status === 'Scanning'
        ? 'info-light'
        : status === 'TemporarilyClosed' || status === 'Quarantined' || status === 'Suspended' || status === 'Deleted'
          ? 'warning-light'
          : status === 'Closed' || status === 'Erased' || status === 'Discarded' || status === 'Failed'
            ? 'destructive-light'
            : 'secondary';
  return (
    <Badge variant={variant}>
      {status.replace(/([a-z])([A-Z])/g, '$1 $2')}
    </Badge>
  );
}

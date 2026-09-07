import { api } from '@premise/api';
import { Button, cn, ConfirmButton, Field, FieldLabel, Input, Label, Select, Switch, Tabs, TabsContent, TabsList, TabsTrigger, useIsMobile, Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemTitle } from '@premise/ui';
import { Building2, CreditCard, Database, Globe, KeyRound, Map as MapIcon, MapPin } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { fmtDate } from '../lib/format';
import { PageHeader, Panel } from '../components/page';
import { useApiMutation } from '../lib/mutation';
import { can, useMe } from '../session';

type SettingsTab = {
  value: string;
  label: string;
  icon: typeof Building2;
  manageSites?: boolean;
};

const SETTINGS_TABS: SettingsTab[] = [
  { value: 'profile', label: 'Profile', icon: Building2 },
  { value: 'billing', label: 'Billing', icon: CreditCard },
  { value: 'sso', label: 'Sign-on', icon: KeyRound },
  { value: 'sites', label: 'Site attributes', icon: MapPin, manageSites: true },
  { value: 'map', label: 'Map', icon: MapIcon },
  { value: 'locator', label: 'Public locator', icon: Globe },
  { value: 'data', label: 'Your data', icon: Database },
];

export function SettingsPage() {
  const { data: me } = useMe();
  const activeOrg =
    me?.tier === 'user' ? me.organizations.find((o) => o.id === me.activeOrg) : undefined;
  const [name, setName] = useState<string | null>(null);

  const rename = useApiMutation({
    mutationFn: (value: string) => api.put('/api/org', { name: value }),
    invalidate: [['me']],
    success: 'Organization renamed',
    onSuccess: () => setName(null),
  });
  const exportData = useApiMutation({
    mutationFn: () => api.post('/api/org/export'),
    success: 'Export queued - check Files shortly',
  });
  const { data: billing } = useQuery({
    queryKey: ['billing'],
    queryFn: ({ signal }) => api.get('/api/billing', { signal }),
  });
  const checkout = useApiMutation({
    mutationFn: (planId: string) =>
      api.post('/api/billing/checkout', { planId, returnPath: '/settings' }),
    onSuccess: ({ url }) => {
      location.href = url;
    },
  });
  const portal = useApiMutation({
    mutationFn: () => api.post('/api/billing/portal', { returnPath: '/settings' }),
    onSuccess: ({ url }) => {
      location.href = url;
    },
  });
  const { data: sso } = useQuery({
    queryKey: ['sso'],
    queryFn: ({ signal }) => api.get('/api/org/sso', { signal }),
  });
  const { data: publicUrl } = useQuery({
    queryKey: ['public-url'],
    queryFn: ({ signal }) => api.get('/api/org/public-url', { signal }),
  });
  const { data: closure } = useQuery({
    queryKey: ['closure'],
    queryFn: ({ signal }) =>
      api.get('/api/org/closure', { signal }),
  });
  const requestClose = useApiMutation({
    mutationFn: () => api.post('/api/org/close'),
    invalidate: [['closure']],
    success: 'Closure scheduled - every manager has been notified',
  });
  const cancelClose = useApiMutation({
    mutationFn: () => api.post('/api/org/close/cancel'),
    invalidate: [['closure']],
    success: 'Closure canceled',
  });
  const ssoPortal = useApiMutation({
    mutationFn: (intent: 'sso' | 'dsync') =>
      api.post('/api/org/sso/portal', { intent, returnPath: '/settings' }),
    onSuccess: ({ url }) => {
      location.href = url;
    },
  });

  const [tab, setTab] = useState('profile');
  const isMobile = useIsMobile();
  if (!activeOrg) return null;
  const draft = name ?? activeOrg.name;
  return (
    <div className="max-w-5xl space-y-6">
      <PageHeader title="Organization settings" description="Profile, billing, sign-on, the map, the public locator, and your data." />
      <Tabs
        value={tab}
        onValueChange={(value) => setTab(String(value))}
        orientation={isMobile ? 'horizontal' : 'vertical'}
        className="w-full gap-5 lg:gap-8"
      >
        <div className={isMobile ? '-mx-1 w-full overflow-x-auto px-1 pb-1' : 'w-44 shrink-0'}>
          <TabsList
            className={
              isMobile
                ? 'h-auto w-max min-w-max justify-start gap-1 bg-transparent p-0'
                : 'h-auto w-full flex-col items-stretch gap-1 bg-transparent p-0'
            }
          >
            {SETTINGS_TABS.filter((t) => !t.manageSites || can(me, 'sites:manage')).map((t) => (
              <TabsTrigger
                key={t.value}
                value={t.value}
                className={cn('w-full justify-start gap-3 px-3 py-1.5 shadow-none', tab === t.value ? 'bg-muted!' : 'bg-transparent')}
              >
                <t.icon aria-hidden />
                <span className="truncate">{t.label}</span>
              </TabsTrigger>
            ))}
          </TabsList>
        </div>
        <div className="min-w-0 flex-1 space-y-6">
          <TabsContent value="profile" className="mt-0">
      <Panel title="Profile" bodyClassName="space-y-3">
          <Field>
            <FieldLabel htmlFor="org-rename">Name</FieldLabel>
            <Input id="org-rename" value={draft} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="org-slug">URL slug</FieldLabel>
            <Input id="org-slug" value={activeOrg.slug} disabled />
          </Field>
          <Button
            disabled={draft === activeOrg.name || !draft.trim() || rename.isPending}
            onClick={() => rename.mutate(draft.trim())}
          >
            Save
          </Button>
        </Panel>
          </TabsContent>
          <TabsContent value="billing" className="mt-0">
      <Panel title="Billing" bodyClassName="space-y-3">
          {billing?.status === 'PastDue' && (
            <div className="rounded-md bg-warning/15 px-3 py-2 text-sm text-warning-foreground">
              <span className="font-semibold">Payment failed.</span> Your features continue
              while the charge retries
              {billing.currentPeriodEnd && ` until ${fmtDate(billing.currentPeriodEnd)}`} — update
              your card via Manage billing below.
            </div>
          )}
          {billing && (
            <>
              <div className="text-sm">
                <span className="font-medium">{billing.planName} plan</span>
                {billing.status && (
                  <span className="ml-2 text-muted-foreground">
                    {billing.status}
                    {billing.currentPeriodEnd &&
                      ` · renews ${fmtDate(billing.currentPeriodEnd)}`}
                  </span>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                {billing.plans
                  .filter((p) => p.id !== billing.planId || billing.status === 'Canceled')
                  .map((p) => (
                    <Button
                      key={p.id}
                      variant="outline"
                      size="sm"
                      disabled={checkout.isPending}
                      onClick={() => checkout.mutate(p.id)}
                    >
                      {billing.planId && billing.status !== 'Canceled'
                        ? `Switch to ${p.name}`
                        : `Upgrade to ${p.name}`}{' '}
                      · ${p.monthlyPriceUsd}/mo
                    </Button>
                  ))}
                {billing.portalAvailable && (
                  <Button variant="ghost" size="sm" disabled={portal.isPending}
                    onClick={() => portal.mutate()}>
                    Manage billing
                  </Button>
                )}
              </div>
              <p className="text-xs text-muted-foreground">
                Checkout and billing management are hosted by your payment provider
                ({billing.provider}). Plan changes apply automatically.
              </p>
            </>
          )}
        </Panel>

          </TabsContent>
          <TabsContent value="sso" className="mt-0">
      <Panel title="Single sign-on" bodyClassName="space-y-2">
          {!sso ? null : !sso.available ? (
            <p className="text-sm text-muted-foreground">
              Enterprise SSO and directory sync are not supported by this
              installation&apos;s auth provider.
            </p>
          ) : !sso.entitled ? (
            <p className="text-sm text-muted-foreground">
              Connect your identity provider and sync your employee directory
              automatically. Available on the Scale plan - upgrade under Billing above.
            </p>
          ) : (
            <>
              <p className="text-sm text-muted-foreground">
                Configuration is hosted by the auth provider: connect your identity
                provider, or sync your employee directory so joiners and leavers are
                provisioned automatically.
              </p>
              <div className="flex flex-wrap gap-2">
                <Button variant="outline" size="sm" disabled={ssoPortal.isPending}
                  onClick={() => ssoPortal.mutate('sso')}>
                  Configure SSO
                </Button>
                <Button variant="outline" size="sm" disabled={ssoPortal.isPending}
                  onClick={() => ssoPortal.mutate('dsync')}>
                  Configure directory sync
                </Button>
              </div>
            </>
          )}
        </Panel>

          </TabsContent>
          {can(me, 'sites:manage') && (
            <TabsContent value="sites" className="mt-0">
              <SiteAttributesCard />
            </TabsContent>
          )}
          <TabsContent value="map" className="mt-0">
            <MapBasemapsCard />
          </TabsContent>
          <TabsContent value="locator" className="mt-0">
      <Panel title="Public locator" bodyClassName="space-y-2">
          {publicUrl && (
            <>
              <p className="text-sm text-muted-foreground">
                Your locations live at{' '}
                <a href={publicUrl.url} target="_blank" rel="noreferrer"
                  className="text-foreground underline underline-offset-4">
                  {publicUrl.url}
                </a>
                . Embed the locator on your own website with this snippet:
              </p>
              <code tabIndex={0} className="block overflow-x-auto whitespace-pre rounded-md bg-muted p-3 text-xs">
                {publicUrl.embedSnippet}
              </code>
            </>
          )}
        </Panel>

          </TabsContent>
          <TabsContent value="data" className="mt-0 space-y-6">
      <Panel title="Your data" bodyClassName="space-y-2">
          <p className="text-sm text-muted-foreground">
            Take a full archive of this organization&apos;s data - sites, people, roles,
            entitlements, and audit history. The archive is delivered to Files.
          </p>
          <Button
            variant="outline"
            disabled={exportData.isPending}
            onClick={() => exportData.mutate()}
          >
            {exportData.isSuccess ? 'Queued - check Files shortly' : 'Export org data'}
          </Button>
        </Panel>

      <Panel title="Close this organization" bodyClassName="space-y-2">
          {closure?.requestedAt ? (
            <>
              <p className="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive">
                Closure scheduled: all data is permanently deleted on{' '}
                <span className="font-semibold">{fmtDate(closure.purgesAt!)}</span>. Everything
                keeps working until then.
              </p>
              <Button variant="outline" disabled={cancelClose.isPending}
                onClick={() => cancelClose.mutate()}>
                Cancel closure
              </Button>
            </>
          ) : (
            <>
              <p className="text-sm text-muted-foreground">
                Schedules permanent deletion of this organization and all its data after a
                30-day grace window. Every manager is notified, everything keeps working
                until the deadline, and any manager can cancel. Export your data first.
              </p>
              <ConfirmButton variant="destructive" confirmLabel="Schedule permanent deletion?"
                disabled={requestClose.isPending}
                onConfirm={() => requestClose.mutate()}>
                Close organization
              </ConfirmButton>
            </>
          )}
        </Panel>
          </TabsContent>
        </div>
      </Tabs>
    </div>
  );
}

function SiteAttributesCard() {
  const [key, setKey] = useState('');
  const [label, setLabel] = useState('');
  const [type, setType] = useState('Text');
  const [isPublic, setIsPublic] = useState(false);

  const { data: definitions } = useQuery({
    queryKey: ['site-attributes'],
    queryFn: ({ signal }) => api.get('/api/sites/attributes', { signal }),
  });
  const create = useApiMutation({
    mutationFn: () =>
      api.post('/api/sites/attributes', { key: key.trim(), label: label.trim(), type, public: isPublic }),
    invalidate: [['site-attributes']],
    success: 'Attribute added',
    onSuccess: () => {
      setKey('');
      setLabel('');
      setIsPublic(false);
    },
  });
  const remove = useApiMutation({
    mutationFn: (id: string) => api.del('/api/sites/attributes/{id}', { path: { id } }),
    invalidate: [['site-attributes'], ['site']],
    success: 'Attribute removed - its values are gone from every site',
  });

  return (
    <Panel title="Site attributes" bodyClassName="space-y-3">
        <p className="text-sm text-muted-foreground">
          Your own fields on every site - a drive-thru flag, a cost center, a manager name.
          Public attributes appear on the site&apos;s public page; the rest stay internal.
        </p>
        {definitions && definitions.length > 0 && (
          <ItemGroup>
            {definitions.map((d) => (
              <Item key={d.id} variant="outline" size="sm">
                <ItemContent>
                  <ItemTitle>{d.label}</ItemTitle>
                  <ItemDescription>
                    {d.key} · {d.type}
                    {d.public && ' · public'}
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  <ConfirmButton size="sm" variant="ghost" confirmLabel="Delete? Values go too"
                    disabled={remove.isPending} onConfirm={() => remove.mutate(d.id)}>
                    Delete
                  </ConfirmButton>
                </ItemActions>
              </Item>
            ))}
          </ItemGroup>
        )}
        <div className="flex flex-wrap items-end gap-2">
          <Field>
            <FieldLabel htmlFor="attr-label">Label</FieldLabel>
            <Input id="attr-label" className="w-40" value={label} placeholder="Drive-thru"
              onChange={(e) => {
                setLabel(e.target.value);
                setKey(e.target.value.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, ''));
              }} />
          </Field>
          <Field>
            <FieldLabel htmlFor="attr-key">Key</FieldLabel>
            <Input id="attr-key" className="w-36 font-mono text-xs" value={key}
              onChange={(e) => setKey(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="attr-type">Type</FieldLabel>
            <Select id="attr-type" className="w-28" value={type}
              onChange={(e) => setType(e.target.value)}>
              <option>Text</option>
              <option>Number</option>
              <option>Boolean</option>
            </Select>
          </Field>
          <label className="flex h-9 items-center gap-2 text-sm">
            <Switch checked={isPublic} onCheckedChange={(checked) => setIsPublic(checked)} />
            Public
          </label>
          <Button size="sm" disabled={!key.trim() || !label.trim() || create.isPending}
            onClick={() => create.mutate()}>
            Add
          </Button>
        </div>
      </Panel>
  );
}

/**
 * The org's own basemaps (ADR 50 §3, the `map.basemaps` setting): raster
 * tile providers the map offers beside the open ones it ships with. A
 * provider key is written once and never shown again; the map receives it
 * in the tile URL, which is how providers key by referrer.
 */
function MapBasemapsCard() {
  const [name, setName] = useState('');
  const [id, setId] = useState('');
  const [urlTemplate, setUrlTemplate] = useState('');
  const [attribution, setAttribution] = useState('');
  const [maxZoom, setMaxZoom] = useState('19');
  const [key, setKey] = useState('');

  const { data } = useQuery({
    queryKey: ['basemaps', 'settings'],
    queryFn: ({ signal }) => api.get('/api/map/basemaps/settings', { signal }),
  });
  const entries = data?.basemaps ?? [];
  // the whole list is the setting: a save carries every entry, keys kept server-side by id
  const keep = entries.map((e) => ({
    id: e.id,
    name: e.name,
    urlTemplate: e.urlTemplate,
    attribution: e.attribution,
    maxZoom: e.maxZoom,
  }));
  const save = useApiMutation({
    mutationFn: (basemaps: typeof keep & { key?: string | null }[]) =>
      api.put('/api/map/basemaps', { basemaps }),
    invalidate: [['basemaps']],
    success: 'Basemaps saved',
    onSuccess: () => {
      setName('');
      setId('');
      setUrlTemplate('');
      setAttribution('');
      setMaxZoom('19');
      setKey('');
    },
  });
  const needsKey = urlTemplate.includes('{key}');

  return (
    <Panel title="Map basemaps" bodyClassName="space-y-3">
        <p className="text-sm text-muted-foreground">
          Raster tile providers the map offers beside OpenStreetMap and the themed default. Put{' '}
          <code className="rounded bg-muted px-1">{'{key}'}</code> in the URL where the provider wants its
          key; the key is stored encrypted and never shown again.
        </p>
        {entries.map((e) => (
          <Item key={e.id} variant="outline" size="sm">
            <ItemContent>
              <ItemTitle>{e.name}</ItemTitle>
              <ItemDescription>
                {e.id} · zoom {String(e.maxZoom)}{e.hasKey && ' · keyed'}
                <span className="block truncate">{e.urlTemplate}</span>
              </ItemDescription>
            </ItemContent>
            <ItemActions>
              <ConfirmButton size="sm" variant="ghost" confirmLabel="Remove?" disabled={save.isPending}
              onConfirm={() => save.mutate(keep.filter((k) => k.id !== e.id))}>
              Remove
            </ConfirmButton>
            </ItemActions>
          </Item>
        ))}
        <div className="grid gap-2 sm:grid-cols-2">
          <Field>
            <FieldLabel htmlFor="basemap-name">Name</FieldLabel>
            <Input id="basemap-name" value={name} placeholder="Aerial"
              onChange={(e) => {
                setName(e.target.value);
                setId(e.target.value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, ''));
              }} />
          </Field>
          <Field>
            <FieldLabel htmlFor="basemap-id">Id</FieldLabel>
            <Input id="basemap-id" className="font-mono text-xs" value={id} onChange={(e) => setId(e.target.value)} />
          </Field>
          <div className="space-y-1 sm:col-span-2">
            <Label htmlFor="basemap-url">URL template</Label>
            <Input id="basemap-url" className="font-mono text-xs" value={urlTemplate}
              placeholder="https://tiles.example.com/{z}/{x}/{y}.png?key={key}"
              onChange={(e) => setUrlTemplate(e.target.value)} />
          </div>
          <Field>
            <FieldLabel htmlFor="basemap-attribution">Attribution</FieldLabel>
            <Input id="basemap-attribution" value={attribution} placeholder="© Example Maps"
              onChange={(e) => setAttribution(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="basemap-zoom">Max zoom</FieldLabel>
            <Input id="basemap-zoom" type="number" min={1} max={22} value={maxZoom}
              onChange={(e) => setMaxZoom(e.target.value)} />
          </Field>
          {needsKey && (
            <div className="space-y-1 sm:col-span-2">
              <Label htmlFor="basemap-key">Provider key</Label>
              <Input id="basemap-key" type="password" autoComplete="off" value={key}
                onChange={(e) => setKey(e.target.value)} />
            </div>
          )}
        </div>
        <Button size="sm" disabled={!name.trim() || !id || !urlTemplate.trim() || (needsKey && !key) || save.isPending}
          onClick={() =>
            save.mutate([
              ...keep,
              { id, name: name.trim(), urlTemplate: urlTemplate.trim(), attribution: attribution.trim(),
                maxZoom: Number(maxZoom) || 19, key: key || null },
            ])
          }>
          Add basemap
        </Button>
      </Panel>
  );
}

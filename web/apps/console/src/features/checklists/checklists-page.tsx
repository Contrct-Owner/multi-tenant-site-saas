import { checklistsApi } from './api';
import { useSites } from '../sites';
import { Button, Checkbox, ConfirmButton, Field, FieldDescription, FieldLabel, FieldLegend, FieldSet, FormDialog, Input, Item, ItemActions, ItemContent, ItemDescription, ItemTitle, Sortable, SortableItem, SortableItemHandle } from '@premise/ui';
import { GripVertical, Plus, X } from 'lucide-react';
import { SitePicker, type PickedSite } from '../sites/components/site-picker';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Loading, PageHeader, Panel } from '../../components/page';
import { useApiMutation } from '../../lib/mutation';
import { can, useMe } from '../../session';

/** The ops core loop (ADR 45): today's lists per site, on the site's clock. */
export function ChecklistsPage() {
  const { data: me } = useMe();
  const manage = can(me, 'checklists:manage');
  const [picked, setPicked] = useState<PickedSite | null>(null);

  // the first site in scope is the default; the picker searches the rest
  const siteQuery = useSites('');
  const sites = siteQuery.data?.pages.flatMap((page) => page.items);
  const first = sites?.[0];
  const current = picked ?? (first ? { id: first.id, name: first.name, city: first.city } : null);
  const activeSite = current?.id ?? '';
  const todayQuery = useQuery({
    queryKey: ['checklists', 'today', activeSite],
    queryFn: ({ signal }) => checklistsApi.today(activeSite, signal),
    enabled: !!activeSite,
  });
  const today = todayQuery.data;
  const check = useApiMutation({
    mutationFn: (input: { templateId: string; itemIndex: number; done: boolean }) =>
      checklistsApi.check({ ...input, siteId: activeSite }),
    invalidate: [['checklists', 'today', activeSite]],
  });

  return (
    <div className="max-w-3xl space-y-6">
      <PageHeader
        title="Checklists"
        description="Today's lists at a site, on that site's own clock."
        actions={
          sites && sites.length > 1 ? (
            <div className="w-72">
              <SitePicker aria-label="Checklist site" value={current} onChange={setPicked} />
            </div>
          ) : undefined
        }
      />
      {siteQuery.isPending && <Loading text="Loading sites…" />}
      {siteQuery.isError && <div role="alert">Could not load sites. <Button onClick={() => void siteQuery.refetch()}>Retry sites</Button></div>}
      {sites?.length === 0 && !siteQuery.isError && <p>No accessible sites yet.</p>}
      {activeSite && todayQuery.isPending && <p role="status">Loading checklists…</p>}
      {todayQuery.isError && <div role="alert">Could not load checklists. <Button
        onClick={() => void todayQuery.refetch()}>Retry checklists</Button></div>}
      {today && (
        <p className="text-sm text-muted-foreground">
          {today.site} · {today.businessDate} (site-local day)
        </p>
      )}
      {today?.lists.length === 0 && (
        <Panel bodyClassName="text-sm text-muted-foreground">
            No checklists apply to this site yet.
            {manage && ' Create a template below.'}
        </Panel>
      )}
      {today?.lists.map((list) => {
        const done = list.items.filter((i) => i.done).length;
        return (
          <Panel
            key={list.id}
            title={list.name}
            actions={
              <span className="text-sm tabular-nums text-muted-foreground">
                {done}/{list.items.length}
              </span>
            }
          >
              <ul className="space-y-2">
                {list.items.map((item) => (
                  <li key={item.index}>
                    <label className="flex cursor-pointer items-center gap-3 text-sm">
                      <Checkbox
                        checked={item.done}
                        disabled={check.isPending}
                        onCheckedChange={(checked) =>
                          check.mutate({
                            templateId: list.id,
                            itemIndex: Number(item.index),
                            done: checked === true,
                          })
                        }
                      />
                      <span className={item.done ? 'text-muted-foreground line-through' : ''}>
                        {item.text}
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
          </Panel>
        );
      })}
      {manage && <TemplatesCard />}
    </div>
  );
}

function TemplatesCard() {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [items, setItems] = useState<ChecklistItemDraft[]>([{ id: 'item-1', text: '' }]);

  const templatesQuery = useQuery({
    queryKey: ['checklists', 'templates'],
    queryFn: ({ signal }) => checklistsApi.templates(signal),
  });
  const templates = templatesQuery.data;
  const create = useApiMutation({
    mutationFn: () =>
      checklistsApi.create({
        name: name.trim(),
        items: items.map((i) => i.text.trim()).filter(Boolean),
      }),
    invalidate: [['checklists']],
    success: 'Checklist created',
    onSuccess: () => {
      setOpen(false);
      setName('');
      setItems([{ id: 'item-1', text: '' }]);
    },
  });
  const remove = useApiMutation({
    mutationFn: checklistsApi.remove,
    invalidate: [['checklists']],
    success: 'Checklist deleted',
  });

  return (
    <Panel
      title="Templates"
      actions={
          <FormDialog
            open={open}
            onOpenChange={setOpen}
            trigger={<Button size="sm">New checklist</Button>}
            title="New checklist"
            description="Applies daily at every site."
          >
            <div className="space-y-3">
              <Field>
                <FieldLabel htmlFor="cl-name">Name</FieldLabel>
                <Input id="cl-name" value={name} placeholder="Opening"
                  onChange={(e) => setName(e.target.value)} />
              </Field>
              <FieldSet>
                <FieldLegend>Items</FieldLegend>
                <FieldDescription>In the order people work through them; drag to reorder.</FieldDescription>
                <ChecklistItemsEditor items={items} onChange={setItems} />
              </FieldSet>
              <Button className="w-full"
                disabled={!name.trim() || !items.some((i) => i.text.trim()) || create.isPending}
                onClick={() => create.mutate()}>
                Create
              </Button>
            </div>
          </FormDialog>
      }
      bodyClassName="space-y-2"
    >
        {templatesQuery.isPending && <Loading text="Loading templates…" rows={2} />}
        {templatesQuery.isError && <div role="alert">Could not load templates. <Button
          onClick={() => void templatesQuery.refetch()}>Retry templates</Button></div>}
        {templates?.length === 0 && (
          <p className="text-sm text-muted-foreground">No templates yet.</p>
        )}
        {templates?.map((t) => (
          <Item key={t.id} variant="outline" size="sm">
            <ItemContent>
              <ItemTitle>{t.name}</ItemTitle>
              <ItemDescription>{t.items.length} items</ItemDescription>
            </ItemContent>
            <ItemActions>
              <ConfirmButton size="sm" variant="ghost" disabled={remove.isPending}
                onConfirm={() => remove.mutate(t.id)}>
                Delete
              </ConfirmButton>
            </ItemActions>
          </Item>
        ))}
    </Panel>
  );
}


type ChecklistItemDraft = { id: string; text: string };

/**
 * The template's items as a list you can reorder: the ReUI Sortable with a
 * grip per row, an input per row, add and remove. Order is the order people
 * work through the list, so it is worth a drag handle.
 */
function ChecklistItemsEditor({
  items,
  onChange,
}: {
  items: ChecklistItemDraft[];
  onChange: (items: ChecklistItemDraft[]) => void;
}) {
  const update = (id: string, text: string) => onChange(items.map((i) => (i.id === id ? { ...i, text } : i)));
  const remove = (id: string) => onChange(items.length > 1 ? items.filter((i) => i.id !== id) : items);
  const add = () => onChange([...items, { id: `item-${Date.now()}`, text: '' }]);
  return (
    <div className="space-y-2">
      <Sortable value={items} onValueChange={onChange} getItemValue={(i) => i.id}>
        <div className="space-y-1.5">
          {items.map((item, index) => (
            <SortableItem key={item.id} value={item.id} className="flex items-center gap-1.5">
              <SortableItemHandle className="text-muted-foreground" aria-label={`Move item ${index + 1}`}>
                <GripVertical className="size-4" />
              </SortableItemHandle>
              <Input
                aria-label={`Item ${index + 1}`}
                value={item.text}
                placeholder={index === 0 ? 'Unlock doors' : 'Next item'}
                onChange={(e) => update(item.id, e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    add();
                  }
                }}
              />
              <Button type="button" variant="ghost" size="icon-sm" aria-label={`Remove item ${index + 1}`} onClick={() => remove(item.id)} disabled={items.length === 1}>
                <X className="size-4" />
              </Button>
            </SortableItem>
          ))}
        </div>
      </Sortable>
      <Button type="button" variant="outline" size="sm" onClick={add}>
        <Plus className="size-4" aria-hidden />
        Add item
      </Button>
    </div>
  );
}

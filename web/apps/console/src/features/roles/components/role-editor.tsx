import type { components } from '@premise/api';
import { Button, Checkbox, Field, FieldLabel, FieldLegend, FieldSet, Input } from '@premise/ui';
import { useState } from 'react';
import { useApiMutation } from '../../../lib/mutation';
import { rolesApi } from '../api';
import { GRANTABLE, grantKey, parseGrant } from '../schema';

type Role = components['schemas']['RoleResponse'];
const WILDCARD = '*:*';

/** Draft and save lifecycle are local to this mounted create/edit form. */
export function RoleEditor({ role, onSaved }: { role: Role | null; onSaved: () => void }) {
  const [name, setName] = useState(role?.name ?? '');
  const [picked, setPicked] = useState(() => new Set(role?.grants.map(grantKey) ?? []));
  const save = useApiMutation({
    mutationFn: () => {
      const grants = [...picked].map(parseGrant);
      return rolesApi.save(role?.id ?? null, name.trim(), grants);
    },
    invalidate: [['roles']],
    success: 'Role saved',
    errorFallback: 'Save failed',
    onSuccess: onSaved,
  });
  const toggle = (key: string) => {
    const next = new Set(picked);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    setPicked(next);
  };

  return (
    <div className="space-y-3">
      <Field>
        <FieldLabel htmlFor="role-name">Name</FieldLabel>
        <Input id="role-name" value={name} onChange={(e) => setName(e.target.value)} />
      </Field>
      <FieldSet>
        <FieldLegend>Grants</FieldLegend>
        <div className="grid grid-cols-2 gap-1.5">
          <Field orientation="horizontal">
            <Checkbox id="grant-wildcard" checked={picked.has(WILDCARD)} onCheckedChange={() => toggle(WILDCARD)} />
            <FieldLabel htmlFor="grant-wildcard" className="font-mono font-normal">
              *:* (everything)
            </FieldLabel>
          </Field>
          {GRANTABLE.map((c) => (
            <Field key={c} orientation="horizontal">
              <Checkbox id={`grant-${c}`} checked={picked.has(c)} onCheckedChange={() => toggle(c)} />
              <FieldLabel htmlFor={`grant-${c}`} className="font-mono font-normal">
                {c}
              </FieldLabel>
            </Field>
          ))}
        </div>
      </FieldSet>
      <Button className="w-full"
        disabled={!name.trim() || picked.size === 0 || save.isPending}
        onClick={() => save.mutate()}>
        {role ? 'Save changes' : 'Create role'}
      </Button>
    </div>
  );
}

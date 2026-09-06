import {
  Filters,
  createFilterQuery,
  createFilterRule,
  flattenFilterConditions,
  type FilterField,
  type FilterQuery,
} from '@premise/ui';
import { CircleDot, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { StatusBadge } from '../../../shell';

export type SiteFilterValues = {
  /** Free text over name and city (the server's `q`). */
  q: string;
  /** Lifecycle statuses to keep (the server's `status`), empty for all. */
  statuses: string[];
};

const STATUS_OPTIONS = [
  { value: 'Open', label: 'Open' },
  { value: 'ComingSoon', label: 'Coming soon' },
  { value: 'TemporarilyClosed', label: 'Temporarily closed' },
  { value: 'Closed', label: 'Closed' },
];

const FIELDS: FilterField[] = [
  {
    id: 'q',
    label: 'Name or city',
    icon: <Search className="size-3.5" aria-hidden />,
    type: 'text',
    placeholder: 'Search…',
  },
  {
    id: 'status',
    label: 'Status',
    icon: <CircleDot className="size-3.5" aria-hidden />,
    type: 'select',
    searchable: false,
    options: STATUS_OPTIONS,
    renderValue: ({ values }) => {
      if (values.length === 0) return 'Select…';
      if (values.length > 1) return `${values.length} selected`;
      return <StatusBadge status={String(values[0])} />;
    },
  },
];

/** The default chip: a search that is inactive until something is typed (the filtering block's shape). */
const defaultQuery = () =>
  createFilterQuery([createFilterRule({ id: 'q-1', path: ['q'], operator: 'contains', value: '' })]);

/** The server-facing values a filter tree means; blank text and empty selects mean nothing. */
export function toSiteFilterValues(query: FilterQuery): SiteFilterValues {
  let q = '';
  const statuses = new Set<string>();
  for (const condition of flattenFilterConditions(query)) {
    if (condition.field === 'q') {
      const text = condition.values.map((v) => String(v ?? '').trim()).find((v) => v.length > 0);
      if (text && !condition.negated) q = text;
    }
    if (condition.field === 'status') {
      const chosen = condition.values.map((v) => String(v)).filter(Boolean);
      if (chosen.length === 0) continue;
      if (condition.negated || condition.operator === 'is_not' || condition.operator === 'is_none_of') {
        for (const s of STATUS_OPTIONS.map((o) => o.value)) if (!chosen.includes(s)) statuses.add(s);
      } else {
        for (const s of chosen) statuses.add(s);
      }
    }
  }
  return { q, statuses: [...statuses] };
}

/**
 * The Sites filter bar: the ReUI Filters component in its basic (chip row)
 * variant. Whatever the chips say becomes the list query's `q` and
 * `status`; the server does the filtering, so the map and the table read
 * the same rows.
 */
export function SiteFilters({ onChange }: { onChange: (values: SiteFilterValues) => void }) {
  const [query, setQuery] = useState<FilterQuery>(defaultQuery);
  const fields = useMemo(() => FIELDS, []);
  return (
    <Filters
      fields={fields}
      query={query}
      variant="basic"
      size="sm"
      showClear
      onQueryChange={(next) => {
        setQuery(next);
        onChange(toSiteFilterValues(next));
      }}
    />
  );
}

import {
  DataGrid,
  DataGridContainer,
  DataGridScrollArea,
  DataGridTable,
  dataGridFeatures,
  Frame,
  FrameDescription,
  FrameFooter,
  FrameHeader,
  FramePanel,
  FrameTitle,
  cn,
  useTable,
  type ColumnDef,
  type DataGridFeatures,
} from '@premise/ui';
import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';

/**
 * The page scaffold every console page shares (direction B): a title row
 * with the page's actions beside it, then Frame panels for its content.
 * Sites set the pattern; the rest of the console follows it here so the
 * surface, spacing and type are one decision, not one per page.
 */
export function PageHeader({
  title,
  count,
  description,
  actions,
}: {
  title: ReactNode;
  /** A total beside the title, muted and tabular (the Sites count). */
  count?: number | string | null;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <h1 className="text-xl font-semibold tracking-tight">
          {title}
          {count !== undefined && count !== null && (
            <span className="ml-2 text-base font-medium tabular-nums text-muted-foreground">{count}</span>
          )}
        </h1>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-3">{actions}</div>}
    </div>
  );
}

/**
 * One content panel on the Frame surface. A title (and optional description
 * and actions) sits in the panel header; the body is padded for text and
 * forms, or edge-to-edge (`flush`) for tables and lists that draw their own
 * rows; a footer takes counts and paging.
 */
export function Panel({
  title,
  description,
  actions,
  footer,
  flush = false,
  className,
  bodyClassName,
  children,
}: {
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  footer?: ReactNode;
  flush?: boolean;
  className?: string;
  bodyClassName?: string;
  children: ReactNode;
}) {
  return (
    <Frame className={className}>
      <FramePanel>
        {(title || actions) && (
          <FrameHeader className="flex-row flex-wrap items-center gap-2">
            <div className="min-w-0 flex-1">
              {title && <FrameTitle>{title}</FrameTitle>}
              {description && <FrameDescription>{description}</FrameDescription>}
            </div>
            {actions && <div className="flex items-center gap-2">{actions}</div>}
          </FrameHeader>
        )}
        <div className={cn(!flush && 'px-4 py-4', flush && (title || actions) && 'border-t', bodyClassName)}>
          {children}
        </div>
        {footer && <FrameFooter className="flex-row flex-wrap items-center gap-3">{footer}</FrameFooter>}
      </FramePanel>
    </Frame>
  );
}

/** A headline number that links to where it comes from (the dashboard). */
export function Stat({ value, label, to }: { value: ReactNode; label: ReactNode; to: string }) {
  return (
    <Frame>
      <FramePanel>
        <Link to={to} className="block px-4 py-4">
          <div className="text-3xl font-semibold tabular-nums">{value}</div>
          <div className="text-sm text-muted-foreground">{label}</div>
        </Link>
      </FramePanel>
    </Frame>
  );
}

/** The one-line status/alert rows pages show while loading or after a failure. */
export function Notice({ kind = 'status', children }: { kind?: 'status' | 'alert'; children: ReactNode }) {
  return (
    <p role={kind} className={cn('text-sm', kind === 'alert' ? 'text-destructive' : 'text-muted-foreground')}>
      {children}
    </p>
  );
}

/**
 * A Data Grid inside a Panel for the console's plain lists (members, files,
 * audit): server-driven rows, the Sites grid's layout, a footer for paging.
 * Columns come from the caller (memoized); everything else is decided here.
 */
export function Grid<TRow extends object>({
  columns,
  rows,
  getRowId,
  isLoading = false,
  loadingMessage,
  emptyMessage,
  onRowClick,
  title,
  description,
  actions,
  footer,
  children,
}: {
  columns: ColumnDef<DataGridFeatures, TRow>[];
  rows: TRow[];
  getRowId: (row: TRow) => string;
  isLoading?: boolean;
  loadingMessage?: ReactNode;
  emptyMessage?: ReactNode;
  onRowClick?: (row: TRow) => void;
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  footer?: ReactNode;
  /** Rendered under the table, inside the panel (a detail pane, a note). */
  children?: ReactNode;
}) {
  const table = useTable({
    features: dataGridFeatures,
    columns,
    data: rows,
    getRowId,
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
    pageCount: -1,
  });
  return (
    <Frame>
      <FramePanel>
        <DataGrid
          table={table}
          recordCount={rows.length}
          isLoading={isLoading}
          loadingMode="skeleton"
          loadingMessage={loadingMessage}
          emptyMessage={emptyMessage}
          onRowClick={onRowClick}
          tableLayout={{ headerBackground: false, headerBorder: true, rowBorder: true }}
        >
          {(title || actions) && (
            <FrameHeader className="flex-row flex-wrap items-center gap-2">
              <div className="min-w-0 flex-1">
                {title && <FrameTitle>{title}</FrameTitle>}
                {description && <FrameDescription>{description}</FrameDescription>}
              </div>
              {actions && <div className="flex items-center gap-2">{actions}</div>}
            </FrameHeader>
          )}
          <DataGridContainer>
            <DataGridScrollArea>
              <DataGridTable />
            </DataGridScrollArea>
          </DataGridContainer>
          {children}
          {footer && <FrameFooter className="flex-row flex-wrap items-center gap-3">{footer}</FrameFooter>}
        </DataGrid>
      </FramePanel>
    </Frame>
  );
}

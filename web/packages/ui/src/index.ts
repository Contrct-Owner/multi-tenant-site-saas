// The @premise/ui barrel (ADR 20): app code imports ONLY from here, never
// from component files directly - a lint rule enforces it. This indirection
// is the real seam: reskin via tokens.css, replace a component behind the
// barrel without touching call sites.
export * from './components/alert';
export * from './components/badge';
export * from './components/button';
export * from './components/card';
export * from './components/input';
export * from './components/label';
export * from './components/table';
export * from './components/textarea';
export * from './components/confirm-button';
export * from './components/dialog';
export * from './components/select';
export * from './components/toast';

// ReUI surface + shadcn primitives the ReUI app shells build on (direction B,
// 2026-09-05). Installed via the shadcn CLI from this package; imports inside
// them are relative because the apps' Vite has no `@` alias. Not exported on
// purpose: components/reui/badge (the template's Badge stays the one above)
// and components/select-menu (Radix listbox the data grid uses internally;
// the template's Select stays the native control).
export * from './components/reui/frame';
export * from './components/reui/data-grid/data-grid';
export * from './components/reui/data-grid/data-grid-table';
export * from './components/reui/data-grid/data-grid-table-virtual';
export * from './components/reui/data-grid/data-grid-scroll-area';
export * from './components/reui/data-grid/data-grid-pagination';
export * from './components/reui/data-grid/data-grid-column-header';
export * from './components/reui/data-grid/data-grid-column-visibility';
export * from './components/reui/data-grid/data-grid-column-filter';
export * from './components/sidebar';
export * from './components/sheet';
export * from './components/tooltip';
export * from './components/dropdown-menu';
export * from './components/popover';
export * from './components/checkbox';
export * from './components/separator';
export * from './components/skeleton';
export * from './components/spinner';
export { useIsMobile } from './hooks/use-mobile';
export { cn } from './lib/utils';

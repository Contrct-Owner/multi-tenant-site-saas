/** The server's list envelope (UX gap: fleet-scale paging). */
export type Page<T> = { items: T[]; total: number; nextOffset: number | null };

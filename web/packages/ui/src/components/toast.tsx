import { Toast as ToastPrimitive } from 'radix-ui';
import { useEffect, useState } from 'react';
import { cn } from '../lib/utils';

type ToastItem = { id: number; message: string; kind: 'success' | 'error' };

// module-level bridge: pages call toast.*() imperatively, the single
// <Toaster/> in the shell renders whatever arrives
let push: ((item: Omit<ToastItem, 'id'>) => void) | null = null;
let nextId = 1;

export const toast = {
  success: (message: string) => push?.({ message, kind: 'success' }),
  error: (message: string) => push?.({ message, kind: 'error' }),
};

export function Toaster() {
  const [items, setItems] = useState<ToastItem[]>([]);
  useEffect(() => {
    push = (item) => setItems((current) => [...current, { ...item, id: nextId++ }]);
    return () => {
      push = null;
    };
  }, []);
  return (
    <ToastPrimitive.Provider swipeDirection="right" duration={4000}>
      {items.map((item) => (
        <ToastPrimitive.Root
          key={item.id}
          onOpenChange={(open) => {
            if (!open) setItems((current) => current.filter((i) => i.id !== item.id));
          }}
          className={cn(
            'rounded-md border px-4 py-3 text-sm shadow-md',
            'data-[state=open]:animate-in data-[state=open]:slide-in-from-right-4',
            item.kind === 'error'
              ? 'border-destructive/30 bg-destructive/10 text-destructive'
              : 'border-success/30 bg-card text-foreground',
          )}
        >
          <ToastPrimitive.Description>{item.message}</ToastPrimitive.Description>
        </ToastPrimitive.Root>
      ))}
      <ToastPrimitive.Viewport className="fixed bottom-4 right-4 z-[100] flex w-80 flex-col gap-2" />
    </ToastPrimitive.Provider>
  );
}

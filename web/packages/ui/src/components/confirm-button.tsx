import { useEffect, useState } from 'react';
import { Button } from './button';
import type { ComponentProps, ReactNode } from 'react';

/**
 * Two-step inline confirmation: first click arms ("Sure?"), second commits,
 * and it disarms itself after a beat. Replaces window.confirm and - worse -
 * destructive controls with no confirmation at all.
 */
export function ConfirmButton({
  onConfirm,
  children,
  confirmLabel = 'Sure?',
  variant = 'ghost',
  ...props
}: Omit<ComponentProps<typeof Button>, 'onClick'> & {
  onConfirm: () => void;
  confirmLabel?: ReactNode;
}) {
  const [armed, setArmed] = useState(false);
  useEffect(() => {
    if (!armed) return;
    const timer = setTimeout(() => setArmed(false), 3000);
    return () => clearTimeout(timer);
  }, [armed]);
  return (
    <Button
      variant={armed ? 'destructive' : variant}
      onClick={() => {
        if (armed) {
          setArmed(false);
          onConfirm();
        } else {
          setArmed(true);
        }
      }}
      {...props}
    >
      {armed ? confirmLabel : children}
    </Button>
  );
}

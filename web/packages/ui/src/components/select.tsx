import type { ComponentProps } from 'react';
import { cn } from '../lib/utils';

/**
 * Styled NATIVE select (deliberately not a popover listbox): the template
 * favors zero-surprise controls, and every console picker was hand-rolling
 * these classes. One place to restyle.
 */
export function Select({ className, ...props }: ComponentProps<'select'>) {
  return (
    <select
      className={cn(
        'h-9 w-full rounded-md border bg-background px-2 text-sm outline-none',
        'focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50',
        className,
      )}
      {...props}
    />
  );
}

const TIME_ZONES: string[] = (() => {
  try {
    return Intl.supportedValuesOf('timeZone');
  } catch {
    return ['Etc/UTC'];
  }
})();

/** Every IANA zone the runtime knows - no more freetext "America/New_York". */
export function TimeZoneSelect(props: Omit<ComponentProps<'select'>, 'children'>) {
  return (
    <Select {...props}>
      {TIME_ZONES.map((zone) => (
        <option key={zone} value={zone}>
          {zone.replaceAll('_', ' ')}
        </option>
      ))}
    </Select>
  );
}

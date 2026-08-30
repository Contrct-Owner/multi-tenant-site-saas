import { describe, expect, it } from 'vitest';
import { isTodayInZone, spanLabel } from './hours';

describe('spanLabel', () => {
  it('collapses a full site-local day into "Open 24 hours"', () => {
    // 00:00-23:59 in New York (EDT: UTC-4)
    expect(
      spanLabel('2026-06-15T04:00:00Z', '2026-06-16T03:59:00Z', 'America/New_York'),
    ).toBe('Open 24 hours');
  });

  it('renders ordinary spans on the site clock, not the runtime clock', () => {
    // 09:00-17:00 in New York
    const label = spanLabel('2026-06-15T13:00:00Z', '2026-06-15T21:00:00Z', 'America/New_York');
    expect(label).toMatch(/9:00\sAM – 5:00\sPM/);
  });
});

describe('isTodayInZone', () => {
  it('answers on the site clock, where "today" can differ from the viewer', () => {
    const now = Date.parse('2026-06-15T03:00:00Z'); // Jun 14 evening in NY, Jun 15 in UTC
    expect(isTodayInZone('2026-06-14', 'America/New_York', now)).toBe(true);
    expect(isTodayInZone('2026-06-15', 'America/New_York', now)).toBe(false);
    expect(isTodayInZone('2026-06-15', 'Etc/UTC', now)).toBe(true);
  });
});

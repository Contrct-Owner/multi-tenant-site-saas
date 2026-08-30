import { describe, expect, it } from 'vitest';
import { entitlementLabel, fmtDayInZone, fmtTimeInZone } from './format';

describe('zone-pinned formatting', () => {
  // the regression this guards: the console rendered site hours in the
  // VIEWER's timezone - midnight-to-midnight showed as "11:00 PM - 10:59 PM"
  it('renders an instant in the requested zone, not the runtime zone', () => {
    const midnightEastern = '2026-06-15T04:00:00Z'; // 00:00 in New York (EDT)
    expect(fmtTimeInZone(midnightEastern, 'America/New_York')).toMatch(/12:00\sAM/);
    expect(fmtDayInZone(midnightEastern, 'America/New_York')).toContain('Jun 15');
    // the same instant is the PREVIOUS day in Los Angeles
    expect(fmtDayInZone(midnightEastern, 'America/Los_Angeles')).toContain('Jun 14');
  });
});

describe('entitlement labels', () => {
  it('humanizes known codes and passes unknown ones through', () => {
    expect(entitlementLabel('sites.max')).toBe('Site limit');
    expect(entitlementLabel('custom.fork_code')).toBe('custom.fork_code');
  });
});

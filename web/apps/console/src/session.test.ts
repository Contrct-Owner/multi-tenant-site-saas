import { describe, expect, it } from 'vitest';
import { can, type Me } from './session';

const user = (capabilities: string[]): Me =>
  ({
    tier: 'user',
    userId: 'u1',
    email: 'x@y.z',
    activeOrg: 'o1',
    organizations: [],
    capabilities,
  }) as unknown as Me;

describe('can', () => {
  it('is false while /me has not resolved - the UI fails closed', () => {
    expect(can(undefined, 'sites:read')).toBe(false);
  });

  it('is false for non-user tiers', () => {
    expect(can({ tier: 'guest' } as unknown as Me, 'sites:read')).toBe(false);
  });

  it('reflects resolved capabilities exactly', () => {
    const me = user(['sites:read']);
    expect(can(me, 'sites:read')).toBe(true);
    expect(can(me, 'sites:manage')).toBe(false);
  });
});

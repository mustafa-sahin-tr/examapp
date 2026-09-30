import { toCoalescedCount } from './notification.model';

describe('toCoalescedCount (issue #305 dilim B)', () => {
  it('acceptsPositiveSafeIntegers', () => {
    expect(toCoalescedCount(1)).toBe(1);
    expect(toCoalescedCount(4)).toBe(4);
    expect(toCoalescedCount(Number.MAX_SAFE_INTEGER)).toBe(Number.MAX_SAFE_INTEGER);
  });

  it('untrustedOrMissing_FallsBackToOne', () => {
    for (const value of [undefined, null, 0, -2, 2.5, '3', NaN, Infinity, Number.MAX_VALUE, {}, [3], true]) {
      expect(toCoalescedCount(value)).withContext(String(value)).toBe(1);
    }
  });
});

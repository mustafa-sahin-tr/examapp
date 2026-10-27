// Issue #576: tohumlu karıştırma kararlı ve permütasyon.
import { seededShuffle } from './seeded-shuffle';

describe('seededShuffle (#576)', () => {
  it('sameSeedSameOrder_IsPermutation_DifferentSeedsUsuallyDiffer', () => {
    const items = Array.from({ length: 12 }, (_, i) => `c${i + 1}`);
    const a = seededShuffle(items, '101');
    expect(seededShuffle(items, '101')).toEqual(a);
    expect([...a].sort()).toEqual([...items].sort());
    expect(a).not.toEqual(items);
    expect(seededShuffle(items, '102')).not.toEqual(a);
    expect(items[0]).toBe('c1');
  });
});

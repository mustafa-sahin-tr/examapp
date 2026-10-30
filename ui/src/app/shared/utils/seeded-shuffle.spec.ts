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

  it('neverReturnsPlanOrder_SeedThatShufflesToIdentityIsRotated', () => {
    // '18' Fisher–Yates'te [1,2,3,4]'ü aynen veriyordu (canlıda D/Y satırları "karışmadı" göründü).
    expect(seededShuffle([1, 2, 3, 4], '18')).toEqual([2, 3, 4, 1]);
    expect(seededShuffle([1, 2, 3, 4], '18')).toEqual(seededShuffle([1, 2, 3, 4], '18'));
    for (let seed = 0; seed < 500; seed++) {
      expect(seededShuffle([1, 2], `${seed}`)).toEqual([2, 1]);
    }
    expect(seededShuffle(['tek'], '18')).toEqual(['tek']);
  });
});

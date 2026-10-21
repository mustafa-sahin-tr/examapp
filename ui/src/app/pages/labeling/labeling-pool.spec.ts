import { isLabelingImage, pageIdOf, pickNextUnlabeled, takeRandom, toHex } from './labeling-pool';

/** Sıradaki değerleri sırayla döndüren deterministik rng. */
function sequenceRng(values: number[]): () => number {
  let i = 0;
  return () => values[i++ % values.length];
}

describe('labeling-pool', () => {
  describe('isLabelingImage', () => {
    it('isLabelingImage_ImageMimeType_True', () => {
      expect(isLabelingImage({ name: 'x.bin', type: 'image/webp' })).toBeTrue();
    });

    it('isLabelingImage_EmptyTypeKnownExtension_True', () => {
      for (const name of ['a.webp', 'b.JPG', 'c.jpeg', 'd.Png']) {
        expect(isLabelingImage({ name, type: '' })).withContext(name).toBeTrue();
      }
    });

    it('isLabelingImage_NonImage_False', () => {
      for (const name of ['.DS_Store', 'question-coordinates.json', 'notes.txt', 'noext']) {
        expect(isLabelingImage({ name, type: name.endsWith('.json') ? 'application/json' : '' }))
          .withContext(name)
          .toBeFalse();
      }
    });
  });

  describe('takeRandom', () => {
    it('takeRandom_EmptyPool_Undefined', () => {
      expect(takeRandom([], () => 0.5)).toBeUndefined();
    });

    it('takeRandom_RemovesPickedItemAndKeepsOthers', () => {
      const pool = ['a', 'b', 'c', 'd'];

      const picked = takeRandom(pool, () => 0.3); // floor(0.3 * 4) = 1

      expect(picked).toBe('b');
      expect(pool.length).toBe(3);
      expect([...pool].sort()).toEqual(['a', 'c', 'd']);
    });

    it('takeRandom_DrainsPoolWithoutRepeats', () => {
      const pool = [1, 2, 3, 4, 5];
      const seen: number[] = [];
      const rng = sequenceRng([0.99, 0, 0.5, 0.2, 0.7]);

      while (pool.length) {
        seen.push(takeRandom(pool, rng)!);
      }

      expect([...seen].sort()).toEqual([1, 2, 3, 4, 5]);
    });

    it('takeRandom_RngReturnsOne_ClampsToLastIndex', () => {
      const pool = ['a', 'b'];

      expect(takeRandom(pool, () => 1)).toBe('b');
    });
  });

  describe('pageIdOf', () => {
    it('toHex_FormatsBytesAsLowercaseHex', () => {
      expect(toHex(new Uint8Array([0, 15, 255]).buffer)).toBe('000fff');
    });

    it('pageIdOf_MatchesPythonSha256HexdigestPrefix', async () => {
      // hashlib.sha256(b"abc").hexdigest()[:16]
      expect(await pageIdOf(new Blob(['abc']))).toBe('ba7816bf8f01cfea');
    });
  });

  describe('pickNextUnlabeled', () => {
    const hashByName = (file: { name: string }) => Promise.resolve(`id-${file.name}`);

    it('pickNextUnlabeled_SkipsLabeledUntilUnlabeled', async () => {
      const pool = [{ name: 'a' }, { name: 'b' }, { name: 'c' }];
      const skipped: string[] = [];

      const result = await pickNextUnlabeled({
        pool,
        labeled: new Set(['id-a', 'id-c']),
        hash: hashByName,
        rng: () => 0, // ilk indeks: a, sonra (sonuncuyla yer değiştiren) c, sonra b
        onSkipLabeled: (file) => skipped.push(file.name),
      });

      expect(result).toEqual({ file: { name: 'b' }, pageId: 'id-b' });
      expect(skipped).toEqual(['a', 'c']);
      expect(pool).toEqual([]);
    });

    it('pickNextUnlabeled_AllLabeled_ReturnsNullAndEmptiesPool', async () => {
      const pool = [{ name: 'a' }, { name: 'b' }];

      const result = await pickNextUnlabeled({ pool, labeled: new Set(['id-a', 'id-b']), hash: hashByName });

      expect(result).toBeNull();
      expect(pool.length).toBe(0);
    });

    it('pickNextUnlabeled_EmptyPool_ReturnsNull', async () => {
      expect(await pickNextUnlabeled({ pool: [], labeled: new Set(), hash: hashByName })).toBeNull();
    });

    it('pickNextUnlabeled_UnreadableFile_ReportedAndSkipped', async () => {
      const pool = [{ name: 'bad' }, { name: 'ok' }];
      const unreadable: string[] = [];

      const result = await pickNextUnlabeled({
        pool,
        labeled: new Set(),
        hash: (file) => (file.name === 'bad' ? Promise.reject(new Error('io')) : hashByName(file)),
        rng: () => 0,
        onUnreadable: (file) => unreadable.push(file.name),
      });

      expect(result?.pageId).toBe('id-ok');
      expect(unreadable).toEqual(['bad']);
    });

    it('pickNextUnlabeled_Cancelled_ReturnsNull', async () => {
      const pool = [{ name: 'a' }];

      const result = await pickNextUnlabeled({
        pool,
        labeled: new Set(),
        hash: hashByName,
        isCancelled: () => true,
      });

      expect(result).toBeNull();
    });
  });
});

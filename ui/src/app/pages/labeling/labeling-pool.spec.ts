import {
  compareLabelingPaths,
  isLabelingImage,
  pageIdOf,
  pickNextUnlabeled,
  sortLabelingFiles,
  toHex,
} from './labeling-pool';

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

  describe('sortLabelingFiles', () => {
    const file = (path: string) => ({ name: path.split('/').pop()!, webkitRelativePath: path });

    it('sortLabelingFiles_FoldersByNameThenFilesByName', () => {
      const files = [
        file('kitap/b/2.webp'),
        file('kitap/a/2.webp'),
        file('kitap/b/1.webp'),
        file('kitap/a/1.webp'),
      ];

      expect(sortLabelingFiles(files).map((f) => f.webkitRelativePath)).toEqual([
        'kitap/a/1.webp',
        'kitap/a/2.webp',
        'kitap/b/1.webp',
        'kitap/b/2.webp',
      ]);
    });

    it('sortLabelingFiles_NumbersInNaturalOrder', () => {
      const files = [file('k/s10.webp'), file('k/s2.webp'), file('k/s1.webp')];

      expect(sortLabelingFiles(files).map((f) => f.name)).toEqual(['s1.webp', 's2.webp', 's10.webp']);
    });

    it('sortLabelingFiles_FolderOwnFilesBeforeSubfolders', () => {
      const files = [file('k/a/x.webp'), file('k/z.webp'), file('k/a/b/y.webp'), file('k/a/c.webp')];

      expect(sortLabelingFiles(files).map((f) => f.webkitRelativePath)).toEqual([
        'k/z.webp',
        'k/a/c.webp',
        'k/a/x.webp',
        'k/a/b/y.webp',
      ]);
    });

    it('sortLabelingFiles_EmptyRelativePath_FallsBackToName', () => {
      const files = [{ name: 'b.webp', webkitRelativePath: '' }, { name: 'a.webp', webkitRelativePath: '' }];

      expect(sortLabelingFiles(files).map((f) => f.name)).toEqual(['a.webp', 'b.webp']);
    });

    it('sortLabelingFiles_TurkishLettersAndNumericFolders', () => {
      expect(sortLabelingFiles([file('k/t.webp'), file('k/ş.webp'), file('k/s.webp')]).map((f) => f.name)).toEqual([
        's.webp',
        'ş.webp',
        't.webp',
      ]);
      expect(
        sortLabelingFiles([file('k/10/a.webp'), file('k/2/a.webp')]).map((f) => f.webkitRelativePath)
      ).toEqual(['k/2/a.webp', 'k/10/a.webp']);
    });

    it('sortLabelingFiles_CaseOnlyDifference_StableRegardlessOfInputOrder', () => {
      const upperFirst = sortLabelingFiles([file('k/A.webp'), file('k/a.webp')]).map((f) => f.name);
      const lowerFirst = sortLabelingFiles([file('k/a.webp'), file('k/A.webp')]).map((f) => f.name);

      expect(upperFirst).toEqual(lowerFirst);
    });

    it('compareLabelingPaths_CaseInsensitive', () => {
      expect(compareLabelingPaths('k/A.webp', 'k/a.webp')).toBe(0);
      expect(compareLabelingPaths('k/b.webp', 'k/A.webp')).toBeGreaterThan(0);
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
        onSkipLabeled: (file) => skipped.push(file.name),
      });

      expect(result).toEqual({ file: { name: 'b' }, pageId: 'id-b' });
      expect(skipped).toEqual(['a']);
      expect(pool).toEqual([{ name: 'c' }]);
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

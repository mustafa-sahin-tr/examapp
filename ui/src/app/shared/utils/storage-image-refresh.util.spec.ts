import { Observable, Subject, defer, of, throwError } from 'rxjs';
import {
  collectStorageImageUrls,
  createStorageImageResolver,
  findFreshStorageUrl,
} from './storage-image-refresh.util';

const OLD = '/img/exam-questions/questions/q1/question.jpg?X-Amz-Signature=old';
const FRESH = '/img/exam-questions/questions/q1/question.jpg?X-Amz-Signature=fresh';

describe('storage-image-refresh util (issue #365 S3)', () => {
  describe('findFreshStorageUrl', () => {
    it('picks the candidate pointing at the same object with a different signature', () => {
      const candidates = ['/img/exam-questions/questions/q2/question.jpg?s=1', FRESH, null, undefined];
      expect(findFreshStorageUrl(candidates, OLD)).toBe(FRESH);
    });

    it('never returns the identical URL (same signing slice would loop)', () => {
      expect(findFreshStorageUrl([OLD], OLD)).toBeNull();
    });

    it('matches encoded and decoded paths of the same key', () => {
      const failed = '/img/study-pages/books/Fen%20Kitab%C4%B1/page_1.webp?X-Amz-Signature=a';
      const fresh = '/img/study-pages/books/Fen%20Kitab%C4%B1/page_1.webp?X-Amz-Signature=b';
      expect(findFreshStorageUrl([fresh], failed)).toBe(fresh);
      expect(findFreshStorageUrl(['/img/study-pages/books/Fen Kitabı/page_1.webp'], failed)).toBe(
        '/img/study-pages/books/Fen Kitabı/page_1.webp'
      );
    });

    it('returns null when nothing matches or input is empty', () => {
      expect(findFreshStorageUrl(['/img/b/other.jpg?x=1'], OLD)).toBeNull();
      expect(findFreshStorageUrl([FRESH], '')).toBeNull();
      expect(findFreshStorageUrl([FRESH], null)).toBeNull();
    });
  });

  describe('collectStorageImageUrls', () => {
    it('walks nested objects and arrays and keeps only /img URLs', () => {
      const dto: Record<string, unknown> = {
        imageUrl: OLD,
        title: 'x',
        link: 'https://example.com/img/a.png',
        answers: [{ imageUrl: '/img/exam-questions/answers/a.jpg?s=1' }, { imageUrl: null }],
        passage: { imageUrl: '/img/exam-questions/passages/p.jpg?s=1', text: '/not-img' },
      };
      dto['self'] = dto; // döngü

      expect(collectStorageImageUrls(dto).sort()).toEqual(
        [OLD, '/img/exam-questions/answers/a.jpg?s=1', '/img/exam-questions/passages/p.jpg?s=1'].sort()
      );
    });
  });

  describe('createStorageImageResolver', () => {
    it('shares one fetch between images failing together and maps to each fresh URL', () => {
      const fetch = jasmine.createSpy('fetch').and.returnValue(
        of({ question: { imageUrl: FRESH }, answers: [{ imageUrl: '/img/exam-questions/answers/a.jpg?new' }] })
      );
      const resolve = createStorageImageResolver(fetch);
      const results: Array<string | null | undefined> = [];

      resolve(OLD).subscribe((u) => results.push(u));
      resolve('/img/exam-questions/answers/a.jpg?old').subscribe((u) => results.push(u));

      expect(fetch).toHaveBeenCalledTimes(1);
      expect(results).toEqual([FRESH, '/img/exam-questions/answers/a.jpg?new']);
    });

    it('refetches after the TTL and when the cache key changes', () => {
      let now = 0;
      let key = 1;
      const fetch = jasmine.createSpy('fetch').and.returnValue(of({ imageUrl: FRESH }));
      const resolve = createStorageImageResolver(fetch, { ttlMs: 1000, now: () => now, cacheKey: () => key });

      resolve(OLD).subscribe();
      now = 500;
      resolve(OLD).subscribe();
      expect(fetch).toHaveBeenCalledTimes(1);

      now = 1600;
      resolve(OLD).subscribe();
      expect(fetch).toHaveBeenCalledTimes(2);

      key = 2;
      resolve(OLD).subscribe();
      expect(fetch).toHaveBeenCalledTimes(3);
    });

    it('turns a failed fetch into null and retries the fetch next time', () => {
      let calls = 0;
      const fetch = (): Observable<{ imageUrl: string }> =>
        defer(() => (++calls === 1 ? throwError(() => new Error('500')) : of({ imageUrl: FRESH })));
      const resolve = createStorageImageResolver(fetch);
      const results: Array<string | null | undefined> = [];

      resolve(OLD).subscribe((u) => results.push(u));
      resolve(OLD).subscribe((u) => results.push(u));

      expect(results).toEqual([null, FRESH]);
      expect(calls).toBe(2);
    });

    it('uses a custom collector when given', () => {
      const pending = new Subject<{ a: string; b: string }>();
      const resolve = createStorageImageResolver(() => pending, { collect: (dto) => [dto.b] });
      const results: Array<string | null | undefined> = [];

      resolve(OLD).subscribe((u) => results.push(u));
      pending.next({ a: FRESH, b: '/img/exam-questions/questions/q1/question.jpg?b' });

      expect(results).toEqual(['/img/exam-questions/questions/q1/question.jpg?b']);
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { HttpEventType, provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { BookImportUploadEvent } from '../models/book-import';
import { BOOK_IMPORT_PART_RETRY_MS, BookImportService } from './book-import.service';

describe('BookImportService chunked upload (#1122)', () => {
  const base = '/api/exam/book-imports';
  let service: BookImportService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting()] });
    service = TestBed.inject(BookImportService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  // 10 bayt, parça 4 bayt → 4 + 4 + 2.
  const file = () => new File(['%PDF-12345'], 'lgs.pdf', { type: 'application/pdf' });
  const session = { id: 'u1', partSizeBytes: 4, partCount: 3 };

  function begin(events: BookImportUploadEvent[], errors: unknown[] = []) {
    service.start({ file: file(), bookName: 'LGS', gradeId: 8, subjectId: null }).subscribe({
      next: (e) => events.push(e),
      error: (e) => errors.push(e),
    });
    const req = http.expectOne(`${base}/uploads`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ bookName: 'LGS', gradeId: 8, subjectId: null, fileName: 'lgs.pdf', fileSizeBytes: 10 });
    req.flush(session);
  }

  async function partText(n: number) {
    const req = http.expectOne(`${base}/uploads/u1/parts/${n}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('Content-Type')).toBe('application/octet-stream');
    return { req, text: await (req.request.body as Blob).text() };
  }

  it('start_SplitsTheFileSendsPartsInOrderThenCompletes', async () => {
    const events: BookImportUploadEvent[] = [];
    begin(events);

    const sent: string[] = [];
    for (const n of [1, 2, 3]) {
      const { req, text } = await partText(n);
      expect(req.request.reportProgress).toBe(true);
      sent.push(text);
      if (n === 2) req.event({ type: HttpEventType.UploadProgress, loaded: 1, total: 4 });
      req.flush(null);
    }
    http.expectOne({ method: 'POST', url: `${base}/uploads/u1/complete` }).flush({ id: 'job-1' });

    expect(sent).toEqual(['%PDF', '-123', '45']);
    const p = (part: number, sentBytes: number) => ({ type: 'progress', part, parts: 3, sentBytes, totalBytes: 10 });
    expect(events).toEqual([
      p(0, 0),
      p(1, 4),
      p(1, 5), // 2. parçanın içinden: 4 + 1 bayt
      p(2, 8),
      p(3, 10),
      { type: 'assembling' },
      { type: 'done', job: { id: 'job-1' } },
    ]);
  });

  it('start_RetriesAPartAfterANetworkError', async () => {
    vi.useFakeTimers();
    const events: BookImportUploadEvent[] = [];
    begin(events);

    (await partText(1)).req.flush(null);
    (await partText(2)).req.error(new ProgressEvent('error'), { status: 0 });
    await vi.advanceTimersByTimeAsync(BOOK_IMPORT_PART_RETRY_MS);
    (await partText(2)).req.flush(null);
    (await partText(3)).req.flush(null);
    http.expectOne(`${base}/uploads/u1/complete`).flush({ id: 'job-1' });

    expect(events.at(-1)).toEqual({ type: 'done', job: { id: 'job-1' } });
  });

  it('start_ClientErrorIsNotRetriedAndAbortsTheSession', async () => {
    const events: BookImportUploadEvent[] = [];
    const errors: unknown[] = [];
    begin(events, errors);

    (await partText(1)).req.flush({ code: 'bookImport.notPdf' }, { status: 400, statusText: 'Bad Request' });

    http.expectOne({ method: 'DELETE', url: `${base}/uploads/u1` }).flush(null);
    expect(errors).toHaveLength(1);
    expect(events).toEqual([{ type: 'progress', part: 0, parts: 3, sentBytes: 0, totalBytes: 10 }]);
  });
});

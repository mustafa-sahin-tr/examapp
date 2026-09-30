import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { WorksheetCommentService } from './worksheet-comment.service';
import { WorksheetComment, WorksheetCommentPage } from '../models/worksheet-comment.model';

describe('WorksheetCommentService (issue #105)', () => {
  let service: WorksheetCommentService;
  let http: HttpTestingController;
  const base = '/api/exam/worksheet/12/comments';

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(WorksheetCommentService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getThread_WorksheetLevelFirstPage_GetsWithoutQuestionOrCursor', () => {
    const page: WorksheetCommentPage = { items: [], nextCursor: null, canWrite: true, lockReason: null };
    let result: WorksheetCommentPage | undefined;

    service.getThread(12, { take: 20 }).subscribe((p) => (result = p));

    const req = http.expectOne((r) => r.url === base);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('questionId')).toBeFalse();
    expect(req.request.params.has('cursor')).toBeFalse();
    expect(req.request.params.get('take')).toBe('20');
    req.flush(page);
    expect(result).toEqual(page);
  });

  it('getThread_QuestionAndCursor_SendsBoth', () => {
    service.getThread(12, { questionId: 34, cursor: 'c1', take: 20 }).subscribe();

    const req = http.expectOne((r) => r.url === base);
    expect(req.request.params.get('questionId')).toBe('34');
    expect(req.request.params.get('cursor')).toBe('c1');
    req.flush({ items: [], nextCursor: null, canWrite: false, lockReason: 'question-not-answered' });
  });

  it('getThread_NullQuestionAndEmptyCursor_Omitted', () => {
    service.getThread(12, { questionId: null, cursor: '' }).subscribe();

    const req = http.expectOne((r) => r.url === base);
    expect(req.request.params.keys()).toEqual([]);
    req.flush({ items: [], nextCursor: null, canWrite: false, lockReason: null });
  });

  it('getReplies_RootAndCursor_HitsRepliesEndpoint', () => {
    service.getReplies(12, 56, { cursor: 'r1', take: 50 }).subscribe();

    const req = http.expectOne((r) => r.url === `${base}/56/replies`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('cursor')).toBe('r1');
    expect(req.request.params.get('take')).toBe('50');
    req.flush({ items: [], nextCursor: null, canReply: true, replyCount: 0 });
  });

  it('getReplies_FirstPage_NoCursorParam', () => {
    service.getReplies(12, 56).subscribe();

    const req = http.expectOne((r) => r.url === `${base}/56/replies`);
    expect(req.request.params.has('cursor')).toBeFalse();
    req.flush({ items: [], nextCursor: null, canReply: true, replyCount: 0 });
  });

  it('create_PostsBodyAndReturnsComment', () => {
    const created: WorksheetComment = {
      id: 7,
      worksheetId: 12,
      questionId: 34,
      parentCommentId: 56,
      authorDisplayName: 'Ali K.',
      authorRole: 'Student',
      isMine: true,
      body: 'Soru',
      createdAt: '2026-09-30T10:00:00Z',
    };
    let result: WorksheetComment | undefined;

    service.create(12, { questionId: 34, parentCommentId: 56, body: 'Soru' }).subscribe((c) => (result = c));

    const req = http.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ questionId: 34, parentCommentId: 56, body: 'Soru' });
    req.flush(created, { status: 201, statusText: 'Created' });
    expect(result).toEqual(created);
  });

  // ---- Issue #305: moderasyon ---------------------------------------------------------------------------------

  it('getThread_ModeratorView_SendsFlag; falseOmitted', () => {
    service.getThread(12, { moderatorView: true }).subscribe();
    const on = http.expectOne((r) => r.url === base);
    expect(on.request.params.get('moderatorView')).toBe('true');
    on.flush({ items: [], nextCursor: null, canWrite: true, lockReason: null });

    service.getThread(12, { moderatorView: false }).subscribe();
    const off = http.expectOne((r) => r.url === base);
    expect(off.request.params.has('moderatorView')).toBeFalse();
    off.flush({ items: [], nextCursor: null, canWrite: true, lockReason: null });
  });

  it('getReplies_ModeratorView_SendsFlag', () => {
    service.getReplies(12, 56, { moderatorView: true }).subscribe();

    const req = http.expectOne((r) => r.url === `${base}/56/replies`);
    expect(req.request.params.get('moderatorView')).toBe('true');
    req.flush({ items: [], nextCursor: null, canReply: false, replyCount: 0 });
  });

  it('report_PostsReasonAndTrimmedNote', () => {
    let result: unknown;
    service.report(12, 7, { reason: 'personalInfo', note: '  telefon yazmış  ' }).subscribe((r) => (result = r));

    const req = http.expectOne(`${base}/7/report`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'personalInfo', note: 'telefon yazmış' });
    req.flush({ alreadyReported: false, reportedByMe: true });
    expect(result).toEqual({ alreadyReported: false, reportedByMe: true });
  });

  it('report_BlankNote_Omitted', () => {
    service.report(12, 7, { reason: 'spam', note: '   ' }).subscribe();

    const req = http.expectOne(`${base}/7/report`);
    expect(req.request.body).toEqual({ reason: 'spam' });
    req.flush({ alreadyReported: true, reportedByMe: true });
  });

  it('hide_PostsTrimmedReason; unhide_PostsWithoutBody', () => {
    service.hide(12, 7, ' kişisel bilgi ').subscribe();
    const hide = http.expectOne(`${base}/7/hide`);
    expect(hide.request.method).toBe('POST');
    expect(hide.request.body).toEqual({ reason: 'kişisel bilgi' });
    hide.flush({});

    service.unhide(12, 7).subscribe();
    const unhide = http.expectOne(`${base}/7/unhide`);
    expect(unhide.request.method).toBe('POST');
    expect(unhide.request.body).toBeNull();
    unhide.flush({});
  });

  it('getReports_WorksheetEndpointWithPaging', () => {
    service.getReports(12, { page: 2, pageSize: 20 }).subscribe();

    const req = http.expectOne((r) => r.url === `${base}/reports`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({ items: [], page: 2, pageSize: 20, totalCount: 0 });
  });

  it('getAdminReports_AdminEndpointViaGateway', () => {
    service.getAdminReports({ page: 1 }).subscribe();

    const req = http.expectOne((r) => r.url === '/api/exam/admin/comments/reports');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.has('pageSize')).toBeFalse();
    req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });
});

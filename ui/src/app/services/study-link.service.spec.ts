import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  StudyLinkService,
  isActiveLimitError,
  isTeacherNotApprovedError,
  rateLimitRetryAfter,
  studyLinkErrorCode,
  studyLinkErrorMessage,
} from './study-link.service';

describe('StudyLinkService (issue #61)', () => {
  let service: StudyLinkService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(StudyLinkService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list_SubTopicScope_SendsOnlySubTopicIdAndDefaults', () => {
    service.list({ subTopicId: 12 }).subscribe();

    const req = http.expectOne((r) => r.url === '/api/exam/study-links');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('subTopicId')).toBe('12');
    expect(req.request.params.has('topicId')).toBeFalse();
    expect(req.request.params.get('includeInactive')).toBe('true');
    expect(req.request.params.get('skip')).toBe('0');
    expect(req.request.params.get('take')).toBe('50');
    req.flush({ success: true, items: [], totalCount: 0, activeCount: 0, maxActiveLinks: 7 });
  });

  it('list_TopicScope_SendsTopicIdAndCustomPaging', () => {
    service.list({ topicId: 3, includeInactive: false, skip: 10, take: 20 }).subscribe();

    const req = http.expectOne((r) => r.url === '/api/exam/study-links');
    expect(req.request.params.get('topicId')).toBe('3');
    expect(req.request.params.has('subTopicId')).toBeFalse();
    expect(req.request.params.get('includeInactive')).toBe('false');
    expect(req.request.params.get('skip')).toBe('10');
    expect(req.request.params.get('take')).toBe('20');
    req.flush({ success: true, items: [], totalCount: 0, activeCount: 0, maxActiveLinks: 7 });
  });

  it('getById_GetsSingleLink', () => {
    service.getById(5).subscribe();
    const req = http.expectOne('/api/exam/study-links/5');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('create_PostsBody', () => {
    const body = { subTopicId: 4, title: 'Kesirler', url: 'https://youtu.be/x', sourceType: 'YouTube' as const, isActive: true };
    service.create(body).subscribe();
    const req = http.expectOne('/api/exam/study-links');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('update_PutsBodyToId', () => {
    const body = { title: 'A', url: 'https://a.com', isActive: false };
    service.update(9, body).subscribe();
    const req = http.expectOne('/api/exam/study-links/9');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('delete_DeletesId', () => {
    service.delete(9).subscribe();
    const req = http.expectOne('/api/exam/study-links/9');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('reorder_PutsScopeAndItems', () => {
    const body = { topicId: 2, items: [{ id: 1, sortOrder: 0 }, { id: 2, sortOrder: 1 }] };
    service.reorder(body).subscribe();
    const req = http.expectOne('/api/exam/study-links/reorder');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush({ success: true, items: [], totalCount: 0, activeCount: 0, maxActiveLinks: 7 });
  });

  it('getForResult_GetsSuggestionsForInstance', () => {
    let result: unknown;
    service.getForResult(77).subscribe((r) => (result = r));
    const req = http.expectOne('/api/exam/study-links/for-result/77');
    expect(req.request.method).toBe('GET');
    req.flush([]);
    expect(result).toEqual([]);
  });

  describe('error helpers', () => {
    it('isActiveLimitError_409WithErrorCode_True', () => {
      const err = new HttpErrorResponse({ status: 409, error: { success: false, conflict: true, errorCode: 'ActiveLimitReached' } });
      expect(isActiveLimitError(err)).toBeTrue();
      expect(isActiveLimitError(new HttpErrorResponse({ status: 400, error: {} }))).toBeFalse();
    });

    it('isActiveLimitError_TotalLimitOrCodelessConflict_False', () => {
      const total = new HttpErrorResponse({ status: 409, error: { conflict: true, errorCode: 'TotalLimitReached' } });
      expect(isActiveLimitError(total)).toBeFalse();
      expect(studyLinkErrorCode(total)).toBe('TotalLimitReached');
      expect(isActiveLimitError(new HttpErrorResponse({ status: 409, error: { conflict: true } }))).toBeFalse();
    });

    it('isTeacherNotApprovedError_Only403WithCode', () => {
      expect(
        isTeacherNotApprovedError(new HttpErrorResponse({ status: 403, error: { errorCode: 'TeacherNotApproved' } }))
      ).toBeTrue();
      expect(isTeacherNotApprovedError(new HttpErrorResponse({ status: 403, error: { errorCode: 'NotOwner' } }))).toBeFalse();
      expect(isTeacherNotApprovedError(new HttpErrorResponse({ status: 403, error: null }))).toBeFalse();
    });

    it('rateLimitRetryAfter_ReadsHeaderOnlyFor429', () => {
      const withHeader = new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '42' }) });
      expect(rateLimitRetryAfter(withHeader)).toBe(42);
      expect(rateLimitRetryAfter(new HttpErrorResponse({ status: 429 }))).toBeNull();
      expect(rateLimitRetryAfter(new HttpErrorResponse({ status: 409 }))).toBeUndefined();
    });

    it('studyLinkErrorMessage_PlainText429Body_ReturnsText', () => {
      expect(studyLinkErrorMessage(new HttpErrorResponse({ status: 429, error: 'Çok fazla istek.' }))).toBe('Çok fazla istek.');
      expect(studyLinkErrorMessage(new HttpErrorResponse({ status: 429, error: '  ' }))).toBeNull();
    });

    it('studyLinkErrorMessage_PrefersMessageThenValidationErrors', () => {
      expect(studyLinkErrorMessage(new HttpErrorResponse({ status: 409, error: { message: 'Sınır dolu' } }))).toBe('Sınır dolu');
      expect(
        studyLinkErrorMessage(new HttpErrorResponse({ status: 400, error: { errors: { Url: ['URL geçersiz'] } } }))
      ).toBe('URL geçersiz');
      expect(studyLinkErrorMessage(new HttpErrorResponse({ status: 500, error: null }))).toBeNull();
      expect(studyLinkErrorMessage(new Error('x'))).toBeNull();
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { QuestionBankService } from './question-bank.service';
import { QuestionBankFilter, QuestionBankPage } from '../models/question-bank';

const EMPTY_PAGE: QuestionBankPage = { items: [], total: 0, skip: 0, take: 0 };
const URL = '/api/exam/questions/bank';

describe('QuestionBankService (issue #471)', () => {
  let service: QuestionBankService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(QuestionBankService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('search_EmptyFilter_SendsGetWithoutQueryParams', () => {
    service.search({}).subscribe();

    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
    req.flush(EMPTY_PAGE);
  });

  it('search_NullUndefinedAndEmptyString_AreSkipped', () => {
    const filter = {
      gradeId: null,
      subjectId: undefined,
      topicId: '',
      source: null,
      scope: 'mine',
    } as unknown as QuestionBankFilter;

    service.search(filter).subscribe();

    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.params.keys()).toEqual(['scope']);
    expect(req.request.params.get('scope')).toBe('mine');
    req.flush(EMPTY_PAGE);
  });

  it('search_FalseAndZeroValues_AreKept', () => {
    service.search({ classified: false, hasCorrectAnswer: false, inTest: false, skip: 0, take: 24 }).subscribe();

    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.params.get('classified')).toBe('false');
    expect(req.request.params.get('hasCorrectAnswer')).toBe('false');
    expect(req.request.params.get('inTest')).toBe('false');
    expect(req.request.params.get('skip')).toBe('0');
    expect(req.request.params.get('take')).toBe('24');
    req.flush(EMPTY_PAGE);
  });

  it('search_SubTopicIdsArray_AppendsRepeatedParams', () => {
    service.search({ subTopicIds: [1, 2] }).subscribe();

    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.params.getAll('subTopicIds')).toEqual(['1', '2']);
    expect(req.request.urlWithParams).toContain('subTopicIds=1&subTopicIds=2');
    req.flush(EMPTY_PAGE);
  });

  it('search_EmptyArray_OmitsParam', () => {
    service.search({ subTopicIds: [] }).subscribe();

    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.params.has('subTopicIds')).toBeFalse();
    req.flush(EMPTY_PAGE);
  });

  it('search_FullFilter_SerializesEveryScalarField', () => {
    service
      .search({
        scope: 'all',
        gradeId: 5,
        subjectId: 7,
        topicId: 9,
        subTopicIds: [11],
        minDifficulty: 4,
        maxDifficulty: 7,
        source: 'AI',
        classified: true,
        hasCorrectAnswer: true,
        skip: 24,
        take: 12,
      })
      .subscribe();

    const req = http.expectOne((r) => r.url === URL);
    const p = req.request.params;
    expect(p.get('scope')).toBe('all');
    expect(p.get('gradeId')).toBe('5');
    expect(p.get('subjectId')).toBe('7');
    expect(p.get('topicId')).toBe('9');
    expect(p.getAll('subTopicIds')).toEqual(['11']);
    expect(p.get('minDifficulty')).toBe('4');
    expect(p.get('maxDifficulty')).toBe('7');
    expect(p.get('source')).toBe('AI');
    expect(p.get('classified')).toBe('true');
    expect(p.get('hasCorrectAnswer')).toBe('true');
    expect(p.get('skip')).toBe('24');
    expect(p.get('take')).toBe('12');
    req.flush(EMPTY_PAGE);
  });

  it('search_Response_ReturnsPageUnchanged', () => {
    const page: QuestionBankPage = { items: [], total: 3, skip: 0, take: 24 };
    let received: QuestionBankPage | undefined;

    service.search({}).subscribe((p) => (received = p));
    http.expectOne((r) => r.url === URL).flush(page);

    expect(received).toEqual(page);
  });

  it('search_ServerError_PropagatesErrorToSubscriber', () => {
    let failed = false;

    service.search({}).subscribe({ error: () => (failed = true) });
    http.expectOne((r) => r.url === URL).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(failed).toBeTrue();
  });
});

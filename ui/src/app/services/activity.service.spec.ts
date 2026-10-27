import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ActivityService } from './activity.service';

describe('ActivityService (#573)', () => {
  let service: ActivityService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting()] });
    service = TestBed.inject(ActivityService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('check_PostsAnswersToGatewayInstanceRoute', () => {
    const answers = [{ instanceQuestionId: 5, selectedAnswerId: null, answerPayload: '{}' }];
    service.check(42, answers).subscribe();

    const req = http.expectOne('/api/exam/test-instances/42/check');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ answers });
    req.flush({ success: true, correctCount: 0, totalCount: 0, questions: [] });
  });

  it('getSolutions_GetsSolutionsRoute', () => {
    service.getSolutions(42).subscribe();

    const req = http.expectOne('/api/exam/test-instances/42/solutions');
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, questions: [] });
  });

  it('retake_PostsWithoutBodyToRetakeRoute (#609)', () => {
    let result: unknown;
    service.retake(42).subscribe((r) => (result = r));

    const req = http.expectOne('/api/exam/test-instances/42/retake');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ success: true, instanceId: 42, alreadyRetaking: false });
    expect(result).toEqual({ success: true, instanceId: 42, alreadyRetaking: false });
  });
});

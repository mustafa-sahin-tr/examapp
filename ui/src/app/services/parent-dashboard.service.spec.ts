import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ParentDashboardService } from './parent-dashboard.service';
import { ParentChildSummary } from '../models/parent-dashboard.model';

/** Issue #420: veli paneli özeti gateway `/api/exam/parent/children/{studentId}/summary` altında. */
describe('ParentDashboardService (issue #420)', () => {
  let service: ParentDashboardService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ParentDashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getChildSummary_GetsSummaryForStudent', () => {
    const body: ParentChildSummary = {
      studentId: 42,
      weekStart: '2026-10-05',
      questionsSolvedThisWeek: 12,
      assignments: { completed: 2, overdue: 1, pending: 3, windowDays: 30 },
      totalPoints: 340,
      lastActivityAt: '2026-10-07T08:00:00Z',
    };
    let received: ParentChildSummary | undefined;
    service.getChildSummary(42).subscribe((s) => (received = s));

    const req = http.expectOne('/api/exam/parent/children/42/summary');
    expect(req.request.method).toBe('GET');
    req.flush(body);
    expect(received).toEqual(body);
  });
  it('getChildAssignments_SendsPageAndOptionalStatus', () => {
    service.getChildAssignments(42, null).subscribe();
    const all = http.expectOne((r) => r.url === '/api/exam/parent/children/42/assignments');
    expect(all.request.method).toBe('GET');
    expect(all.request.params.get('page')).toBe('1');
    expect(all.request.params.has('status')).toBeFalse();
    all.flush({});

    service.getChildAssignments(42, 'overdue', 3).subscribe();
    const filtered = http.expectOne((r) => r.url === '/api/exam/parent/children/42/assignments');
    expect(filtered.request.params.get('status')).toBe('overdue');
    expect(filtered.request.params.get('page')).toBe('3');
    filtered.flush({});
  });

  it('getChildTestResult_GetsTheInstanceSummary', () => {
    service.getChildTestResult(42, 501).subscribe();
    const req = http.expectOne('/api/exam/parent/children/42/test-results/501');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });
});

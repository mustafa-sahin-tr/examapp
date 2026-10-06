import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { StudentSchoolRequestService } from './student-school-request.service';

describe('StudentSchoolRequestService (issue #361)', () => {
  let service: StudentSchoolRequestService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(StudentSchoolRequestService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list goes through the gateway with paging and updates the pending count', () => {
    service.list(2, 10).subscribe();

    const req = http.expectOne((r) => r.url === '/api/exam/student-school-requests');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    req.flush({ pageNumber: 2, pageSize: 10, totalCount: 13, items: [] });

    expect(service.pendingCount()).toBe(13);
  });

  it('refreshPendingCount reads { count }', () => {
    let value = -1;
    service.refreshPendingCount().subscribe((c) => (value = c));
    http.expectOne('/api/exam/student-school-requests/count').flush({ count: 4 });
    expect(value).toBe(4);
    expect(service.pendingCount()).toBe(4);
  });

  it('approve and reject POST to the student-scoped decision endpoints', () => {
    service.approve(7).subscribe();
    service.reject(8).subscribe();

    const approve = http.expectOne('/api/exam/student-school-requests/7/approve');
    expect(approve.request.method).toBe('POST');
    approve.flush({ message: 'ok' });
    const reject = http.expectOne('/api/exam/student-school-requests/8/reject');
    expect(reject.request.method).toBe('POST');
    reject.flush({ message: 'ok' });
  });
});

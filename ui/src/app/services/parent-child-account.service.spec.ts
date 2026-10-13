import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ParentChildAccountService } from './parent-child-account.service';
import { CreateChildAccountRequest, PARENT_CONSENT_VERSION } from '../models/parent-child-account.model';

/** Issue #438: gateway yolları (/api/exam/parent/children...) ve gövdeler. */
describe('ParentChildAccountService (issue #438)', () => {
  let service: ParentChildAccountService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ParentChildAccountService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('create posts the request to /api/exam/parent/children', () => {
    const body = {
      firstName: 'Ali',
      lastName: 'Kaya',
      username: 'ali.kaya',
      gradeId: 3,
      schoolId: null,
      studentNumber: null,
      consentAccepted: true,
      consentVersion: PARENT_CONSENT_VERSION,
    } as Omit<CreateChildAccountRequest, 'password'>;
    let result: unknown;
    service.create({ ...body, password: 'x' } as CreateChildAccountRequest).subscribe((r) => (result = r));

    const req = http.expectOne('/api/exam/parent/children');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.username).toBe('ali.kaya');
    req.flush({ studentId: 1, linkId: 2, username: 'ali.kaya', studentName: 'Ali Kaya' }, { status: 201, statusText: 'Created' });
    expect(result).toEqual({ studentId: 1, linkId: 2, username: 'ali.kaya', studentName: 'Ali Kaya' });
  });

  it('resetPassword posts newPassword to the child-scoped url', () => {
    service.resetPassword(41, 'Defter77yz').subscribe();

    const req = http.expectOne('/api/exam/parent/children/41/reset-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ newPassword: 'Defter77yz' });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});

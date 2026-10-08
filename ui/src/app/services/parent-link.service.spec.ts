import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ParentLinkService } from './parent-link.service';
import { formatInviteCode } from '../models/parent-link.model';

/** Issue #419/#436: veli–öğrenci bağlantısı uçları gateway `/api/exam/parent-links/...` altında. */
describe('ParentLinkService (issue #419, #436)', () => {
  let service: ParentLinkService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ParentLinkService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('createSecondParentCode_PostsToLinkSecondParentCode (issue #436)', () => {
    service.createSecondParentCode(7).subscribe((r) => expect(r.code).toBe('ABCDEFGHJKMN'));
    const req = http.expectOne('/api/exam/parent-links/7/second-parent-code');
    expect(req.request.method).toBe('POST');
    req.flush({ code: 'ABCDEFGHJKMN', expiresAt: '2026-10-09T09:00:00Z' });
  });

  it('getMyParents_And_getMyChildren_UseGet', () => {
    service.getMyParents().subscribe();
    service.getMyChildren().subscribe();
    expect(http.expectOne('/api/exam/parent-links/my-parents').request.method).toBe('GET');
    http.expectOne('/api/exam/parent-links/my-children').flush([]);
  });

  it('redeem_PostsCodeInBody', () => {
    service.redeem('ABCDEFGHJKMN').subscribe();
    const req = http.expectOne('/api/exam/parent-links/redeem');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'ABCDEFGHJKMN' });
    req.flush({
      linkId: 1,
      status: 'Pending',
      studentName: null,
      gradeName: null,
      schoolName: null,
      linkedAt: null,
      requestedAt: '',
      pendingExpiresAt: '',
    });
  });

  it('revoke_PostsToLinkRevoke', () => {
    service.revoke(42).subscribe();
    const req = http.expectOne('/api/exam/parent-links/42/revoke');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('approve_And_reject_PostToLinkActions', () => {
    service.approve(5).subscribe();
    service.reject(6).subscribe();
    const approve = http.expectOne('/api/exam/parent-links/5/approve');
    const reject = http.expectOne('/api/exam/parent-links/6/reject');
    expect(approve.request.method).toBe('POST');
    expect(reject.request.method).toBe('POST');
    approve.flush(null, { status: 204, statusText: 'No Content' });
    reject.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('extractError_PrefersServerMessage_FallsBackOtherwise', () => {
    const withBody = new HttpErrorResponse({
      status: 400,
      error: { message: 'Kod geçersiz', errorCode: 'InvalidCode' },
    });
    expect(service.extractError(withBody, 'yedek')).toBe('Kod geçersiz');
    expect(service.errorCode(withBody)).toBe('InvalidCode');
    expect(service.extractError(new HttpErrorResponse({ status: 500 }), 'yedek')).toBe('yedek');
  });

  it('formatInviteCode_GroupsTwelveCharsInFours', () => {
    expect(formatInviteCode('abcdefghjkmn')).toBe('ABCD-EFGH-JKMN');
    expect(formatInviteCode('ABC')).toBe('ABC');
  });
});

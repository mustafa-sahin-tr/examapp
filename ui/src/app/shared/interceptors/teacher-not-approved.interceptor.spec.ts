import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthService } from '../../services/auth.service';
import { teacherNotApprovedInterceptor } from './teacher-not-approved.interceptor';

/** Issue #287: 403 `TeacherNotApproved` → AuthService.handleTeacherNotApproved (yönlendirme + profil yenileme). */
describe('teacherNotApprovedInterceptor (issue #287)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['handleTeacherNotApproved']);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([teacherNotApprovedInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function request(url = '/api/exam/study-links'): { error?: HttpErrorResponse } {
    const outcome: { error?: HttpErrorResponse } = {};
    http.get(url).subscribe({ error: (e: HttpErrorResponse) => (outcome.error = e) });
    return outcome;
  }

  it('403TeacherNotApproved_CallsHandlerAndStillPropagatesError', () => {
    const outcome = request();
    httpMock
      .expectOne('/api/exam/study-links')
      .flush(
        { success: false, errorCode: 'TeacherNotApproved', message: 'Onay bekliyor' },
        { status: 403, statusText: 'Forbidden' }
      );

    expect(auth.handleTeacherNotApproved).toHaveBeenCalledTimes(1);
    expect(outcome.error?.status).toBe(403);
    expect(outcome.error?.error.message).toBe('Onay bekliyor');
  });

  it('403WithoutErrorCode_IsIgnored', () => {
    const outcome = request();
    httpMock.expectOne('/api/exam/study-links').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(auth.handleTeacherNotApproved).not.toHaveBeenCalled();
    expect(outcome.error?.status).toBe(403);
  });

  it('403OtherErrorCode_IsIgnored', () => {
    request();
    httpMock
      .expectOne('/api/exam/study-links')
      .flush({ success: false, errorCode: 'NotOwner' }, { status: 403, statusText: 'Forbidden' });

    expect(auth.handleTeacherNotApproved).not.toHaveBeenCalled();
  });

  it('OtherStatusWithTeacherNotApprovedBody_IsIgnored', () => {
    request();
    httpMock
      .expectOne('/api/exam/study-links')
      .flush({ errorCode: 'TeacherNotApproved' }, { status: 400, statusText: 'Bad Request' });

    expect(auth.handleTeacherNotApproved).not.toHaveBeenCalled();
  });

  it('CrossOrigin403_IsIgnored', () => {
    request('https://third-party.example.com/x');
    httpMock
      .expectOne('https://third-party.example.com/x')
      .flush({ errorCode: 'TeacherNotApproved' }, { status: 403, statusText: 'Forbidden' });

    expect(auth.handleTeacherNotApproved).not.toHaveBeenCalled();
  });

  it('Success_DoesNothing', () => {
    request();
    httpMock.expectOne('/api/exam/study-links').flush([]);
    expect(auth.handleTeacherNotApproved).not.toHaveBeenCalled();
  });
});

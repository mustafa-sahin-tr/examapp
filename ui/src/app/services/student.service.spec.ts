import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { StudentService } from './student.service';

describe('StudentService', () => {
  let service: StudentService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [StudentService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(StudentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('getLastLogin', () => {
    it('getLastLogin_Called_GetsMeLastLoginEndpoint', () => {
      service.getLastLogin().subscribe();

      const req = httpMock.expectOne('/api/exam/student/me/last-login');
      expect(req.request.method).toBe('GET');
      req.flush({ lastLoginAtUtc: '2026-09-08T12:00:00Z' });
    });

    it('getLastLogin_ServerReturnsTimestamp_PassesResponseThrough', (done) => {
      service.getLastLogin().subscribe((res) => {
        expect(res).toEqual({ lastLoginAtUtc: '2026-09-08T12:00:00Z' });
        done();
      });

      httpMock.expectOne('/api/exam/student/me/last-login').flush({ lastLoginAtUtc: '2026-09-08T12:00:00Z' });
    });

    it('getLastLogin_FirstLoginHasNoPreviousLogin_PassesNullThrough', (done) => {
      service.getLastLogin().subscribe((res) => {
        expect(res.lastLoginAtUtc).toBeNull();
        done();
      });

      httpMock.expectOne('/api/exam/student/me/last-login').flush({ lastLoginAtUtc: null });
    });
  });
});

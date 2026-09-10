import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { BadgeService } from './badge.service';

describe('BadgeService', () => {
  let service: BadgeService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });

    service = TestBed.inject(BadgeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('getUserActivity', () => {
    it('getUserActivity_ApiReturns500_ErrorPropagatesWithoutMockFallback', () => {
      const userId = 16;
      let nextCalled = false;
      let capturedError: unknown = null;

      service.getUserActivity(userId).subscribe({
        next: () => {
          nextCalled = true;
        },
        error: (error) => {
          capturedError = error;
        },
      });

      const req = httpMock.expectOne(`/api/badge/reports/users/${userId}/activity`);
      expect(req.request.method).toBe('GET');
      req.flush('Internal Server Error', { status: 500, statusText: 'Internal Server Error' });

      expect(nextCalled).toBeFalse();
      expect(capturedError).not.toBeNull();
      expect((capturedError as { status: number }).status).toBe(500);
    });
  });

  describe('getUserBadgeProgress', () => {
    it('getUserBadgeProgress_ApiReturns500_ErrorPropagatesWithoutMockFallback', () => {
      const userId = 16;
      let nextCalled = false;
      let capturedError: unknown = null;

      service.getUserBadgeProgress(userId).subscribe({
        next: () => {
          nextCalled = true;
        },
        error: (error) => {
          capturedError = error;
        },
      });

      const req = httpMock.expectOne(`/api/badge/reports/users/${userId}/badge-progress`);
      expect(req.request.method).toBe('GET');
      req.flush('Internal Server Error', { status: 500, statusText: 'Internal Server Error' });

      expect(nextCalled).toBeFalse();
      expect(capturedError).not.toBeNull();
      expect((capturedError as { status: number }).status).toBe(500);
    });
  });
});

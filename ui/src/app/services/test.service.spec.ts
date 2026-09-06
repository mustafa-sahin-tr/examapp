import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';

import { TestService } from './test.service';

describe('TestService', () => {
  let service: TestService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        TestService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
      ],
    });

    service = TestBed.inject(TestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('copyWorksheet', () => {
    it('copyWorksheet_Called_PostsToCopyEndpointWithNullBody', () => {
      service.copyWorksheet(42).subscribe();

      const req = httpMock.expectOne('/api/exam/worksheet/42/copy');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toBeNull();
      req.flush({ worksheetId: 99 });
    });

    it('copyWorksheet_ServerReturnsNewWorksheetId_PassesResponseThrough', (done) => {
      service.copyWorksheet(7).subscribe((res) => {
        expect(res).toEqual({ worksheetId: 123 } as any);
        done();
      });

      httpMock.expectOne('/api/exam/worksheet/7/copy').flush({ worksheetId: 123 });
    });

    it('copyWorksheet_ServerErrors_PropagatesError', (done) => {
      service.copyWorksheet(5).subscribe({
        next: () => done.fail('should not succeed'),
        error: (err) => {
          expect(err.status).toBe(403);
          done();
        },
      });

      httpMock
        .expectOne('/api/exam/worksheet/5/copy')
        .flush({ message: 'yetki yok' }, { status: 403, statusText: 'Forbidden' });
    });
  });
});

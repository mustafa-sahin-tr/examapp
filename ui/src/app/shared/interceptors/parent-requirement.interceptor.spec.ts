import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';

import { parentRequirementInterceptor } from './parent-requirement.interceptor';
import { ParentRequirementHandler } from '../../services/parent-requirement-handler.service';
import { ParentStatusStore } from '../../services/parent-status.store';
import { StudentParentService } from '../../services/student-parent.service';
import { ParentStatus } from '../../models/parent-status.model';
import { parentStatusOf } from '../testing/parent-status-testing';
import { translocoTestingModule } from '../testing/transloco-testing';

/** Issue #440 / #441: veli kuralı 403'lerinin istemci tepkisi. */
describe('parentRequirementInterceptor + ParentRequirementHandler', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let store: ParentStatusStore;
  let reload: jasmine.Spy;
  let reloadResult: Subject<ParentStatus | null>;
  let router: Router;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  beforeEach(() => {
    reloadResult = new Subject<ParentStatus | null>();
    reload = jasmine.createSpy('reload').and.callFake(() => reloadResult.asObservable());
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    TestBed.configureTestingModule({
      imports: [translocoTestingModule()],
      providers: [
        provideHttpClient(withXhr(), withInterceptors([parentRequirementInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: StudentParentService, useValue: { reload } },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    store = TestBed.inject(ParentStatusStore);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
  });

  afterEach(() => httpMock.verify());

  function fail403(url: string, errorCode: string, method: 'GET' | 'POST' = 'GET'): unknown {
    let received: unknown;
    const request$ = method === 'GET' ? http.get(url) : http.post(url, {});
    request$.subscribe({ error: (e: unknown) => (received = e) });
    httpMock.expectOne(url).flush({ success: false, errorCode, message: 'x' }, { status: 403, statusText: 'Forbidden' });
    return received;
  }

  function resolveReload(status: ParentStatus): void {
    store.set(status);
    reloadResult.next(status);
    reloadResult.complete();
  }

  it('parentRequired_ExpiredAfterReload_NavigatesToParentRequired_AndRethrows', () => {
    const error = fail403('/api/exam/student/tests', 'ParentRequired');
    expect(error).toBeTruthy();
    expect(reload).toHaveBeenCalledTimes(1);

    resolveReload(parentStatusOf({ mode: 'Expired' }));

    expect(router.navigateByUrl).toHaveBeenCalledWith('/parent-required');
  });

  it('parentRequired_NotExpired_DoesNotNavigate (booking "velisi yok" yanıtı)', () => {
    fail403('/api/exam/bookings', 'ParentRequired', 'POST');
    resolveReload(parentStatusOf({ mode: 'Grace', daysLeft: 20 }));

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('parentRequired_ConcurrentErrors_ReloadOnce', () => {
    fail403('/api/exam/a', 'ParentRequired');
    fail403('/api/exam/b', 'ParentRequired');
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('activationRequired_Post_ReloadsAndShowsSnackbar', () => {
    reload.and.returnValue(of(null));
    fail403('/api/exam/direct-messages', 'ParentActivationRequired', 'POST');

    expect(reload).toHaveBeenCalled();
    expect(snackBar.open).toHaveBeenCalledWith(
      'Veliniz hesabını aktive edene kadar bu özellik kapalı.',
      jasmine.any(String),
      jasmine.any(Object)
    );
  });

  it('activationRequired_Get_ReloadsWithoutSnackbar', () => {
    reload.and.returnValue(of(null));
    fail403('/api/exam/leaderboard', 'ParentActivationRequired');

    expect(reload).toHaveBeenCalled();
    expect(snackBar.open).not.toHaveBeenCalled();
  });

  it('other403_Ignored', () => {
    fail403('/api/exam/x', 'TeacherNotApproved');
    expect(reload).not.toHaveBeenCalled();
    expect(TestBed.inject(ParentRequirementHandler)).toBeTruthy();
  });
});

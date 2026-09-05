import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { NEVER, of } from 'rxjs';

import { CallbackComponent } from './callback.component';
import { AuthService } from '../../services/auth.service';
import { MatSnackBar } from '@angular/material/snack-bar';

/**
 * BLOCKED — see PR discussion for issue #28.
 *
 * The behavior to verify is the `hasAppRole` line in `ngOnInit()`:
 *   const hasAppRole = ['Student', 'Teacher', 'Parent', 'Admin'].some(...)
 * ...and the resulting `dest` it feeds into `window.location.href = ...`.
 *
 * That assignment is a *direct* `window.location.href` write, not
 * `Router.navigate`. Empirically (verified against this project's actual
 * Karma + ChromeHeadless setup, not assumed):
 *
 *   1. `spyOnProperty(window, 'location', 'get'/'set')` and
 *      `spyOnProperty(window.location, 'href', 'set')` both throw
 *      `TypeError: ... is not declared configurable` — real Chrome exposes
 *      `Location` members as [LegacyUnforgeable]; they cannot be spied on.
 *   2. `Object.defineProperty(window, 'location', { configurable: true, ... })`
 *      throws `TypeError: Cannot redefine property: location` for the same
 *      reason.
 *   3. Letting the real assignment execute (no mocking at all) causes Karma
 *      to abort the affected spec with "Some of your tests did a full page
 *      reload!" — confirmed by running the suite. Every `it()` that lets the
 *      component's success flow run to completion hits this, whether or not
 *      the assertion itself is about `window.location`.
 *
 * So there is currently NO way to unit-test what URL the component redirects
 * to (or even to make any assertion *after* that point) without a
 * testability seam in the component (e.g. injecting a small
 * `WindowLocationService`/`DOCUMENT`-based wrapper instead of touching the
 * global `window` directly, or extracting the `hasAppRole`/`dest`
 * computation into a pure, independently-testable method). That is a
 * production-code change and out of scope for this test-only task, so it is
 * not applied here — flagging as a recommended follow-up instead of writing
 * a fragile/unsafe workaround (e.g. allowing real navigation) to force these
 * tests green.
 *
 * The tests below therefore only cover what IS observable without reaching
 * the redirect line: that ngOnInit reads `code`/`state` from the query
 * params and hands the code off to `exchangeCodeForToken`. The four
 * acceptance-criteria scenarios from issue #28 are listed as explicitly
 * skipped (`xit`) with the reasoning above, rather than silently omitted.
 */
describe('CallbackComponent', () => {
  let routerSpy: jasmine.SpyObj<Router>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let snackBarSpy: jasmine.SpyObj<MatSnackBar>;

  function createComponent(queryParams: Record<string, string>): ComponentFixture<CallbackComponent> {
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    snackBarSpy = jasmine.createSpyObj('MatSnackBar', ['open']);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['exchangeCodeForToken']);
    // Deliberately never emits, so the success callback (which ends in the
    // real `window.location.href` write) never runs during these tests.
    authServiceSpy.exchangeCodeForToken.and.returnValue(NEVER);

    TestBed.configureTestingModule({
      imports: [CallbackComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { queryParams: of(queryParams) } },
        { provide: Router, useValue: routerSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: MatSnackBar, useValue: snackBarSpy },
      ],
    }).compileComponents();

    return TestBed.createComponent(CallbackComponent);
  }

  it('should create', () => {
    const fixture = createComponent({});
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('ngOnInit_NoCodeParam_NavigatesToLoginWithoutCallingAuthService', () => {
    const fixture = createComponent({});
    fixture.detectChanges();

    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
  });

  it(
    'ngOnInit_CodeParamPresent_ExchangesCodeForToken',
    fakeAsync(() => {
      const fixture = createComponent({ code: 'auth-code', state: '' });
      fixture.detectChanges();
      tick(150); // flush only the initial delay(100) before exchangeCodeForToken() is called.

      expect(authServiceSpy.exchangeCodeForToken).toHaveBeenCalledWith('auth-code');
      // exchangeCodeForToken() returns NEVER above, so no further timers are
      // pending here — nothing left to flush, nothing can reach the redirect line.
    })
  );

  // --- Issue #28 acceptance criteria: blocked, see file header comment. ---
  xit('ngOnInit_AdminRoleOnly_RedirectsToDashboardNotCompleteProfile', () => {});
  xit('ngOnInit_AdminAndTeacherRoles_RedirectsToDashboard', () => {});
  xit('ngOnInit_NoAppRoles_RedirectsToCompleteProfile', () => {});
  xit('ngOnInit_NoAppRolesWithRoleIntent_RedirectsToCompleteProfileWithRoleQueryParam', () => {});
});

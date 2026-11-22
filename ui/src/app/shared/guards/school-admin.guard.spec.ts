import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, isObservable, of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { MatSnackBar } from '@angular/material/snack-bar';
import { translocoTestingModule } from '../testing/transloco-testing';
import { schoolAdminGuard } from './school-admin.guard';

/** Issue #1277: okul yöneticisi sayfa kapısı. */
describe('schoolAdminGuard', () => {
  function run(initially: boolean, afterRefresh: boolean): Promise<boolean | UrlTree> {
    const isSchoolAdmin = signal(initially);
    TestBed.configureTestingModule({
      imports: [translocoTestingModule()],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            isSchoolAdmin,
            refreshProfile: () => {
              isSchoolAdmin.set(afterRefresh);
              return of(null);
            },
          },
        },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    });
    const result = TestBed.runInInjectionContext(() => schoolAdminGuard({} as never, {} as never)) as
      | boolean
      | UrlTree
      | Observable<boolean | UrlTree>;
    return isObservable(result) ? firstValueFrom(result) : Promise.resolve(result);
  }

  it('lets a school admin in', async () => {
    expect(await run(true, true)).toBe(true);
  });

  it('refreshes the profile once and lets a newly granted admin in', async () => {
    expect(await run(false, true)).toBe(true);
  });

  it('redirects a teacher who is not a school admin', async () => {
    const result = await run(false, false);
    expect(result instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).not.toContain('school-admin');
  });
});

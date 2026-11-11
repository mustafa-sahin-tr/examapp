import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRouteSnapshot, Router, UrlTree, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

import { AuthService } from '../../services/auth.service';
import { labelingGuard } from './labeling.guard';

/** Issue #1060: öğretmen işaretlemeye yalnız hızlı ekleme düzeltmesi için girer. */
describe('labelingGuard', () => {
  const snackBar = { open: vi.fn() };

  function run(roles: string[], query: Record<string, string> = {}): unknown {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { hasRealmRole: (role: string) => roles.includes(role) } },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
      ],
    });
    const route = { queryParamMap: convertToParamMap(query) } as ActivatedRouteSnapshot;
    return TestBed.runInInjectionContext(() => labelingGuard(route, {} as never));
  }

  const denied = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree) === '/dashboard';

  it('lets the content team in without a fix target', () => {
    expect(run(['Admin'])).toBe(true);
    TestBed.resetTestingModule();
    expect(run(['QuestionTrainer'])).toBe(true);
  });

  it('lets a teacher in only to fix a quick add photo', () => {
    expect(run(['Teacher'], { bookImport: 'job-1', test: '3', page: '1', kind: 'QuickAdd' })).toBe(true);
  });

  it('keeps a teacher out of folder labeling and book import fixes', () => {
    expect(denied(run(['Teacher']))).toBe(true);
    TestBed.resetTestingModule();
    expect(denied(run(['Teacher'], { bookImport: 'job-1', test: '3', page: '1' }))).toBe(true);
    TestBed.resetTestingModule();
    expect(denied(run(['Student'], { bookImport: 'job-1', kind: 'QuickAdd' }))).toBe(true);
  });
});

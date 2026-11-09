import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, of } from 'rxjs';

import { entitlementGuard } from './entitlement.guard';
import { EntitlementStateService } from '../../services/entitlement-state.service';
import { EntitlementLimitHandler } from '../interceptors/entitlement-limit.interceptor';

/** Issue #1087: planda olmayan özelliğin sayfası açılmaz, yükseltme penceresi açılır. */
describe('entitlementGuard (issue #1087)', () => {
  let has: ReturnType<typeof vi.fn>;
  let showNotInPlan: ReturnType<typeof vi.fn>;
  let router: Router;

  beforeEach(() => {
    has = vi.fn();
    showNotInPlan = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: EntitlementStateService,
          useValue: { load: () => of(null), has, plan: () => ({ planKey: 'teacher-basic' }) },
        },
        { provide: EntitlementLimitHandler, useValue: { showNotInPlan } },
      ],
    });
    router = TestBed.inject(Router);
  });

  const run = (feature: string) =>
    firstValueFrom(
      TestBed.runInInjectionContext(() => entitlementGuard(feature)({} as never, {} as never)) as Observable<boolean | UrlTree>,
    );

  it('lets the feature through when it is in the plan', async () => {
    has.mockReturnValue(true);

    expect(await run('quick-add')).toBe(true);
    expect(has).toHaveBeenCalledWith('quick-add');
    expect(showNotInPlan).not.toHaveBeenCalled();
  });

  it('shows the not-in-plan dialog and sends a direct visit to the dashboard', async () => {
    has.mockReturnValue(false);

    const result = await run('question-generation');

    expect(showNotInPlan).toHaveBeenCalledWith('question-generation', 'teacher-basic');
    expect(router.serializeUrl(result as UrlTree)).toBe('/dashboard');
  });

  it('keeps an in-app visitor on the current page', async () => {
    has.mockReturnValue(false);
    vi.spyOn(router, 'navigated', 'get').mockReturnValue(true);

    expect(await run('question-generation')).toBe(false);
  });
});

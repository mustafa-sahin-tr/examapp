import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, convertToParamMap } from '@angular/router';
import { Observable, firstValueFrom, of } from 'rxjs';

import { worksheetListResolver } from './worksheet-resolver';
import { TestService } from '../../services/test.service';
import { AuthService } from '../../services/auth.service';
import { Paged, Test } from '../../models/test-instance';

describe('worksheetListResolver (#573)', () => {
  const listWorksheets = vi.fn().mockReturnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 12 }));

  function resolve(role: string, query: Record<string, string>): Promise<Paged<Test>> {
    TestBed.configureTestingModule({
      providers: [
        { provide: TestService, useValue: { listWorksheets } },
        { provide: AuthService, useValue: { hasRole: (r: string) => r === role } },
      ],
    });
    const route = { queryParamMap: convertToParamMap(query) } as ActivatedRouteSnapshot;
    const result = TestBed.runInInjectionContext(() =>
      worksheetListResolver(route, {} as RouterStateSnapshot)
    ) as Observable<Paged<Test>>;
    return firstValueFrom(result);
  }

  beforeEach(() => listWorksheets.mockClear());

  it('studentOnActivitiesTab_SkipsPagedListFetch', async () => {
    const result = await resolve('Student', { type: 'activities' });

    expect(listWorksheets).not.toHaveBeenCalled();
    expect(result.items).toEqual([]);
  });

  it('teacherOnActivitiesTab_FetchesKindActivity', async () => {
    await resolve('Teacher', { type: 'activities' });

    expect(listWorksheets).toHaveBeenCalledWith(expect.objectContaining({ kind: 1 }));
  });

  it('testsTab_FetchesKindTest', async () => {
    await resolve('Student', {});

    expect(listWorksheets).toHaveBeenCalledWith(expect.objectContaining({ kind: 0 }));
  });
});

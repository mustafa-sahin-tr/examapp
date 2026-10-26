import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, ResolveFn } from '@angular/router';
import { of } from 'rxjs';
import { TestService } from '../../services/test.service';
import { AuthService } from '../../services/auth.service';
import { Paged, Test } from '../../models/test-instance';

export const worksheetListResolver: ResolveFn<Paged<Test>> = (route: ActivatedRouteSnapshot) => {
  const testService = inject(TestService);
  const search = route.queryParamMap.get('search') ?? undefined;
  // Issue #573: liste sayfası Testler | Etkinlikler sekmeli; ilk sayfa seçili türle (`?type=activities`) gelir.
  const activities = route.queryParamMap.get('type') === 'activities';
  // S8: öğrencinin Etkinlikler sekmesi kendi listesini (ActivityListComponent) yükler; sayfalı liste kullanılmaz.
  if (activities && inject(AuthService).hasRole('Student')) {
    return of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 12 });
  }
  const kind = activities ? 1 : 0;
  return testService.listWorksheets({ search, pageNumber: 1, pageSize: 12, sortBy: 'newest', kind });
};

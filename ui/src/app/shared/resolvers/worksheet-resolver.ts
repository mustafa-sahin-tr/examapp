import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, ResolveFn } from '@angular/router';
import { TestService } from '../../services/test.service';
import { Paged, Test } from '../../models/test-instance';

export const worksheetListResolver: ResolveFn<Paged<Test>> = (route: ActivatedRouteSnapshot) => {
  const testService = inject(TestService);
  const search = route.queryParamMap.get('search') ?? undefined;
  return testService.listWorksheets({ search, pageNumber: 1, pageSize: 12, sortBy: 'newest' });
};

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { hasSchoolListFilterParams } from '../../pages/admin/school-manager/school-list-filter';

/**
 * Issue #897: Yönetim sayfasında artık Okullar sekmesi yok. Okul filtresi taşıyan eski `/admin?q=…` bağlantıları (#281)
 * aynı filtreyle `/admin/schools`'a gider; filtresiz `/admin` olduğu gibi açılır.
 */
export const adminSchoolFilterRedirectGuard: CanActivateFn = (route) =>
  hasSchoolListFilterParams(route.queryParamMap)
    ? inject(Router).createUrlTree(['/admin/schools'], { queryParams: route.queryParams })
    : true;

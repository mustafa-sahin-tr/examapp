import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { ENTITLEMENT_NOT_IN_PLAN_CODE } from '../../models/subscription';
import { EntitlementStateService } from '../../services/entitlement-state.service';
import { EntitlementLimitHandler } from '../interceptors/entitlement-limit.interceptor';

/**
 * Issue #1087: özellik planda yoksa sayfa açılmaz, yükseltme penceresi açılır (sunucunun 403 `entitlement.notInPlan`
 * yanıtıyla aynı pencere). Uygulama içi gezintide kişi bulunduğu sayfada kalır; doğrudan adresle gelen panoya düşer.
 * authGuard ve roleGuard'dan SONRA kullanılır.
 */
export const entitlementGuard =
  (feature: string): CanActivateFn =>
  () => {
    const state = inject(EntitlementStateService);
    const handler = inject(EntitlementLimitHandler);
    const router = inject(Router);

    return state.load().pipe(
      map(() => {
        if (state.has(feature)) return true;
        handler.show({
          code: ENTITLEMENT_NOT_IN_PLAN_CODE,
          feature,
          limit: 0,
          used: null,
          resetsAtUtc: null,
          planKey: state.plan()?.planKey ?? null,
        });
        return router.navigated ? false : router.createUrlTree(['/dashboard']);
      }),
    );
  };

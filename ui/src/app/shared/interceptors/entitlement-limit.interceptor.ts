import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { catchError, throwError } from 'rxjs';

import {
  ENTITLEMENT_NOT_IN_PLAN_CODE,
  EntitlementLimitReached,
  entitlementCounterpartMessageOf,
  entitlementLimitOf,
  isSubscriptionRequired,
} from '../../models/subscription';
import { EntitlementStateService } from '../../services/entitlement-state.service';
import { UpgradeDialogComponent } from '../components/upgrade-dialog/upgrade-dialog.component';
import { isCrossOriginUrl } from '../utils/request-url.util';

/** Issue #1029: aynı anda tek yükseltme penceresi (art arda gelen sınır yanıtları pencereyi çoğaltmaz). */
@Injectable({ providedIn: 'root' })
export class EntitlementLimitHandler {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly router = inject(Router);
  private readonly entitlements = inject(EntitlementStateService);
  private open: MatDialogRef<UpgradeDialogComponent> | null = null;

  show(limit: EntitlementLimitReached): void {
    if (this.open) return;
    this.open = this.dialog.open(UpgradeDialogComponent, { data: limit, width: '480px', maxWidth: '95vw' });
    this.open.afterClosed().subscribe(() => (this.open = null));
  }

  /** Issue #1087: arayüzün kendi denetimi (rota, düğme) — sunucunun 403 `entitlement.notInPlan` penceresinin aynısı. */
  showNotInPlan(feature: string, planKey: string | null): void {
    this.show({ code: ENTITLEMENT_NOT_IN_PLAN_CODE, feature, limit: 0, used: null, resetsAtUtc: null, planKey });
  }

  /** Issue #1088: karşı tarafın planı izin vermiyor; çağıran yükseltemez, yalnız bilgilendirilir. */
  showCounterpart(message: string): void {
    this.snackBar.open(message, this.transloco.translate<string>('common.close'), { duration: 8000 });
  }

  /**
   * Issue #1090: deneme bitti, plan yok. Plan durumu yenilenir (plan sayfası açıklamayı gösterir) ve kullanıcı plan sayfasına
   * gider; plan / abonelik sayfasındaysa yerinde kalır (bu sayfaların kendi istekleri açıktır).
   */
  showLocked(): void {
    if (!this.entitlements.plan()?.locked) this.entitlements.load(true).subscribe();
    const path = this.router.url.split('?')[0];
    if (path === '/plans' || path === '/subscription') return;
    void this.router.navigateByUrl('/plans');
  }
}

/**
 * Issue #1029: 429 `entitlement.limitReached` (her sınırlı uç noktanın ortak yanıtı) → yükseltme penceresi. Hata çağırana
 * iletilir; sayfa kendi satır içi mesajını göstermeye devam edebilir. #1088: 403 `entitlement.counterpartNotInPlan` → bildirim.
 * #1090: 402 `subscription.required` → plan sayfası.
 */
export const entitlementLimitInterceptor: HttpInterceptorFn = (req, next) => {
  const handler = inject(EntitlementLimitHandler);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (isCrossOriginUrl(req.url)) return throwError(() => error);
      if (isSubscriptionRequired(error)) {
        handler.showLocked();
        return throwError(() => error);
      }
      const limit = entitlementLimitOf(error);
      if (limit) handler.show(limit);
      const counterpart = limit ? null : entitlementCounterpartMessageOf(error);
      if (counterpart) handler.showCounterpart(counterpart);
      return throwError(() => error);
    })
  );
};

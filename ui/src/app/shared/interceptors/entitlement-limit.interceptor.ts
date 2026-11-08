import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { catchError, throwError } from 'rxjs';

import { EntitlementLimitReached, entitlementLimitOf } from '../../models/subscription';
import { UpgradeDialogComponent } from '../components/upgrade-dialog/upgrade-dialog.component';
import { isCrossOriginUrl } from '../utils/request-url.util';

/** Issue #1029: aynı anda tek yükseltme penceresi (art arda gelen sınır yanıtları pencereyi çoğaltmaz). */
@Injectable({ providedIn: 'root' })
export class EntitlementLimitHandler {
  private readonly dialog = inject(MatDialog);
  private open: MatDialogRef<UpgradeDialogComponent> | null = null;

  show(limit: EntitlementLimitReached): void {
    if (this.open) return;
    this.open = this.dialog.open(UpgradeDialogComponent, { data: limit, width: '480px', maxWidth: '95vw' });
    this.open.afterClosed().subscribe(() => (this.open = null));
  }
}

/**
 * Issue #1029: 429 `entitlement.limitReached` (her sınırlı uç noktanın ortak yanıtı) → yükseltme penceresi. Hata çağırana
 * iletilir; sayfa kendi satır içi mesajını göstermeye devam edebilir.
 */
export const entitlementLimitInterceptor: HttpInterceptorFn = (req, next) => {
  const handler = inject(EntitlementLimitHandler);

  return next(req).pipe(
    catchError((error: unknown) => {
      const limit = isCrossOriginUrl(req.url) ? null : entitlementLimitOf(error);
      if (limit) handler.show(limit);
      return throwError(() => error);
    })
  );
};

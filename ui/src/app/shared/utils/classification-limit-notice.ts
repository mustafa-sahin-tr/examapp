import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

import { PLANS_URL } from '../components/upgrade-dialog/upgrade-dialog.component';

/**
 * Issue #1031: plan sınırı dolunca soru yine kaydedilir ama sınıflandırılmaz (429 değil, yanıtta `classificationSkipped`). Kayıt
 * başarılı olduğu için yükseltme penceresi açılmaz; teşekkür bildiriminin yerine bu not çıkar, "Planı yükselt" plan seçimine gider.
 */
@Injectable({ providedIn: 'root' })
export class ClassificationLimitNotice {
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly router = inject(Router);

  showIfSkipped(response: { classificationSkipped?: number } | null | undefined): void {
    const count = response?.classificationSkipped ?? 0;
    if (count <= 0) return;
    this.snackBar
      .open(this.transloco.translate<string>('classificationLimit.note', { count }), this.transloco.translate<string>('classificationLimit.upgrade'), { duration: 10000 })
      .onAction()
      .subscribe(() => void this.router.navigateByUrl(PLANS_URL));
  }
}

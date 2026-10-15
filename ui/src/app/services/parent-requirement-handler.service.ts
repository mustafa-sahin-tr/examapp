import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { PARENT_REQUIRED_URL } from '../models/parent-status.model';
import { ParentStatusStore } from './parent-status.store';
import { StudentParentService } from './student-parent.service';

/**
 * Issue #440 / #441: sunucunun veli kuralı 403'lerine tepki (interceptor'dan çağrılır). Aynı anda gelen 403'ler tek durum
 * isteğiyle işlenir; "veli bilgisi ekle" ekranı veli uçları dışında bir şey çağırmadığından döngü oluşmaz.
 */
@Injectable({ providedIn: 'root' })
export class ParentRequirementHandler {
  private readonly studentParent = inject(StudentParentService);
  private readonly store = inject(ParentStatusStore);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  private handlingRequired = false;

  /** 403 `ParentRequired`: durumu tazele; süre dolduysa (`Expired`) "veli bilgisi ekle" ekranına git. */
  handleParentRequired(): void {
    if (this.handlingRequired) {
      return;
    }
    this.handlingRequired = true;
    this.studentParent
      .reload()
      .pipe(finalize(() => (this.handlingRequired = false)))
      .subscribe({
        next: () => {
          const currentPath = this.router.url.split(/[?#]/)[0];
          if (this.store.expired() && currentPath !== PARENT_REQUIRED_URL) {
            void this.router.navigateByUrl(PARENT_REQUIRED_URL);
          }
        },
        error: () => undefined,
      });
  }

  /** 403 `ParentActivationRequired`: kısıtlı mod arayüzü için durumu tazele; kullanıcı eylemiyse bilgilendir. */
  handleActivationRequired(notify: boolean): void {
    this.studentParent.reload().subscribe({ error: () => undefined });
    if (notify) {
      this.snackBar.open(
        this.transloco.translate<string>('parentRequirement.restricted.snackbar'),
        this.transloco.translate<string>('common.close'),
        { duration: 5000 }
      );
    }
  }
}

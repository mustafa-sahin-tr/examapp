import { Injectable, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

import { activityErrorMessage, activityRoute } from '../models/activity';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../shared/components/confirm-dialog/confirm-dialog.component';
import { ActivityService } from './activity.service';
import { TestService } from './test.service';

/**
 * Issue #573: bir etkinliği açar — mevcut `start-test` (devam eden oturum varsa aynısı döner) ve `/activity/:instanceId`.
 * Sunucu yeni oturum açmazsa (`success:false`, ör. zaten bitmiş) mesaj gösterilir ve sayfa detayına gidilir.
 * Issue #609: tamamlanmış etkinliği "Yeniden çöz" — onay, `retake` (aynı oturum sıfırlanır) ve oynatıcı.
 */
@Injectable({ providedIn: 'root' })
export class ActivityLauncherService {
  private readonly testService = inject(TestService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly activityService = inject(ActivityService);
  private readonly dialog = inject(MatDialog);

  /** Başlatılmakta olan worksheet id'si (çift tıklamayı engeller). */
  readonly launching = signal<number | null>(null);
  /** Issue #609: yeniden çözme isteği uçuşta olan oturum id'si (onay açıkken de dolu; çift tıklamayı engeller). */
  readonly retaking = signal<number | null>(null);

  open(worksheetId: number | null | undefined): void {
    if (!worksheetId || this.launching() !== null) return;
    this.launching.set(worksheetId);
    this.testService.startTest(worksheetId).subscribe({
      next: (response) => {
        this.launching.set(null);
        if (response?.success && response.instanceId) {
          void this.router.navigate(activityRoute(response.instanceId), { state: { worksheetId } });
          return;
        }
        this.notify(response?.message || this.tr('failed'));
        void this.router.navigate(['/test', worksheetId]);
      },
      error: (error: unknown) => {
        this.launching.set(null);
        this.notify(activityErrorMessage(error) ?? this.tr('failed'));
      },
    });
  }

  /**
   * Issue #609: "Cevapların silinecek" onayından sonra `POST test-instances/{id}/retake`; başarılıysa oynatıcı açılır.
   * `alreadyRetaking` (zaten yeniden çözülüyordu, hiçbir şey silinmedi) da aynı yolla açılır. Hata → sunucu mesajı (snackbar).
   * `reopen` verilirse (oynatıcı zaten aynı rotada) gezinme yerine o çağrılır — aynı URL'ye gezinme yok sayılır.
   */
  retake(instanceId: number | null | undefined, worksheetId: number | null | undefined, reopen?: () => void): void {
    if (!instanceId || this.retaking() !== null) return;
    this.retaking.set(instanceId);
    const data: ConfirmDialogData = {
      title: this.tr('retakeTitle', 'retake'),
      message: this.tr('retakeMessage', 'retake'),
      confirmText: this.tr('retakeConfirm', 'retake'),
      cancelText: this.tr('retakeCancel', 'retake'),
      icon: 'replay',
      confirmColor: 'primary',
    };
    this.dialog
      .open<ConfirmDialogComponent, ConfirmDialogData, boolean>(ConfirmDialogComponent, { width: '480px', maxWidth: '90vw', data })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) {
          this.retaking.set(null);
          return;
        }
        this.activityService.retake(instanceId).subscribe({
          next: (response) => {
            this.retaking.set(null);
            if (!response?.success) {
              this.notify(response?.message || this.tr('retakeFailed', 'retake'));
              return;
            }
            const target = response.instanceId || instanceId;
            if (reopen && target === instanceId) {
              reopen();
              return;
            }
            void this.router.navigate(activityRoute(target), worksheetId ? { state: { worksheetId } } : undefined);
          },
          // 403 AccessDenied, 404 notFound / WorksheetNotFound, 409, 429 RateLimited: sunucu mesajı; yoksa yerel yedek.
          error: (error: unknown) => {
            this.retaking.set(null);
            const rateLimited = error instanceof HttpErrorResponse && error.status === 429;
            this.notify(activityErrorMessage(error) ?? this.tr(rateLimited ? 'retakeRateLimited' : 'retakeFailed', 'retake'));
          },
        });
      });
  }

  private notify(message: string): void {
    this.snackBar.open(message, this.tr('dismiss'), { duration: 4000 });
  }

  private tr(key: string, group: 'launch' | 'retake' = 'launch'): string {
    return this.transloco.translate<string>(`shared.activity.${group}.${key}`) ?? '';
  }
}

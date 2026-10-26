import { Injectable, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

import { activityErrorMessage, activityRoute } from '../models/activity';
import { TestService } from './test.service';

/**
 * Issue #573: bir etkinliği açar — mevcut `start-test` (devam eden oturum varsa aynısı döner) ve `/activity/:instanceId`.
 * Sunucu yeni oturum açmazsa (`success:false`, ör. zaten bitmiş) mesaj gösterilir ve sayfa detayına gidilir.
 */
@Injectable({ providedIn: 'root' })
export class ActivityLauncherService {
  private readonly testService = inject(TestService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  /** Başlatılmakta olan worksheet id'si (çift tıklamayı engeller). */
  readonly launching = signal<number | null>(null);

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

  private notify(message: string): void {
    this.snackBar.open(message, this.tr('dismiss'), { duration: 4000 });
  }

  private tr(key: string): string {
    return this.transloco.translate<string>(`shared.activity.launch.${key}`) ?? '';
  }
}

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_SNACK_BAR_DATA, MatSnackBarRef } from '@angular/material/snack-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { BadgeEarnedPushPayload } from '../../../models/notification.model';
import { BadgeMedallionComponent } from '../badge-medallion/badge-medallion.component';

/** Toast'taki "Rozetlerim" eyleminin hedefi (BadgeThropyComponent). */
export const BADGE_TOAST_ROUTE = '/certificates';
/** Eylem içerdiği için varsayılan 4 sn kısa (tasarım kararı 5). */
export const BADGE_TOAST_DURATION_MS = 6000;
export const BADGE_TOAST_PANEL_CLASS = 'badge-toast';
/** CR U6: duyuruyu snackbar'ın kendi canlı bölgesi yapar; içerikte ayrıca `role="status"` yok (çift okuma olmasın). */
export const BADGE_TOAST_POLITENESS = 'polite' as const;

/**
 * Issue #149 — rozet kazanım toast'u (`MatSnackBar.openFromComponent`). Emoji yok; "Yeni" medalyon + ad +
 * en fazla 2 satır açıklama + "Rozetlerim" eylemi + kapat. Duyuru `politeness: 'polite'` ile.
 */
@Component({
  selector: 'app-badge-earned-toast',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, BadgeMedallionComponent],
  templateUrl: './badge-earned-toast.component.html',
  styleUrls: ['./badge-earned-toast.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BadgeEarnedToastComponent {
  readonly data = inject<BadgeEarnedPushPayload>(MAT_SNACK_BAR_DATA);
  private readonly snackBarRef = inject<MatSnackBarRef<BadgeEarnedToastComponent>>(MatSnackBarRef);
  private readonly router = inject(Router);

  openBadges(): void {
    this.snackBarRef.dismissWithAction();
    void this.router.navigateByUrl(BADGE_TOAST_ROUTE);
  }

  dismiss(): void {
    this.snackBarRef.dismiss();
  }
}

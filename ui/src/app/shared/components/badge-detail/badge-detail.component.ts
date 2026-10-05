import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LocaleService } from '../../../services/locale.service';
import { BadgeMedallionComponent } from '../badge-medallion/badge-medallion.component';
import { BadgeTranslate, badgeStateText, isEarnedState } from '../badge-medallion/badge-state.util';
import { BadgeThropyItem } from '../badge-thropy/badge-thropy.types';

/**
 * Issue #149 — seçili rozetin detay paneli (Rozetlerim: yol düğümü ve tekil rozet kartı ortak).
 * Medalyon 72 + ad + yol/adım çipleri + açıklama + `role="progressbar"` çubuk + "Kalan n · %p".
 */
@Component({
  selector: 'app-badge-detail',
  standalone: true,
  imports: [BadgeMedallionComponent, TranslocoDirective],
  templateUrl: './badge-detail.component.html',
  styleUrls: ['./badge-detail.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BadgeDetailComponent {
  private readonly localeService = inject(LocaleService);

  readonly badge = input.required<BadgeThropyItem>();
  /** Mobilde seçili satırın yerinde açılımı: medalyonsuz, çerçevesiz (medalyon zaten satırda). */
  readonly compact = input(false);

  isEarned(badge: BadgeThropyItem): boolean {
    return isEarnedState(badge.state);
  }

  stateText(t: BadgeTranslate, badge: BadgeThropyItem): string {
    return badgeStateText(t, {
      state: badge.state,
      current: badge.completedLabel,
      target: badge.totalLabel,
      earnedDateUtc: badge.earnedDateUtc,
      locale: this.localeService.localeDefinition().angularLocale,
    });
  }
}

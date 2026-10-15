import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';

import { ParentStatusStore } from '../../../services/parent-status.store';

/** Kısıtlı mod bilgisinin gösterildiği özellik → `parentRequirement.restricted.<feature>` metni. */
export type ParentRestrictedFeature = 'booking' | 'messages' | 'comments' | 'leaderboard';

/**
 * Issue #440: veli daveti aktive edilene kadar kapalı özelliklerin (özel ders talebi, direkt mesaj, yorum, liderlik tablosu)
 * yanında gösterilen bilgi bandı. Yalnız öğrencinin durumu `AwaitingActivation` iken render edilir; diğer herkes için boştur.
 * Eylemi pasifleştirmek barındıran komponentin işidir (`ParentStatusStore.activationRestricted`).
 */
@Component({
  selector: 'app-parent-activation-notice',
  standalone: true,
  imports: [MatIconModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (restricted()) {
      <p class="pan" role="status" data-test="parent-activation-notice">
        <mat-icon class="pan__icon" aria-hidden="true">family_restroom</mat-icon>
        <span>{{ 'parentRequirement.restricted.' + feature() | transloco }}</span>
      </p>
    }
  `,
  styleUrl: './parent-activation-notice.component.scss',
})
export class ParentActivationNoticeComponent {
  readonly feature = input.required<ParentRestrictedFeature>();
  protected readonly restricted = inject(ParentStatusStore).activationRestricted;
}

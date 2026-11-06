import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';

import { ParentStatusCardComponent } from '../parent-status-card/parent-status-card.component';

/** Kilit ekranının açıldığı sayfa → `parentRequirement.locked.<feature>` metni. */
export type ParentLockedFeature = 'messages' | 'tutors';

/**
 * Issue #845: veli daveti aktive edilene kadar kapalı bir sayfanın (Mesajlar, Özel Ders) yerine gösterilir — sayfa sessizce
 * boş kalmaz, nedenini söyler. Yeniden gönder / e-postayı düzelt ve kalan süre ana sayfadaki veli kartından gelir (aynı mantık
 * iki yerde yazılmasın). Barındıran sayfa yalnız `ParentStatusStore.activationRestricted` iken render eder.
 */
@Component({
  selector: 'app-parent-locked-page',
  standalone: true,
  imports: [MatIconModule, TranslocoPipe, ParentStatusCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="plp" aria-labelledby="plp-title" data-test="parent-locked-page">
      <span class="plp__ring" aria-hidden="true"><mat-icon>lock</mat-icon></span>
      <h2 id="plp-title" class="plp__title">{{ 'parentRequirement.locked.title' | transloco }}</h2>
      <p class="plp__body">{{ 'parentRequirement.locked.' + feature() | transloco }}</p>
      <p class="plp__body">{{ 'parentRequirement.locked.meanwhile' | transloco }}</p>
      <app-parent-status-card class="plp__card" />
    </section>
  `,
  styleUrl: './parent-locked-page.component.scss',
})
export class ParentLockedPageComponent {
  readonly feature = input.required<ParentLockedFeature>();
}

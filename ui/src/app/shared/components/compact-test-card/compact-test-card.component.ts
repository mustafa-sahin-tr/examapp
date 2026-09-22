import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoService } from '@jsverse/transloco';

import { LocaleService } from '../../../services/locale.service';

/**
 * Kompakt test kartı (issue #188): 40×40 thumb + tek satır başlık + opsiyonel bitiş tarihi
 * + opsiyonel ilerleme rozeti. ~220px yatay; tüm kart tıklanabilir.
 *
 * Dashboard "Atanmış Testler" bölümündeki gömülü `.mini-test-card` buradan türetildi; görsel
 * kurallar birebir taşındı. Host elemanının kendisi `role="button"` olduğu için
 * yerleşim (flex-basis, mobil genişlik) üst komponent tarafından element seçicisiyle ezilebilir.
 */
@Component({
  selector: 'app-compact-test-card',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './compact-test-card.component.html',
  styleUrl: './compact-test-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    role: 'button',
    tabindex: '0',
    '[attr.aria-label]': 'resolvedAriaLabel()',
    '(click)': 'activated.emit()',
    '(keydown.enter)': 'activated.emit()',
    '(keydown.space)': 'onSpace($event)',
  },
})
export class CompactTestCardComponent {
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);

  readonly title = input.required<string>();
  /** null/boş → placeholder ikon. */
  readonly thumbUrl = input<string | null | undefined>(null);
  /** ISO string veya Date; null → bitiş etiketi render edilmez. */
  readonly dueDate = input<Date | string | null | undefined>(null);
  /** 0-100; null → ilerleme rozeti render edilmez. */
  readonly progressPercent = input<number | null | undefined>(null);
  /** Verilmezse başlık + bitiş + ilerleme metninden türetilir. */
  readonly ariaLabel = input<string | null | undefined>(null);

  /** Tıklama, Enter veya Space ile tetiklenir. */
  readonly activated = output<void>();

  readonly thumbStyle = computed(() => {
    const url = this.thumbUrl();
    return url ? `url("${url}")` : null;
  });

  /** Dashboard ile aynı biçim: `toLocaleDateString(aktif dil)` → "Bitiş: 12.03.2026". */
  readonly dueLabel = computed(() => {
    const raw = this.dueDate();
    if (!raw) {
      return null;
    }
    const date = raw instanceof Date ? raw : new Date(raw);
    if (Number.isNaN(date.getTime())) {
      return null;
    }
    // localeDefinition() okunması dil değişiminde etiketin yeniden hesaplanmasını sağlar.
    const locale = this.localeService.localeDefinition().angularLocale;
    return this.transloco.translate<string>('shared.compactTestCard.due', {
      date: date.toLocaleDateString(locale),
    });
  });

  readonly progressLabel = computed(() => {
    const percent = this.progressPercent();
    if (percent == null || Number.isNaN(percent)) {
      return null;
    }
    // Dil değişiminde yeniden hesaplansın diye locale tanımı okunur.
    this.localeService.localeDefinition();
    const clamped = Math.min(100, Math.max(0, Math.round(percent)));
    return this.transloco.translate<string>('shared.compactTestCard.progress', { percent: clamped });
  });

  readonly resolvedAriaLabel = computed(() => {
    const explicit = this.ariaLabel();
    if (explicit) {
      return explicit;
    }
    return [this.title(), this.dueLabel(), this.progressLabel()].filter((part) => !!part).join(', ');
  });

  onSpace(event: Event): void {
    // Space'in sayfayı kaydırmasını engelle; davranış dashboard'daki gömülü kartla aynı.
    event.preventDefault();
    this.activated.emit();
  }
}

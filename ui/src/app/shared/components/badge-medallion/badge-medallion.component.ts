import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { BADGE_FALLBACK_ICON, BadgeMedallionState, isEarnedState, resolveBadgeIcon } from './badge-state.util';

/** Tasarım ölçeği (issue #149 karar 2); ara değerler en yakın adıma yuvarlanır. */
export const BADGE_MEDALLION_SIZES = [24, 32, 40, 44, 48, 56, 64, 72] as const;

/** İlerleme halkası yalnız bu boyuttan itibaren çizilir; altında yanındaki çubuk/metin taşır. */
export const BADGE_RING_MIN_SIZE = 56;
/** Bu boyut ve altında köşe glifi gizlenir. */
export const BADGE_CORNER_HIDDEN_MAX_SIZE = 32;

export function snapMedallionSize(value: number | string | null | undefined): number {
  const numeric = Number(value);
  if (value === null || value === undefined || value === '' || !Number.isFinite(numeric)) {
    return 56;
  }
  return BADGE_MEDALLION_SIZES.reduce<number>(
    (best, size) => (Math.abs(size - numeric) < Math.abs(best - numeric) ? size : best),
    BADGE_MEDALLION_SIZES[0]
  );
}

const CORNER_GLYPHS: Record<BadgeMedallionState, string | null> = {
  earned: 'check',
  new: 'auto_awesome',
  locked: 'lock',
  'in-progress': null,
};

/**
 * Issue #149 — tek rozet medalyonu (4 durum). Saf görsel: `aria-hidden`; erişilebilir ad üst öğede
 * (`badgeAriaLabel`). Renkler yalnız `--ms-*` token'larından.
 */
@Component({
  selector: 'app-badge-medallion',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './badge-medallion.component.html',
  styleUrls: ['./badge-medallion.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'badge-medallion',
    'aria-hidden': 'true',
    '[attr.data-state]': 'state()',
    '[class.badge-medallion--compact]': 'hideCorner()',
    '[style.--bm-size]': "size() + 'px'",
  },
})
export class BadgeMedallionComponent {
  /** Material Symbols adı (yeni alan); geçersizse yok sayılır. */
  readonly icon = input<string | null | undefined>(null);
  /** Eski SVG yolu (`achievements/<dosya>.svg`); geçiş süresince yedek. */
  readonly iconUrl = input<string | null | undefined>(null);
  readonly state = input<BadgeMedallionState>('earned');
  readonly size = input(56, { transform: snapMedallionSize });
  /** 0..100; yalnız `in-progress` durumunda ve ≥56px'te halka olarak çizilir. */
  readonly progressPercent = input<number | null | undefined>(null);

  /** Yüklenemeyen görsel yolu — varsayılan glife düşülür (kırık görsel gösterilmez). */
  private readonly failedSrc = signal<string | null>(null);

  readonly iconSource = computed(() => {
    const source = resolveBadgeIcon(this.icon(), this.iconUrl());
    if (source.kind === 'image' && source.src === this.failedSrc()) {
      return { kind: 'symbol' as const, name: BADGE_FALLBACK_ICON };
    }
    return source;
  });

  readonly filled = computed(() => isEarnedState(this.state()));
  readonly hideCorner = computed(() => this.size() <= BADGE_CORNER_HIDDEN_MAX_SIZE);
  readonly cornerGlyph = computed(() => (this.hideCorner() ? null : CORNER_GLYPHS[this.state()]));
  readonly showRing = computed(() => this.state() === 'in-progress' && this.size() >= BADGE_RING_MIN_SIZE);
  readonly ringPercent = computed(() => {
    const value = Number(this.progressPercent() ?? 0);
    return Number.isFinite(value) ? Math.max(0, Math.min(100, Math.round(value))) : 0;
  });

  onImageError(src: string): void {
    this.failedSrc.set(src);
  }
}

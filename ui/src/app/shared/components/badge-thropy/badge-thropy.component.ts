import {
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  computed,
  inject,
  signal,
  ChangeDetectionStrategy
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { BadgeProgressItem, BadgeService } from '../../../services/badge.service';
import { LocaleService } from '../../../services/locale.service';
import { AuthService } from '../../../services/auth.service';
import { BadgePathComponent } from '../badge-path/badge-path.component';
import { BadgeDetailComponent } from '../badge-detail/badge-detail.component';
import { BadgeMedallionComponent } from '../badge-medallion/badge-medallion.component';
import {
  BadgeMedallionState,
  BadgeTranslate,
  badgeAriaLabel,
  badgeStateText,
  deriveBadgeState,
  isEarnedState,
} from '../badge-medallion/badge-state.util';
import { BadgeThropyItem, BadgeThropyPath } from './badge-thropy.types';

/** Başlıktaki durum efsanesi (renk körlüğü için şekilli mini medalyonlar). */
export const BADGE_LEGEND: ReadonlyArray<{ state: BadgeMedallionState; labelKey: string }> = [
  { state: 'earned', labelKey: 'earned' },
  { state: 'new', labelKey: 'new' },
  { state: 'in-progress', labelKey: 'inProgress' },
  { state: 'locked', labelKey: 'locked' },
];

/** Issue #889: "Neredeyse" şeridindeki en yakın rozet sayısı. */
export const NEARLY_COUNT = 3;

/** Seçimin geldiği bölüm; detay paneli o bölümün altında açılır (tek panel). */
export type BadgeSelectionSource = 'top' | 'path' | 'standalone';

/**
 * Rozetler sayfası. #889: önce tamamlanmaya en yakın üç rozet ("Neredeyse"), sonra kazanılanlar, sonra kapalı yol
 * başlıkları; durum açıklaması "?" içinde.
 */
@Component({
  selector: 'app-badge-thropy',
  imports: [
    MatProgressSpinnerModule,
    MatButtonModule,
    MatIconModule,
    BadgePathComponent,
    BadgeDetailComponent,
    BadgeMedallionComponent,
    TranslocoDirective,
  ],
  templateUrl: './badge-thropy.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./badge-thropy.component.scss'],
})
export class BadgeThropyComponent implements OnInit, OnChanges {
  /** Boş bırakılırsa sözlükten (`shared.badgeThropy.title`) gelir. */
  @Input() title = '';
  /** Boş bırakılırsa sözlükten (`shared.badgeThropy.subtitle`) gelir. */
  @Input() subtitle = '';
  @Input() userId: number = 0;
  /**
   * CR Ö1: sayfa zaten kendi bölüm başlığını gösteriyorsa (öğrenci profili) başlık/alt başlık render edilmez;
   * özet çipi ve efsane kalır.
   */
  @Input() showHeading = true;

  @Output() badgeSelected = new EventEmitter<string>();

  private readonly badgeService = inject(BadgeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly authService = inject(AuthService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  /** Sayı biçimi aktif dile bağlıdır (dil değişince sayfa yeniden yüklenir). */
  private readonly numberFormatter = new Intl.NumberFormat(
    this.localeService.localeDefinition().angularLocale
  );

  /** Dışarıdan başlık verilmediyse çeviriye düşer. */
  get headingTitle(): string {
    return this.title || this.transloco.translate<string>('shared.badgeThropy.title') || '';
  }

  get headingSubtitle(): string {
    return this.subtitle || this.transloco.translate<string>('shared.badgeThropy.subtitle') || '';
  }

  readonly badges = signal<BadgeThropyItem[]>([]);
  readonly badgePaths = signal<BadgeThropyPath[]>([]);
  readonly standaloneBadges = signal<BadgeThropyItem[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal(false);
  readonly selectedBadge = signal<BadgeThropyItem | null>(null);
  readonly selectedSource = signal<BadgeSelectionSource | null>(null);
  /** #889: durum açıklaması varsayılan kapalı, "?" ile açılır. */
  readonly legendOpen = signal(false);
  /** #889: yollar kapalı başlık; açılan yolların anahtarları. */
  readonly expandedPaths = signal<ReadonlySet<string>>(new Set());

  /**
   * #889: tamamlanmaya en yakın rozetler — ilerlemesi olanlar yüzdeye (eşitse kalan miktara) göre; üçü dolmazsa her yolun
   * sıradaki rozetiyle tamamlanır.
   */
  readonly nearlyBadges = computed(() => {
    const pending = this.badges().filter((b) => !b.isCompleted);
    const started = pending
      .filter((b) => b.currentValue > 0)
      .sort((a, b) => b.progressPercent - a.progressPercent || (a.targetValue - a.currentValue) - (b.targetValue - b.currentValue));
    const picked = started.slice(0, NEARLY_COUNT);
    for (const path of this.badgePaths()) {
      if (picked.length >= NEARLY_COUNT) break;
      const next = path.badges.find((b) => !b.isCompleted);
      if (next && !picked.includes(next)) picked.push(next);
    }
    return picked;
  });

  /** #889: kazanılanlar, en yenisi önce. */
  readonly earnedBadges = computed(() =>
    this.badges()
      .filter((b) => isEarnedState(b.state))
      .sort((a, b) => (b.earnedDateUtc ?? '').localeCompare(a.earnedDateUtc ?? '')),
  );

  /** Kazanılan tekil rozetler yukarıda; burada yalnız henüz kazanılmamışlar. */
  readonly pendingStandalone = computed(() => this.standaloneBadges().filter((b) => !isEarnedState(b.state)));
  readonly earnedCount = computed(() => this.badges().filter((badge) => isEarnedState(badge.state)).length);
  readonly legend = BADGE_LEGEND;

  ngOnInit(): void {
    this.loadBadges(this.userId);
  }

  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['userId'];
    if (change && !change.isFirstChange()) {
      const previousValue = (change.previousValue as number | undefined) ?? 0;
      const currentValue = (change.currentValue as number | undefined) ?? 0;
      if (currentValue !== previousValue) {
        this.loadBadges(currentValue);
      }
    }
  }

  selectBadge(badge: BadgeThropyItem, source: BadgeSelectionSource = 'standalone'): void {
    if (!badge) {
      return;
    }

    const isSame = this.selectedBadge()?.id === badge.id && this.selectedSource() === source;
    this.selectedBadge.set(isSame ? null : badge);
    this.selectedSource.set(isSame ? null : source);

    if (!isSame) {
      this.badgeSelected.emit(badge.id);
    }
  }

  /** Tekil rozet kartının erişilebilir adı; medalyon `aria-hidden`. */
  ariaLabel(t: BadgeTranslate, badge: BadgeThropyItem): string {
    return badgeAriaLabel(t, {
      state: badge.state,
      name: badge.name,
      current: badge.completedLabel,
      target: badge.totalLabel,
      earnedDateUtc: badge.earnedDateUtc,
      locale: this.localeService.localeDefinition().angularLocale,
    });
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

  /** CR U1: yol düğümü etkinleştirildi — seçimin tek kaynağı burası (aç/kapa dahil). */
  onPathBadgeSelected(id: string): void {
    const badge = this.badges().find((item) => item.id === id);
    if (badge) {
      this.selectBadge(badge, 'path');
    }
  }

  togglePath(key: string): void {
    this.expandedPaths.update((open) => {
      const next = new Set(open);
      if (next.has(key)) {
        next.delete(key);
        if (this.selectedSource() === 'path' && this.selectedBadge()?.pathKey === key) {
          this.selectedBadge.set(null);
          this.selectedSource.set(null);
        }
      } else {
        next.add(key);
      }
      return next;
    });
  }

  /** Yolun sıradaki (ilk kazanılmamış) rozeti; hepsi kazanıldıysa null. */
  nextInPath(path: BadgeThropyPath): BadgeThropyItem | null {
    return path.badges.find((b) => !b.isCompleted) ?? null;
  }

  reload(): void {
    this.loadBadges(this.userId);
  }

  private loadBadges(userId: number): void {
    this.isLoading.set(true);
    this.loadError.set(false);
    this.selectedBadge.set(null);
    this.selectedSource.set(null);
    const storedUserId = this.authService.getUserIdFromLocalStorage();
    this.badgeService
      .getUserBadgeProgress(userId || storedUserId || 0)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          const now = Date.now();
          const mapped = (response?.badgeProgress ?? []).map((item) => this.mapBadge(item, now));
          this.badges.set(mapped);

          const partition = this.partitionBadges(mapped);
          this.badgePaths.set(partition.paths);
          this.standaloneBadges.set(partition.standalone);

          // #889: açılışta seçim yok; yollar kapalı, detay kullanıcı seçince açılır.
          this.selectedBadge.set(null);
          this.selectedSource.set(null);
          this.expandedPaths.set(new Set());
          this.isLoading.set(false);
        },
        error: (error) => {
          console.error('BadgeThropyComponent: unable to load badge progress', error);
          this.badges.set([]);
          this.badgePaths.set([]);
          this.standaloneBadges.set([]);
          this.loadError.set(true);
          this.isLoading.set(false);
        },
      });
  }

  private mapBadge(source: BadgeProgressItem, now: number): BadgeThropyItem {
    const progressValue = source.targetValue > 0 ? (source.currentValue / source.targetValue) * 100 : 0;
    const progressPercent = Math.max(0, Math.min(Math.round(progressValue), 100));

    const completedAmount = this.formatNumber(source.currentValue);
    const totalAmount = this.formatNumber(source.targetValue);
    const remainingAmount = this.formatNumber(Math.max((source.targetValue ?? 0) - (source.currentValue ?? 0), 0));

    return {
      id: source.badgeDefinitionId,
      name: source.name,
      icon: source.icon ?? null,
      iconUrl: source.iconUrl ?? null,
      description: source.description,
      currentValue: source.currentValue,
      targetValue: source.targetValue,
      progressPercent,
      completedLabel: completedAmount,
      totalLabel: totalAmount,
      remainingLabel: remainingAmount,
      isCompleted: !!source.isCompleted,
      state: deriveBadgeState(
        { isCompleted: !!source.isCompleted, earnedDateUtc: source.earnedDateUtc, currentValue: source.currentValue },
        now
      ),
      earnedDateUtc: source.earnedDateUtc,
      pathKey: source.pathKey ?? null,
      pathName: source.pathName ?? null,
      pathOrder: Number.isFinite(source.pathOrder as number) ? Number(source.pathOrder) : null,
    };
  }

  private formatNumber(value: number): string {
    if (!Number.isFinite(value) || value < 0) {
      return '0';
    }

    return this.numberFormatter.format(value);
  }

  private partitionBadges(items: BadgeThropyItem[]): { paths: BadgeThropyPath[]; standalone: BadgeThropyItem[] } {
    const pathMap = new Map<string, BadgeThropyPath>();
    const standalone: BadgeThropyItem[] = [];

    for (const item of items) {
      if (item.pathKey) {
        const key = item.pathKey;
        let group = pathMap.get(key);
        if (!group) {
          group = {
            key,
            name: item.pathName || item.name,
            badges: [],
            completedCount: 0,
            completionPercent: 0,
          };
          pathMap.set(key, group);
        }
        group.badges.push(item);
      } else {
        standalone.push(item);
      }
    }

    const paths = Array.from(pathMap.values()).map((path) => {
      path.badges.sort((a, b) => {
        const orderA = a.pathOrder ?? Number.MAX_SAFE_INTEGER;
        const orderB = b.pathOrder ?? Number.MAX_SAFE_INTEGER;
        if (orderA !== orderB) {
          return orderA - orderB;
        }
        if (a.targetValue !== b.targetValue) {
          return a.targetValue - b.targetValue;
        }
        return a.name.localeCompare(b.name);
      });

      const completedCount = path.badges.filter((badge) => badge.isCompleted).length;
      const totalCount = Math.max(path.badges.length, 1);
      path.completedCount = completedCount;
      path.completionPercent = Math.round((completedCount / totalCount) * 100);

      if (!path.name && path.badges.length) {
        path.name = path.badges[0].pathName || path.badges[0].name;
      }

      return path;
    });

    paths.sort((a, b) => {
      const orderA = a.badges[0]?.pathOrder ?? Number.MAX_SAFE_INTEGER;
      const orderB = b.badges[0]?.pathOrder ?? Number.MAX_SAFE_INTEGER;
      if (orderA !== orderB) {
        return orderA - orderB;
      }
      return a.name.localeCompare(b.name);
    });

    standalone.sort((a, b) => a.name.localeCompare(b.name));

    return { paths, standalone };
  }
}

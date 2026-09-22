import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, finalize } from 'rxjs';

import { LeaderboardEntry, LeaderboardResult, LeaderboardScope } from '../../../models/leaderboard.model';
import { AuthService } from '../../../services/auth.service';
import { LEADERBOARD_PAGE_SIZE, LeaderboardService } from '../../../services/leaderboard.service';

/** Kendi sözlüğü: `public/i18n/leaderboard/<lang>.json` — sayfa scope'una bağımlı değil. */
const SCOPE = 'leaderboard';

/**
 * Liderlik tablosu (issue #193): "Herkes / Okulum" kapsam geçişli, sayfalı XP sıralaması.
 *
 * İlk istek (hibrit, tek istek hedefi):
 * - Profil önbelleğinde okul varsa (`AuthService.schoolIdOf`) doğrudan `scope=school` istenir. Okul kimliği
 *   gönderilmez, yalnızca kapsam seçilir; sunucu 400 dönerse (okul yok/doğrulanamadı) `schoolScopeAvailable=false`
 *   kabul edilip `global` çekilir.
 * - Profilde okul yoksa `global` yoklanır; yanıttaki `schoolScopeAvailable` true ise liste gösterilmeden
 *   `school` çekilir → varsayılan "Okulum" (okul içi rekabet daha anlamlı).
 * Her durumda son sözü sunucunun `schoolScopeAvailable` alanı söyler; toggle ona göre görünür.
 */
@Component({
  selector: 'app-leaderboard',
  standalone: true,
  imports: [MatButtonModule, MatButtonToggleModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './leaderboard.component.html',
  styleUrl: './leaderboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LeaderboardComponent implements OnInit {
  private readonly leaderboardService = inject(LeaderboardService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  /** Aktif kapsam; ilk yanıt gelene kadar başlangıç tahmini. */
  readonly scope = signal<LeaderboardScope>('global');
  /** İlk başarılı yanıt gelene kadar `null` → toggle render edilmez. */
  readonly schoolScopeAvailable = signal<boolean | null>(null);
  readonly entries = signal<LeaderboardEntry[]>([]);
  readonly totalCount = signal(0);
  readonly myRank = signal<number | null>(null);
  readonly myXp = signal<number | null>(null);

  /** İlk sayfa veya kapsam değişimi yükleniyor (liste gizli). */
  readonly loading = signal(false);
  /** "Daha fazla yükle" devam ediyor (liste görünür kalır). */
  readonly loadingMore = signal(false);

  /** İlk sayfa / kapsam değişimi başarısız. Metin: sunucu mesajı varsa o, yoksa şablonda `t('error')`. */
  readonly failed = signal(false);
  readonly errorMessage = signal<string | null>(null);
  /** "Daha fazla yükle" başarısız; mevcut liste korunur, aynı `skip` ile tekrar denenir. */
  readonly moreFailed = signal(false);
  readonly moreErrorMessage = signal<string | null>(null);

  readonly showScopeToggle = computed(() => this.schoolScopeAvailable() === true);
  readonly hasMore = computed(() => this.entries().length < this.totalCount());
  readonly isEmpty = computed(() => !this.loading() && !this.failed() && this.entries().length === 0);
  readonly showMyRank = computed(() => !this.loading() && !this.failed() && this.myRank() !== null);

  private inflight: Subscription | null = null;

  ngOnInit(): void {
    this.loadInitial();
  }

  /** Toggle değişimi: aynı kapsama tekrar tıklanınca istek atılmaz. */
  setScope(scope: LeaderboardScope): void {
    if (scope === this.scope()) {
      return;
    }
    this.scope.set(scope);
    this.loadPage(scope, 0, false);
  }

  loadMore(): void {
    if (this.loading() || this.loadingMore() || !this.hasMore()) {
      return;
    }
    this.loadPage(this.scope(), this.entries().length, true);
  }

  /** İlk yükleme hatasından sonra tam yeniden başlar; kapsam bilindikten sonra aynı kapsamı sayfa 0'dan çeker. */
  retry(): void {
    if (this.schoolScopeAvailable() === null) {
      this.loadInitial();
    } else {
      this.loadPage(this.scope(), 0, false);
    }
  }

  /** "Daha fazla yükle" hatası: liste korunur, kaldığı `skip` ile tekrar dener. */
  retryMore(): void {
    this.moreFailed.set(false);
    this.moreErrorMessage.set(null);
    this.loadMore();
  }

  private loadInitial(): void {
    const cachedSchoolId = AuthService.schoolIdOf(this.auth.user());
    if (cachedSchoolId !== null) {
      this.loadSchoolFirst();
    } else {
      this.probeGlobal();
    }
  }

  /** Profilde okul var: doğrudan `school`. 400 → okul kapsamı yok say, `global` çek. */
  private loadSchoolFirst(): void {
    this.beginInitial();
    this.scope.set('school');
    this.inflight = this.leaderboardService
      .getLeaderboard('school', 0, LEADERBOARD_PAGE_SIZE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.schoolScopeAvailable.set(result.schoolScopeAvailable);
          this.apply(result, false);
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          if (err.status === 400) {
            this.schoolScopeAvailable.set(false);
            this.scope.set('global');
            this.loadPage('global', 0, false);
            return;
          }
          this.failInitial(err);
        },
      });
  }

  /** Profilde okul yok: `global` yoklanır; sunucu okul bildirirse liste gösterilmeden `school` çekilir. */
  private probeGlobal(): void {
    this.beginInitial();
    this.scope.set('global');
    this.inflight = this.leaderboardService
      .getLeaderboard('global', 0, LEADERBOARD_PAGE_SIZE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.schoolScopeAvailable.set(result.schoolScopeAvailable);
          if (result.schoolScopeAvailable) {
            this.scope.set('school');
            this.loadPage('school', 0, false);
            return;
          }
          this.apply(result, false);
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => this.failInitial(err),
      });
  }

  private beginInitial(): void {
    this.inflight?.unsubscribe();
    this.loading.set(true);
    this.failed.set(false);
    this.errorMessage.set(null);
    this.moreFailed.set(false);
    this.moreErrorMessage.set(null);
  }

  private failInitial(err: HttpErrorResponse): void {
    this.loading.set(false);
    this.failed.set(true);
    this.errorMessage.set(this.leaderboardService.extractError(err));
  }

  private loadPage(scope: LeaderboardScope, skip: number, append: boolean): void {
    this.inflight?.unsubscribe();
    this.moreFailed.set(false);
    this.moreErrorMessage.set(null);
    if (append) {
      this.loadingMore.set(true);
    } else {
      this.loading.set(true);
      this.failed.set(false);
      this.errorMessage.set(null);
    }

    this.inflight = this.leaderboardService
      .getLeaderboard(scope, skip, LEADERBOARD_PAGE_SIZE)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.loading.set(false);
          this.loadingMore.set(false);
        })
      )
      .subscribe({
        next: (result) => {
          this.schoolScopeAvailable.set(result.schoolScopeAvailable);
          this.apply(result, append);
        },
        error: (err: HttpErrorResponse) => {
          const message = this.leaderboardService.extractError(err);
          if (append) {
            // Mevcut satırlar korunur; kullanıcı aynı skip ile tekrar dener.
            this.moreFailed.set(true);
            this.moreErrorMessage.set(message);
            return;
          }
          this.entries.set([]);
          this.totalCount.set(0);
          this.failed.set(true);
          this.errorMessage.set(message);
        },
      });
  }

  private apply(result: LeaderboardResult, append: boolean): void {
    const incoming = result.entries ?? [];
    this.entries.set(append ? [...this.entries(), ...incoming] : incoming);
    this.totalCount.set(result.totalCount ?? incoming.length);
    this.myRank.set(result.myRank ?? null);
    this.myXp.set(result.myXp ?? null);
  }
}

import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, finalize, take } from 'rxjs';

import { activityErrorCode, activityErrorMessage } from '../../models/activity';
import { activityStepLabel } from '../../shared/components/activity-player-frame/activity-player-frame.component';
import { StorageImageRetryDirective } from '../../shared/directives/storage-image-retry.directive';
import { createStorageImageResolver } from '../../shared/utils/storage-image-refresh.util';
import {
  OPEN_TEXT_EVALUATIONS,
  OPEN_TEXT_LIMITS,
  OPEN_TEXT_QUEUE_STATUSES,
  OPEN_TEXT_REVIEW_ERROR_CODES,
  OpenTextEvaluation,
  OpenTextQueueStatus,
  OpenTextReviewItem,
  OpenTextStudentReview,
  isOpenTextEvaluation,
  stripOpenTextControlChars,
} from '../../models/open-text';
import { LocaleService } from '../../services/locale.service';
import { OpenTextReviewService } from '../../services/open-text-review.service';
import { OpenTextReviewStatusComponent } from '../../shared/components/open-text-review-status/open-text-review-status.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/** Çeviriler kendi Transloco scope'unda: `public/i18n/open-text-reviews/<lang>.json`. */
export const OPEN_TEXT_REVIEWS_SCOPE = 'open-text-reviews';
export const OPEN_TEXT_REVIEWS_PAGE_SIZE = 20;

/** Satırdaki değerlendirme formu (kaydedilene dek yerel). */
export interface OpenTextReviewDraft {
  evaluation: OpenTextEvaluation | null;
  feedback: string;
}

/**
 * Issue #583: öğretmenin açık uçlu cevap değerlendirme kuyruğu (`/open-text-reviews`, isteğe bağlı `?worksheetId=`). Durum filtresi
 * (bekleyen / değerlendirilen / hepsi), sayfalama; satırda öğrenci numarası, sınıf, etkinlik + madde, madde görseli, öğrencinin
 * metni (düz metin, `pre-wrap`), sözcük sayısı / sınırlar, örnek cevap + rubrik (açılır) ve mevcut değerlendirme. Değerlendir /
 * düzelt: yeterli / kısmen / eksik + geri bildirim (≤ 1000) → `PUT` (kuyruktaki `answerHash` ile). 409 `OpenTextAnswerChanged`
 * → "Cevap değişti" + liste yenilenir; diğer hatalar sunucu mesajıyla snackbar. Puan yok. Kapsam (hangi öğrenciler) sunucuda.
 *
 * Öğrenci metni ve geri bildirim yalnız interpolasyonla gösterilir (HTML yorumlanmaz). Kartlar telefonda tek sütun.
 */
@Component({
  selector: 'app-open-text-reviews',
  standalone: true,
  imports: [
    PageHeaderComponent,
    MatButtonModule,
    MatButtonToggleModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    TranslocoPipe,
    OpenTextReviewStatusComponent,
    StorageImageRetryDirective,
  ],
  providers: [provideTranslocoScope(OPEN_TEXT_REVIEWS_SCOPE)],
  templateUrl: './open-text-reviews.component.html',
  host: { '(document:keydown)': 'onKeydown($event)' },
  styleUrl: './open-text-reviews.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OpenTextReviewsComponent {
  private readonly service = inject(OpenTextReviewService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  readonly statuses = OPEN_TEXT_QUEUE_STATUSES;
  readonly evaluations = OPEN_TEXT_EVALUATIONS;
  readonly limits = OPEN_TEXT_LIMITS;

  readonly status = signal<OpenTextQueueStatus>('pending');
  readonly worksheetId = signal<number | null>(null);
  readonly pageIndex = signal(0);
  readonly pageSize = signal(OPEN_TEXT_REVIEWS_PAGE_SIZE);

  readonly items = signal<OpenTextReviewItem[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  /** Yanıt geldi (ilk yükleme bitti); boş durum yalnız bundan sonra gösterilir. */
  readonly loaded = signal(false);
  readonly savingId = signal<number | null>(null);
  readonly drafts = signal<Record<number, OpenTextReviewDraft>>({});

  /** Issue #864: sağ panelde açık satır; yoksa listenin ilki. */
  readonly selectedId = signal<number | null>(null);
  readonly selected = computed(() => {
    const items = this.items();
    return items.find((item) => item.instanceQuestionId === this.selectedId()) ?? items[0] ?? null;
  });
  /** Kayıttan sonra yeniden okunan sayfada sıradaki satıra geçmek için kaydedilen satır ve sırası. */
  private advanceFrom: { id: number; index: number } | null = null;

  readonly isEmpty = computed(() => this.loaded() && !this.loading() && !this.error() && this.items().length === 0);
  /** Etkinlik filtresinin adı (listedeki satırlardan; boşsa yalnız "Bu etkinlik"). */
  readonly worksheetName = computed(() => {
    const id = this.worksheetId();
    return id ? this.items().find((item) => item.worksheetId === id)?.worksheetName ?? null : null;
  });

  private loadSub: Subscription | null = null;

  /** İmzalı görselin süresi dolarsa aynı sayfa yeniden çekilir ve taze URL kullanılır (kuyruk görüntülenmeye devam eder). */
  readonly imageRefresh = createStorageImageResolver(
    () =>
      this.service.list({ status: this.status(), worksheetId: this.worksheetId(), page: this.pageIndex() + 1, pageSize: this.pageSize() }),
    {
      collect: (page) => (page?.items ?? []).map((item) => item.questionImageUrl),
      cacheKey: () => `${this.status()}|${this.worksheetId()}|${this.pageIndex()}|${this.pageSize()}`,
    }
  );

  constructor() {
    this.preloadScope();
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const raw = Number(params.get('worksheetId'));
      this.worksheetId.set(Number.isInteger(raw) && raw > 0 ? raw : null);
      const status = params.get('status');
      this.status.set((OPEN_TEXT_QUEUE_STATUSES as readonly string[]).includes(status ?? '') ? (status as OpenTextQueueStatus) : 'pending');
      this.pageIndex.set(0);
      this.load();
    });
  }

  load(): void {
    this.loadSub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.loadSub = this.service
      .list({ status: this.status(), worksheetId: this.worksheetId(), page: this.pageIndex() + 1, pageSize: this.pageSize() })
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (page) => {
          const items = page?.items ?? [];
          const total = page?.totalCount ?? 0;
          // Son sayfanın son satırı değerlendirildiyse bir önceki sayfaya dön.
          if (items.length === 0 && this.pageIndex() > 0 && total > 0) {
            this.pageIndex.set(this.pageIndex() - 1);
            this.load();
            return;
          }
          this.items.set(items);
          this.totalCount.set(total);
          this.applyAdvance(items);
          this.seedDrafts(items);
          this.loaded.set(true);
        },
        error: (err: unknown) => {
          const rateLimited =
            (err instanceof HttpErrorResponse && err.status === 429) ||
            activityErrorCode(err) === OPEN_TEXT_REVIEW_ERROR_CODES.rateLimited;
          const message = activityErrorMessage(err) ?? this.text(rateLimited ? 'messages.rateLimited' : 'messages.loadFailed');
          if (rateLimited) {
            this.snackBar.open(message, this.text('messages.ok'), { duration: 5000 });
            // Liste duruyorsa olduğu gibi kalır; hiç yoksa hata + "Tekrar dene".
            if (this.items().length > 0) {
              this.loaded.set(true);
              return;
            }
          }
          this.error.set(message);
          this.loaded.set(true);
        },
      });
  }

  setStatus(status: OpenTextQueueStatus): void {
    if (status === this.status()) return;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { status: status === 'pending' ? null : status },
      queryParamsHandling: 'merge',
    });
  }

  clearWorksheet(): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { worksheetId: null }, queryParamsHandling: 'merge' });
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  select(item: OpenTextReviewItem): void {
    this.selectedId.set(item.instanceQuestionId);
  }

  /** Issue #864: hazır geri bildirim cümleleri (değerlendirmeye göre). */
  phrasesFor(evaluation: OpenTextEvaluation): string[] {
    const phrases = this.transloco.translateObject<string[]>(`${OPEN_TEXT_REVIEWS_SCOPE}.phrases.${evaluation}`);
    return Array.isArray(phrases) ? phrases : [];
  }

  addPhrase(item: OpenTextReviewItem, phrase: string): void {
    if (!this.canEdit(item)) return;
    const current = this.draftOf(item).feedback.trim();
    const next = (current ? `${current} ${phrase}` : phrase).slice(0, OPEN_TEXT_LIMITS.maxFeedbackLength);
    this.patchDraft(item, { feedback: next });
  }

  /**
   * Issue #864: kısayollar — 1/2/3 değerlendirme, J/K sonraki/önceki, Ctrl/⌘+Enter kaydet. Yazı alanındayken yalnız kaydet
   * kısayolu çalışır (rakam geri bildirime yazılır).
   */
  onKeydown(event: KeyboardEvent): void {
    const item = this.selected();
    if (!item || event.altKey) return;
    const target = event.target instanceof Element ? event.target : null;
    const typing = !!target?.closest('input, textarea, select, [contenteditable="true"]');
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      if (this.canSave(item)) {
        event.preventDefault();
        this.save(item);
      }
      return;
    }
    if (typing || event.ctrlKey || event.metaKey) return;
    const index = ['1', '2', '3'].indexOf(event.key);
    if (index >= 0 && this.canEdit(item) && this.savingId() === null) {
      event.preventDefault();
      this.setEvaluation(item, this.evaluations[index]);
      return;
    }
    if (event.key === 'j' || event.key === 'k') {
      const items = this.items();
      const at = items.findIndex((i) => i.instanceQuestionId === item.instanceQuestionId);
      const next = items[at + (event.key === 'j' ? 1 : -1)];
      if (next) {
        event.preventDefault();
        this.select(next);
      }
    }
  }

  draftOf(item: OpenTextReviewItem): OpenTextReviewDraft {
    return this.drafts()[item.instanceQuestionId] ?? { evaluation: null, feedback: '' };
  }

  setEvaluation(item: OpenTextReviewItem, value: unknown): void {
    if (!isOpenTextEvaluation(value)) return;
    this.patchDraft(item, { evaluation: value });
  }

  setFeedback(item: OpenTextReviewItem, event: Event): void {
    const field = event.target as HTMLTextAreaElement | null;
    if (!this.canEdit(item)) return;
    const raw = field?.value ?? '';
    const clean = stripOpenTextControlChars(raw).slice(0, OPEN_TEXT_LIMITS.maxFeedbackLength);
    if (field && clean !== raw) field.value = clean;
    this.patchDraft(item, { feedback: clean });
  }

  /** Satır bu öğretmen tarafından değerlendirilebilir mi (alan yoksa evet). */
  canEdit(item: OpenTextReviewItem): boolean {
    return item.canEdit !== false;
  }

  /** Madde etiketi (a, b, c …) — oynatıcının sırası; numara yoksa null. */
  itemLabel(item: OpenTextReviewItem): string | null {
    const n = item.itemNumber;
    return typeof n === 'number' && Number.isInteger(n) && n >= 1 ? activityStepLabel(n - 1) : null;
  }

  /** Kaydet düğmesi: değerlendirme seçili, kayıt sürmüyor ve (düzeltmede) bir şey değişti. */
  canSave(item: OpenTextReviewItem): boolean {
    const draft = this.draftOf(item);
    if (!this.canEdit(item) || !draft.evaluation || this.savingId() !== null) return false;
    const review = item.review;
    if (!review || review.stale || item.status !== 'reviewed') return true;
    return review.evaluation !== draft.evaluation || (review.feedback ?? '') !== draft.feedback.trim();
  }

  /** Kuyruk satırındaki değerlendirme, ortak durum bileşeninin biçiminde. */
  currentReview(item: OpenTextReviewItem): OpenTextStudentReview | null {
    const review = item.review;
    if (!review) return null;
    return {
      status: review.stale || item.status === 'outdated' ? 'outdated' : 'reviewed',
      evaluation: review.evaluation,
      feedback: review.feedback,
      reviewedAt: review.reviewedAt,
    };
  }

  save(item: OpenTextReviewItem): void {
    const draft = this.draftOf(item);
    if (!this.canEdit(item) || !draft.evaluation || this.savingId() !== null) return;
    this.savingId.set(item.instanceQuestionId);
    const feedback = draft.feedback.trim();
    this.service
      .review(item.instanceQuestionId, { evaluation: draft.evaluation, feedback: feedback || null, answerHash: item.answerHash })
      .pipe(
        finalize(() => this.savingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (result) => {
          if (result?.success === false) {
            this.onSaveError(result);
            return;
          }
          this.snackBar.open(this.text('messages.saved'), this.text('messages.ok'), { duration: 3000 });
          this.afterSaved(item);
          this.service.refreshPendingCount().subscribe({ error: () => {} });
        },
        error: (err: unknown) => this.onSaveError(err),
      });
  }

  formatDate(iso: string | null): string {
    if (!iso) return '—';
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return '—';
    return date.toLocaleString(this.localeService.localeDefinition().angularLocale, {
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  /**
   * Kayıttan sonra geçerli sayfa sunucudan yeniden okunur: bekleyen listesinde satırı yerelde çıkarmak sonraki sayfanın ilk
   * satırını atlatırdı (sayfa kayması). Kaydedilen satırın formu sunucudaki değerlendirmeyle yeniden başlar.
   */
  private afterSaved(item: OpenTextReviewItem): void {
    const id = item.instanceQuestionId;
    this.advanceFrom = { id, index: this.items().findIndex((i) => i.instanceQuestionId === id) };
    this.drafts.update((all) => {
      const next = { ...all };
      delete next[id];
      return next;
    });
    this.load();
  }

  /**
   * Issue #864: kayıttan sonra sıradaki cevap açılır — bekleyen listesinde kaydedilen satır düştüğü için aynı sıra, satır
   * listede kaldıysa (değerlendirilen / hepsi) bir sonraki. Seçim listede yoksa ilk satır.
   */
  private applyAdvance(items: OpenTextReviewItem[]): void {
    const from = this.advanceFrom;
    this.advanceFrom = null;
    if (from && from.index >= 0 && items.length > 0) {
      const stayed = items.findIndex((i) => i.instanceQuestionId === from.id);
      const index = stayed >= 0 ? Math.min(stayed + 1, items.length - 1) : Math.min(from.index, items.length - 1);
      this.selectedId.set(items[index].instanceQuestionId);
      return;
    }
    if (!items.some((i) => i.instanceQuestionId === this.selectedId())) {
      this.selectedId.set(items[0]?.instanceQuestionId ?? null);
    }
  }

  private onSaveError(source: unknown): void {
    const code = activityErrorCode(source);
    const status = source instanceof HttpErrorResponse ? source.status : null;
    if (code === OPEN_TEXT_REVIEW_ERROR_CODES.answerChanged) {
      this.snackBar.open(this.text('messages.answerChanged'), this.text('messages.ok'), { duration: 5000 });
      this.load();
      return;
    }
    if (code === OPEN_TEXT_REVIEW_ERROR_CODES.forbidden || status === 403) {
      this.snackBar.open(activityErrorMessage(source) ?? this.text('messages.forbidden'), this.text('messages.ok'), { duration: 5000 });
      this.load();
      return;
    }
    const message =
      status === 429 || code === OPEN_TEXT_REVIEW_ERROR_CODES.rateLimited
        ? activityErrorMessage(source) ?? this.text('messages.rateLimited')
        : activityErrorMessage(source) ?? this.text('messages.saveFailed');
    this.snackBar.open(message, this.text('messages.ok'), { duration: 5000 });
    // Satır artık kuyrukta değil / gönderilmemiş: liste tazelenir.
    if (code === OPEN_TEXT_REVIEW_ERROR_CODES.notFound || code === OPEN_TEXT_REVIEW_ERROR_CODES.notSubmitted) this.load();
  }

  private patchDraft(item: OpenTextReviewItem, change: Partial<OpenTextReviewDraft>): void {
    const id = item.instanceQuestionId;
    this.drafts.update((all) => ({ ...all, [id]: { ...this.draftOf(item), ...change } }));
  }

  /** Yeni gelen satırların formu mevcut değerlendirmeyle başlar; öğretmenin yazmakta olduğu taslak korunur. */
  private seedDrafts(items: OpenTextReviewItem[]): void {
    const current = this.drafts();
    const next: Record<number, OpenTextReviewDraft> = {};
    for (const item of items) {
      next[item.instanceQuestionId] = current[item.instanceQuestionId] ?? this.draftFromItem(item);
    }
    this.drafts.set(next);
  }

  private draftFromItem(item: OpenTextReviewItem): OpenTextReviewDraft {
    return { evaluation: item.review?.evaluation ?? null, feedback: item.review?.feedback ?? '' };
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${OPEN_TEXT_REVIEWS_SCOPE}.${key}`, params) ?? '';
  }

  /** Şablon dışı metinler (snackbar) senkron okunduğu için scope önceden yüklenir. */
  private preloadScope(): void {
    this.transloco.load(`${OPEN_TEXT_REVIEWS_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }
}

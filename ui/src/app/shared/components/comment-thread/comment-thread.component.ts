import { isPlatformBrowser } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  PLATFORM_ID,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  debounceTime,
  defer,
  filter,
  finalize,
  interval,
  map,
  merge,
  skip,
  switchMap,
  take,
  tap,
} from 'rxjs';
import {
  WORKSHEET_COMMENT_DEFAULT_TAKE,
  WORKSHEET_COMMENT_MAX_LENGTH,
  WORKSHEET_COMMENT_MAX_TAKE,
  WorksheetComment,
  WorksheetCommentLockReason,
  WorksheetCommentPage,
  WorksheetCommentRepliesPage,
  WorksheetCommentRoot,
  WorksheetCommentStudentSummary,
  parseStudentCommentsSummary,
  toQuestionOrder,
} from '../../../models/worksheet-comment.model';
import { parseUtcDate } from '../../../pages/notifications/notification-format';
import { LocaleService } from '../../../services/locale.service';
import { WorksheetCommentService } from '../../../services/worksheet-comment.service';
import { CommentComposerComponent } from '../comment-composer/comment-composer.component';
import { CommentItemComponent } from '../comment-item/comment-item.component';
import { commentErrorMessage } from './comment-error';
import { CommentView, ThreadView, canLoadOlderReplies, serverCommentView } from './comment-view';

/** Yorum bileşenlerinin Transloco scope'u: `public/i18n/comments/<lang>.json`. */
export const COMMENTS_SCOPE = 'comments';

/** Derin linkte vurgunun süresi (maket 6b). */
export const COMMENT_HIGHLIGHT_MS = 2000;

/** Derin linkteki yorumu bulmak için otomatik yüklenecek en fazla ek sayfa (kök ya da replies). */
export const COMMENT_HIGHLIGHT_MAX_PAGES = 5;

/**
 * Soru değişiminde yükleme gecikmesi — gezginde hızlı ileri/geri okuma limitini (60/dk) yakmasın.
 * İlk yükleme beklemez.
 */
export const COMMENT_CONTEXT_DEBOUNCE_MS = 250;

/** Göreli zaman ("3 dakika önce") tazeleme aralığı. */
const RELATIVE_TIME_TICK_MS = 60_000;

const LOCK_REASONS: readonly WorksheetCommentLockReason[] = [
  'comments-disabled',
  'question-not-answered',
  'worksheet-not-started',
];

interface ThreadContext {
  worksheetId: number;
  questionId: number | null;
}

/** Öğretmen özet şeridinin tek parçası (Transloco anahtarı `comments` scope'unda + parametre). */
export interface StudentSummaryPart {
  key: string;
  params?: Record<string, unknown>;
}

/** Öğretmen özet şeridi (issue #309): varsayılan metni + sıfır olmayan override sayıları. */
export interface StudentSummaryStrip {
  defaultDisabled: boolean;
  defaultKey: string;
  parts: StudentSummaryPart[];
}

/**
 * `studentCommentsSummary` → şerit metin parçaları. Önce varsayılanın tersi yöndeki override'lar (asıl bilgi), sonra
 * aynı yöndekiler; sıfır sayılar gösterilmez. Tekil/çoğul ayrı anahtar (en: "1 assignment" / "2 assignments").
 */
export function studentSummaryStrip(summary: WorksheetCommentStudentSummary): StudentSummaryStrip {
  const { enabled, disabled } = summary.assignmentOverrides;
  const off = { count: disabled, key: 'thread.summary.assignmentsOff' };
  const on = { count: enabled, key: 'thread.summary.assignmentsOn' };
  const ordered = summary.worksheetDefault ? [off, on] : [on, off];
  return {
    defaultDisabled: !summary.worksheetDefault,
    defaultKey: summary.worksheetDefault ? 'thread.summary.defaultOn' : 'thread.summary.defaultOff',
    parts: ordered
      .filter((item) => item.count > 0)
      .map((item) => ({ key: item.count === 1 ? `${item.key}One` : item.key, params: { count: item.count } })),
  };
}

interface PendingHighlight {
  commentId: number;
  rootId: number | null;
  pagesLeft: number;
}

function compareComments(a: CommentView, b: CommentView): number {
  const diff = parseUtcDate(a.comment.createdAt).getTime() - parseUtcDate(b.comment.createdAt).getTime();
  return Number.isNaN(diff) || diff === 0 ? a.comment.id - b.comment.id : diff;
}

function toThreadView(root: WorksheetCommentRoot): ThreadView {
  return {
    key: `t${root.id}`,
    root: serverCommentView(root),
    replies: (root.replies ?? []).map(serverCommentView),
    replyCount: root.replyCount ?? 0,
    canReply: root.canReply === true,
    olderCursor: null,
    olderStarted: false,
    loadingOlder: false,
    olderError: null,
  };
}

/**
 * Issue #105 — worksheet veya soru bazlı yorum/soru thread'i.
 * Yazma alanı yalnız sunucunun `canWrite`'ına göre çizilir; kilit nedeni `lockReason`'dan gelir — UI tahmin yürütmez
 * (backend POST'ta aynı kuralı yeniden uygular). Tek seviye cevap, optimistic gönderim, cursor sayfalama,
 * derin link vurgusu (`highlightCommentId`).
 */
@Component({
  selector: 'app-comment-thread',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    CommentComposerComponent,
    CommentItemComponent,
  ],
  providers: [provideTranslocoScope(COMMENTS_SCOPE)],
  templateUrl: './comment-thread.component.html',
  styleUrls: ['./comment-thread.component.scss'],
})
export class CommentThreadComponent {
  private readonly api = inject(WorksheetCommentService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly worksheetId = input.required<number>();
  /** Boşsa worksheet seviyesi thread; doluysa o sorunun (`Question.id`) thread'i. */
  readonly questionId = input<number | null>(null);
  /** Derin link: vurgulanıp görünür alana kaydırılacak yorum. */
  readonly highlightCommentId = input<number | null>(null);
  /** Derin link: vurgulanacak yorum bir cevapsa kökü (son 5 dışında kalan cevabı yüklemek için). */
  readonly highlightRootId = input<number | null>(null);
  /** Görüntüleyen öğretmen mi — derin linkte cevap alanı otomatik açılır; optimistic kaydın rozeti. */
  readonly viewerIsTeacher = input(false);
  /**
   * Öğretmen görünümü: worksheet ayarında öğrenci yorumları kapalı (sunucu öğretmene `lockReason` göndermez;
   * bu bilgi worksheet'in kendi `commentsEnabled` alanından gelir).
   */
  readonly studentsLockedNotice = input(false);
  /**
   * Issue #309: öğretmen görünümünde sunucunun `studentCommentsSummary`'si ile özet şeridi gösterilsin mi (yalnız
   * worksheet thread'i — soru kartlarında şerit tekrarlanmasın). Alan null/bozuksa `studentsLockedNotice`'a düşülür.
   */
  readonly showStudentsSummary = input(false);
  /** Bilgi şeridinde "Ayarları düzenle" gösterilsin mi. */
  readonly showEditSettings = input(false);
  readonly editSettings = output<void>();
  /**
   * Issue #309: soru thread'inde sorunun 1 tabanlı sırası — thread sayfasının `questionOrder`'ından (derin linkten
   * değil); bilinmiyorsa null. Yalnız değer değişince, HTTP yanıtında ya da bağlam değişiminde yayınlanır.
   */
  readonly questionOrderChange = output<number | null>();

  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly threads = signal<ThreadView[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly canWrite = signal(false);
  protected readonly lockReason = signal<WorksheetCommentLockReason | null>(null);
  protected readonly studentSummary = signal<WorksheetCommentStudentSummary | null>(null);
  protected readonly summaryStrip = computed(() => {
    const summary = this.studentSummary();
    return summary && this.showStudentsSummary() ? studentSummaryStrip(summary) : null;
  });
  protected readonly loadingMore = signal(false);
  protected readonly loadMoreError = signal<string | null>(null);
  protected readonly draft = signal('');
  protected readonly highlightedId = signal<number | null>(null);
  protected readonly openReplies = signal<ReadonlySet<string>>(new Set<string>());
  protected readonly now = signal(Date.now());
  /** Ekran okuyucuya duyurulan son olay (yalnız başarılı gönderim) — görsel olarak gizli live region. */
  protected readonly announcement = signal('');
  protected readonly locale = computed(() => this.localeService.localeDefinition().angularLocale);

  protected readonly lockMessageKey = computed(() => {
    const reason = this.lockReason();
    return reason ? `thread.lock.${reason}` : null;
  });
  protected readonly isEmpty = computed(() => this.loaded() && !this.error() && this.threads().length === 0);

  private readonly rootComposer = viewChild(CommentComposerComponent);

  /** Thread kimliği: worksheet + soru. Değişince ekrandaki her şey sıfırlanır. */
  private readonly context = computed<ThreadContext>(
    () => ({ worksheetId: this.worksheetId(), questionId: this.questionId() }),
    { equal: (a, b) => a.worksheetId === b.worksheetId && a.questionId === b.questionId }
  );
  /** Aynı bağlamda yeniden yükleme (tekrar dene, yeni derin link). */
  private readonly reload$ = new Subject<void>();
  /** Her yeniden yüklemede artar; eski isteklerin (ilk sayfa / daha fazla / önceki cevaplar) cevabı yok sayılır. */
  private generation = 0;
  /** Yalnız bağlam (soru) değişince artar; eski bağlamda başlayan gönderimlerin yanıtı ekrana yazılmaz. */
  private contextVersion = 0;
  /** Son "daha fazla" isteği; zincirlenmiş istekte öncekinin finalize'ı yükleniyor durumunu kapatmasın. */
  private moreRequest = 0;
  /** Bir kez uygulanmış derin link yorumları — tekrar vurgulanmaz / cevap alanı tekrar açılmaz. */
  private readonly consumedHighlights = new Set<number>();
  private pendingHighlight: PendingHighlight | null = null;
  private highlightTimer: ReturnType<typeof setTimeout> | null = null;
  private tempSequence = 0;
  private emittedQuestionOrder: number | null = null;

  constructor() {
    // Şablon dışından (hata mesajları) senkron `translate()` için scope sözlüğünü render'dan bağımsız yükle.
    this.transloco.selectTranslate('thread.retry', {}, COMMENTS_SCOPE).pipe(takeUntilDestroyed()).subscribe();

    const context$ = toObservable(this.context);
    merge(
      context$.pipe(take(1)),
      context$.pipe(
        skip(1),
        tap(() => this.resetForContextChange()),
        debounceTime(COMMENT_CONTEXT_DEBOUNCE_MS)
      ),
      this.reload$.pipe(map(() => this.context()))
    )
      .pipe(
        filter((ctx) => ctx.worksheetId > 0),
        switchMap((ctx) => this.fetchFirstPage(ctx)),
        takeUntilDestroyed()
      )
      .subscribe();

    effect(() => {
      const commentId = this.highlightCommentId();
      const rootId = this.highlightRootId();
      untracked(() => {
        if (!commentId || this.consumedHighlights.has(commentId)) {
          this.pendingHighlight = null;
          return;
        }
        this.pendingHighlight = { commentId, rootId, pagesLeft: COMMENT_HIGHLIGHT_MAX_PAGES };
        // Yükleme sürüyorsa sonunda uygulanır; yüklüyse yeni yorum için taze veri çekilir.
        if (this.loaded()) {
          this.reload$.next();
        }
      });
    });

    if (this.isBrowser) {
      interval(RELATIVE_TIME_TICK_MS)
        .pipe(takeUntilDestroyed())
        .subscribe(() => this.now.set(Date.now()));
    }

    this.destroyRef.onDestroy(() => {
      if (this.highlightTimer) {
        clearTimeout(this.highlightTimer);
      }
    });
  }

  /** Aynı bağlamda ilk sayfayı yeniden yükler (hata sonrası "Tekrar dene"). */
  protected load(): void {
    this.reload$.next();
  }

  /**
   * Soru (bağlam) değişti: eski sorunun kökleri, gönderilmemiş/failed kayıtları, taslak ve açık cevap alanları yeni
   * soruya taşınmaz; eski thread gizlenir, yeni yükleme beklenirken spinner görünür.
   */
  private resetForContextChange(): void {
    this.generation++;
    this.contextVersion++;
    this.threads.set([]);
    this.loaded.set(false);
    this.loading.set(true);
    this.error.set(null);
    this.nextCursor.set(null);
    this.canWrite.set(false);
    this.lockReason.set(null);
    this.studentSummary.set(null);
    this.loadMoreError.set(null);
    this.loadingMore.set(false);
    this.draft.set('');
    this.openReplies.set(new Set<string>());
    this.highlightedId.set(null);
    this.announcement.set('');
    this.emitQuestionOrder(null);
  }

  /** İlk sayfa; aynı bağlamda yeniden yüklemede gönderilmemiş kök yorumlar korunur. */
  private fetchFirstPage(ctx: ThreadContext): Observable<unknown> {
    return defer(() => {
      const generation = ++this.generation;
      this.loading.set(true);
      this.error.set(null);
      this.loadMoreError.set(null);
      this.loadingMore.set(false);
      return this.api
        .getThread(ctx.worksheetId, { questionId: ctx.questionId, take: WORKSHEET_COMMENT_DEFAULT_TAKE })
        .pipe(
          tap({
            next: (page) => {
              if (generation !== this.generation) {
                return;
              }
              const unsent = this.threads().filter((t) => t.root.state !== 'sent');
              this.applyPageFlags(page);
              this.threads.set([...unsent, ...(page?.items ?? []).map(toThreadView)]);
              this.loaded.set(true);
              this.emitQuestionOrder(page?.questionOrder);
              this.tryHighlight();
            },
            error: (err: unknown) => {
              if (generation !== this.generation) {
                return;
              }
              this.threads.set([]);
              this.loaded.set(false);
              this.canWrite.set(false);
              this.lockReason.set(null);
              this.studentSummary.set(null);
              this.nextCursor.set(null);
              this.error.set(commentErrorMessage(err, this.t, 'thread.loadError'));
              // pendingHighlight korunur: "Tekrar dene" başarılı olunca derin link yine uygulanır.
            },
          }),
          catchError(() => EMPTY),
          finalize(() => {
            if (generation === this.generation) {
              this.loading.set(false);
            }
          })
        );
    });
  }

  protected loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor || this.loadingMore()) {
      return;
    }
    const generation = this.generation;
    const request = ++this.moreRequest;
    this.loadingMore.set(true);
    this.loadMoreError.set(null);
    this.api
      .getThread(this.worksheetId(), {
        questionId: this.questionId(),
        cursor,
        take: WORKSHEET_COMMENT_DEFAULT_TAKE,
      })
      .pipe(
        finalize(() => {
          if (generation === this.generation && request === this.moreRequest) {
            this.loadingMore.set(false);
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (page) => {
          if (generation !== this.generation) {
            return;
          }
          this.applyPageFlags(page);
          const known = new Set(this.threads().map((t) => t.root.comment.id));
          const added = (page?.items ?? []).filter((root) => !known.has(root.id)).map(toThreadView);
          this.threads.update((list) => [...list, ...added]);
          // Derin link aramasında zincirlenen sonraki sayfa isteği engellenmesin (finalize henüz çalışmadı).
          this.loadingMore.set(false);
          this.tryHighlight();
        },
        error: (err: unknown) => {
          if (generation !== this.generation) {
            return;
          }
          this.loadMoreError.set(commentErrorMessage(err, this.t, 'thread.loadMoreError'));
          this.pendingHighlight = null;
        },
      });
  }

  /** "Önceki cevapları göster": replies ucunu baştan (eskiden yeniye) sayfa sayfa yükler, mevcutlarla birleştirir. */
  protected loadOlderReplies(threadKey: string): void {
    const thread = this.findThread(threadKey);
    if (!thread || thread.loadingOlder || thread.root.state !== 'sent') {
      return;
    }
    const generation = this.generation;
    this.updateThread(threadKey, (t) => ({ ...t, loadingOlder: true, olderError: null }));
    this.api
      .getReplies(this.worksheetId(), thread.root.comment.id, {
        cursor: thread.olderStarted ? thread.olderCursor : null,
        take: WORKSHEET_COMMENT_MAX_TAKE,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          if (generation !== this.generation) {
            return;
          }
          this.updateThread(threadKey, (t) => this.mergeReplies(t, page));
          this.tryHighlight();
        },
        error: (err: unknown) => {
          if (generation !== this.generation) {
            return;
          }
          this.updateThread(threadKey, (t) => ({
            ...t,
            loadingOlder: false,
            olderError: commentErrorMessage(err, this.t, 'item.olderError'),
          }));
          this.pendingHighlight = null;
        },
      });
  }

  protected submitRoot(body: string): void {
    const text = body.trim();
    if (!text || text.length > WORKSHEET_COMMENT_MAX_LENGTH || !this.canWrite()) {
      return;
    }
    const root = this.pendingView(text, null);
    const thread: ThreadView = {
      key: `t-${root.key}`,
      root,
      replies: [],
      replyCount: 0,
      canReply: false,
      olderCursor: null,
      olderStarted: false,
      loadingOlder: false,
      olderError: null,
    };
    this.threads.update((list) => [thread, ...list]);
    this.draft.set('');
    this.sendRoot(thread.key);
  }

  protected submitReply(threadKey: string, body: string): void {
    const thread = this.findThread(threadKey);
    const text = body.trim();
    if (!thread || !thread.canReply || !text || text.length > WORKSHEET_COMMENT_MAX_LENGTH) {
      return;
    }
    const reply = this.pendingView(text, thread.root.comment.id);
    this.updateThread(threadKey, (t) => ({ ...t, replies: [...t.replies, reply] }));
    this.setReplyOpen(threadKey, false);
    this.sendReply(threadKey, reply.key);
  }

  protected retry(threadKey: string, key: string): void {
    const thread = this.findThread(threadKey);
    if (!thread) {
      return;
    }
    if (thread.root.key === key) {
      this.sendRoot(threadKey);
    } else {
      this.sendReply(threadKey, key);
    }
  }

  protected discard(threadKey: string, key: string): void {
    const thread = this.findThread(threadKey);
    if (!thread) {
      return;
    }
    if (thread.root.key === key) {
      this.threads.update((list) => list.filter((t) => t.key !== threadKey));
    } else {
      this.updateThread(threadKey, (t) => ({ ...t, replies: t.replies.filter((r) => r.key !== key) }));
    }
  }

  /** Gönderilemeyen kök yorumu ana yazma alanına geri alır. */
  protected editRoot(threadKey: string): void {
    const thread = this.findThread(threadKey);
    if (!thread) {
      return;
    }
    this.draft.set(thread.root.comment.body);
    this.threads.update((list) => list.filter((t) => t.key !== threadKey));
    this.rootComposer()?.focus();
  }

  protected isReplyOpen(threadKey: string): boolean {
    return this.openReplies().has(threadKey);
  }

  protected setReplyOpen(threadKey: string, open: boolean): void {
    this.openReplies.update((current) => {
      const next = new Set(current);
      if (open) {
        next.add(threadKey);
      } else {
        next.delete(threadKey);
      }
      return next;
    });
  }

  private sendRoot(threadKey: string): void {
    const thread = this.findThread(threadKey);
    if (!thread || thread.root.state === 'sent') {
      return;
    }
    this.updateThread(threadKey, (t) => ({ ...t, root: { ...t.root, state: 'sending', error: null } }));
    const version = this.contextVersion;
    const pending = thread.root.comment;
    this.api
      // Optimistic kayda yazılmış worksheet/soru kullanılır — bileşenin güncel questionId'si değil.
      .create(pending.worksheetId, { questionId: pending.questionId, parentCommentId: null, body: pending.body })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (comment) => {
          if (version !== this.contextVersion) {
            return;
          }
          this.announce('thread.announceSent');
          this.updateThread(threadKey, (t) => ({
            ...t,
            root: { ...t.root, comment: this.withFallbacks(comment, t.root.comment), state: 'sent', error: null },
            // Kök yazabilen, kendi köküne cevap yazabilir (backend her POST'ta yeniden doğrular).
            canReply: this.canWrite(),
          }));
        },
        error: (err: unknown) => {
          if (version !== this.contextVersion) {
            return;
          }
          this.updateThread(threadKey, (t) => ({
            ...t,
            root: { ...t.root, state: 'failed', error: commentErrorMessage(err, this.t, 'errors.sendFailed') },
          }));
        },
      });
  }

  private sendReply(threadKey: string, replyKey: string): void {
    const thread = this.findThread(threadKey);
    const reply = thread?.replies.find((r) => r.key === replyKey);
    if (!thread || !reply || reply.state === 'sent') {
      return;
    }
    const setReply = (fn: (r: CommentView) => CommentView, countDelta = 0) =>
      this.updateThread(threadKey, (t) => ({
        ...t,
        replyCount: t.replyCount + countDelta,
        replies: t.replies.map((r) => (r.key === replyKey ? fn(r) : r)),
      }));

    setReply((r) => ({ ...r, state: 'sending', error: null }));
    const version = this.contextVersion;
    const pending = reply.comment;
    this.api
      .create(pending.worksheetId, {
        questionId: pending.questionId,
        parentCommentId: pending.parentCommentId,
        body: pending.body,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (comment) => {
          if (version !== this.contextVersion) {
            return;
          }
          this.announce('thread.announceReplySent');
          setReply((r) => ({ ...r, comment: this.withFallbacks(comment, r.comment), state: 'sent', error: null }), 1);
        },
        error: (err: unknown) => {
          if (version !== this.contextVersion) {
            return;
          }
          setReply((r) => ({ ...r, state: 'failed', error: commentErrorMessage(err, this.t, 'errors.sendFailed') }));
        },
      });
  }

  /** Görsel olarak gizli live region'a duyuru; aynı metin art arda da okunsun diye sonuna görünmez fark eklenir. */
  private announce(key: string): void {
    const text = this.t(key);
    this.announcement.update((previous) => (previous === text ? `${text} ` : text));
  }

  private applyPageFlags(page: WorksheetCommentPage | null): void {
    this.canWrite.set(page?.canWrite === true);
    this.studentSummary.set(parseStudentCommentsSummary(page?.studentCommentsSummary));
    const reason = page?.lockReason ?? null;
    this.lockReason.set(reason && LOCK_REASONS.includes(reason) ? reason : null);
    this.nextCursor.set(page?.nextCursor || null);
  }

  /**
   * Issue #309: sayfa düzeyindeki `questionOrder`'ı (soru thread'inde sayfa boşken de dolu) yayınlar; worksheet
   * thread'inde ya da geçersiz değerde null. Aynı değer tekrar yayınlanmaz.
   */
  private emitQuestionOrder(value: unknown): void {
    const order = this.questionId() !== null ? toQuestionOrder(value) : null;
    if (order !== this.emittedQuestionOrder) {
      this.emittedQuestionOrder = order;
      this.questionOrderChange.emit(order);
    }
  }

  private mergeReplies(thread: ThreadView, page: WorksheetCommentRepliesPage): ThreadView {
    const sent = new Map<number, CommentView>();
    for (const reply of thread.replies) {
      if (reply.state === 'sent') {
        sent.set(reply.comment.id, reply);
      }
    }
    for (const item of page?.items ?? []) {
      if (!sent.has(item.id)) {
        sent.set(item.id, serverCommentView(item));
      }
    }
    const ordered = [...sent.values()].sort(compareComments);
    const unsent = thread.replies.filter((r) => r.state !== 'sent');
    return {
      ...thread,
      replies: [...ordered, ...unsent],
      replyCount: page?.replyCount ?? thread.replyCount,
      canReply: page?.canReply ?? thread.canReply,
      olderCursor: page?.nextCursor || null,
      olderStarted: true,
      loadingOlder: false,
      olderError: null,
    };
  }

  /**
   * Derin linkteki yorumu yüklü veride arar; yoksa kökün cevaplarını ya da sonraki kök sayfasını (en fazla
   * {@link COMMENT_HIGHLIGHT_MAX_PAGES}) yükleyip yeniden dener. Kök bulunup cevap bulunamazsa kökü vurgular.
   */
  private tryHighlight(): void {
    const target = this.pendingHighlight;
    if (!target) {
      return;
    }
    const threads = this.threads();
    const isTarget = (v: CommentView) => v.state === 'sent' && v.comment.id === target.commentId;
    const found = threads.find((t) => isTarget(t.root) || t.replies.some(isTarget));
    if (found) {
      this.pendingHighlight = null;
      this.applyHighlight(target.commentId, found);
      return;
    }

    const root = target.rootId
      ? threads.find((t) => t.root.state === 'sent' && t.root.comment.id === target.rootId)
      : undefined;
    if (root) {
      if (target.pagesLeft > 0 && canLoadOlderReplies(root)) {
        target.pagesLeft--;
        this.loadOlderReplies(root.key);
        return;
      }
      this.pendingHighlight = null;
      this.applyHighlight(root.root.comment.id, root);
      return;
    }

    if (target.pagesLeft > 0 && this.nextCursor()) {
      target.pagesLeft--;
      this.loadMore();
      return;
    }
    this.pendingHighlight = null;
  }

  private applyHighlight(commentId: number, thread: ThreadView): void {
    // Derin link bir kez tüketilir: aynı soruya geri dönülünce ya da "Tekrar dene"de yeniden uygulanmaz.
    const requested = this.highlightCommentId();
    if (requested) {
      this.consumedHighlights.add(requested);
    }
    this.highlightedId.set(commentId);
    const openReply = this.viewerIsTeacher() && thread.canReply;
    if (openReply) {
      this.setReplyOpen(thread.key, true);
    }
    if (!this.isBrowser) {
      return;
    }
    afterNextRender(
      () => {
        const element = this.host.nativeElement.querySelector<HTMLElement>(`[data-comment-id="${commentId}"]`);
        if (!element) {
          return;
        }
        const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
        element.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'center' });
        if (!openReply) {
          element.focus({ preventScroll: true });
        }
      },
      { injector: this.injector }
    );
    if (this.highlightTimer) {
      clearTimeout(this.highlightTimer);
    }
    this.highlightTimer = setTimeout(() => {
      this.highlightTimer = null;
      this.highlightedId.set(null);
    }, COMMENT_HIGHLIGHT_MS);
  }

  private pendingView(body: string, parentCommentId: number | null): CommentView {
    const sequence = ++this.tempSequence;
    return {
      key: `p${sequence}`,
      state: 'sending',
      error: null,
      comment: {
        id: -sequence,
        worksheetId: this.worksheetId(),
        questionId: this.questionId(),
        questionOrder: null,
        parentCommentId,
        authorDisplayName: '',
        authorRole: this.viewerIsTeacher() ? 'Teacher' : 'Student',
        isMine: true,
        body,
        createdAt: new Date().toISOString(),
      },
    };
  }

  /** Sunucu yanıtında beklenmedik boş alan olursa optimistic değeri koru. */
  private withFallbacks(comment: WorksheetComment | null, pending: WorksheetComment): WorksheetComment {
    return comment && typeof comment.id === 'number' ? { ...pending, ...comment } : pending;
  }

  private findThread(threadKey: string): ThreadView | undefined {
    return this.threads().find((t) => t.key === threadKey);
  }

  private updateThread(threadKey: string, fn: (thread: ThreadView) => ThreadView): void {
    this.threads.update((list) => list.map((t) => (t.key === threadKey ? fn(t) : t)));
  }

  /** `comments` scope'unda senkron çeviri (hata mesajları). */
  private readonly t = (key: string, params?: Record<string, unknown>): string =>
    this.transloco.translate<string>(`${COMMENTS_SCOPE}.${key}`, params) ?? key;
}

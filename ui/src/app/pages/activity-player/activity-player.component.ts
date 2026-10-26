import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, filter, firstValueFrom, map } from 'rxjs';

import {
  ACTIVITY_ERROR_CODES,
  ActivityFeedbackRow,
  ActivitySolution,
  WorksheetKind,
  activityErrorCode,
  activityErrorMessage,
  activityRoute,
  parseActivityFeedback,
} from '../../models/activity';
import { AssignedWorksheet } from '../../models/assignment';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import { TestInstance, TestInstanceQuestion, TestStatus } from '../../models/test-instance';
import { ActivityService } from '../../services/activity.service';
import { TestService } from '../../services/test.service';
import {
  ActivityCheckPanelComponent,
  ActivityCheckSummary,
  ActivityPanelError,
  ActivityResultRow,
} from '../../shared/components/activity-check-panel/activity-check-panel.component';
import {
  ActivityPlayerFrameComponent,
  ActivityStep,
  activityStepLabel,
} from '../../shared/components/activity-player-frame/activity-player-frame.component';
import { ActivitySummaryComponent } from '../../shared/components/activity-summary/activity-summary.component';
import {
  DragDropLabelingPlanV1,
  QuestionCanvasDragDropLabelingComponent,
} from '../../shared/components/question-canvas-dragdrop-labeling/question-canvas-dragdrop-labeling.component';
import { QuestionCanvasViewComponentv5 } from '../../shared/components/question-canvas-view-v5/question-canvas-view-v5.component';
import { createStorageImageResolver } from '../../shared/utils/storage-image-refresh.util';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';

/** Sayfa çevirileri kendi scope'unda: `public/i18n/activity/<lang>.json`. */
export const ACTIVITY_SCOPE = 'activity';

const DRAG_DROP = 'dragDropLabeling';

interface ItemAnswer {
  selectedAnswerId: number | null;
  answerPayload: string | null;
}

interface ItemResult {
  isCorrect: boolean;
  correctCount: number;
  totalCount: number;
  rows: ActivityFeedbackRow[];
}

type LoadState = 'loading' | 'error' | 'ready';

/** Panelde hata gösterilen adım — "Tekrar dene" aynı adımı yeniden dener. */
export type ActivityAction = 'check' | 'reveal' | 'finish';

/**
 * Issue #573: etkinlik sayfası oynatıcısı (`/activity/:testInstanceId`). Madde madde ilerleme, her değişiklikte mevcut
 * `save-answer`, "Cevapları kontrol et" (`POST test-instances/{id}/check`, tüm maddeler), satır bazlı geri bildirim,
 * "Doğru cevapları göster" (`GET .../solutions`) ve "Etkinliği bitir" (mevcut `end-test`; en az bir kontrol şart).
 * Puan, yüzde, XP, süre sayacı yok. Test oturumu (kind=Test) bu sayfaya gelirse `/testsolve`'a yönlendirilir.
 *
 * OnPush: tüm durum signal; çocuk görünümler (drag-drop, MCQ) klasik girdileriyle beslenir.
 */
@Component({
  selector: 'app-activity-player',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    ActivityPlayerFrameComponent,
    ActivityCheckPanelComponent,
    ActivitySummaryComponent,
    QuestionCanvasDragDropLabelingComponent,
    QuestionCanvasViewComponentv5,
    QuestionLiteViewComponent,
  ],
  templateUrl: './activity-player.component.html',
  styleUrl: './activity-player.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideTranslocoScope(ACTIVITY_SCOPE)],
})
export class ActivityPlayerComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly testService = inject(TestService);
  private readonly activityService = inject(ActivityService);
  private readonly transloco = inject(TranslocoService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  private readonly contentRef = viewChild<ElementRef<HTMLElement>>('itemContent');

  readonly instanceId = signal(0);
  readonly loadState = signal<LoadState>('loading');
  readonly instance = signal<TestInstance | null>(null);
  readonly items = signal<TestInstanceQuestion[]>([]);
  readonly regions = signal<QuestionRegion[]>([]);
  readonly currentIndex = signal(0);

  readonly answers = signal<Record<number, ItemAnswer>>({});
  readonly results = signal<Record<number, ItemResult>>({});
  readonly solutions = signal<Record<number, ActivitySolution> | null>(null);
  readonly checkCount = signal(0);
  readonly summary = signal<ActivityCheckSummary | null>(null);
  readonly revealed = signal(false);

  readonly checking = signal(false);
  readonly revealing = signal(false);
  readonly finishing = signal(false);
  readonly panelError = signal<ActivityPanelError | null>(null);
  readonly panelErrorMessage = signal<string | null>(null);
  /** Hatası gösterilen adım (W2: "Tekrar dene" bunu yeniden dener). */
  readonly failedAction = signal<ActivityAction | null>(null);
  readonly saveFailed = signal(false);

  /** Kontrol/bitirme sürerken içerik salt okunur (gönderilen cevap ile ekran ayrışmasın). */
  readonly contentLocked = computed(() => this.checking() || this.finishing());
  /** Ağ hatası metni "cevapların kayıtlı" diyebilir mi (kayıt hatası yoksa ve hata bitirmeden gelmediyse). */
  readonly answersSaved = computed(() => !this.saveFailed() && this.failedAction() !== 'finish');

  /**
   * S2: `computed` içindeki senkron çeviriler dil değişince / scope sözlüğü yüklenince yeniden hesaplansın.
   */
  private readonly i18nTick = toSignal(
    this.transloco.events$.pipe(
      filter((e) => e.type === 'langChanged' || e.type === 'translationLoadSuccess'),
      map(() => Date.now())
    ),
    { initialValue: 0 }
  );

  readonly stepLabel = activityStepLabel;

  readonly finished = signal(false);
  readonly nextActivity = signal<AssignedWorksheet | null>(null);
  readonly nextBusy = signal(false);

  /** issue #365 (S3): imzalı görsel URL'si dolarsa oturum yeniden çekilir (salt okuma). */
  readonly imageRefresh = createStorageImageResolver(
    () => this.testService.getCanvasTestWithAnswers(this.instanceId()),
    { cacheKey: () => this.instanceId() }
  );

  /** Madde başına harcanan süre (sn) — mevcut test ekranı gibi `timeTaken` olarak gönderilir; ekranda sayaç yok. */
  private readonly timeSpent = new Map<number, number>();
  private itemStartedAt = Date.now();
  /** W3: madde başına tek uçuşta kayıt; uçuşta yeni değişiklik olursa yalnız son değer sonra gönderilir. */
  private readonly inFlight = new Map<number, Promise<void>>();
  private readonly queued = new Set<number>();
  /** Kaydı başarısız maddeler (bitirmeden önce yeniden gönderilir). */
  private readonly failedItems = new Set<number>();
  private nextActivitySub: Subscription | null = null;
  private readonly plans = new Map<number, DragDropLabelingPlanV1 | null>();

  readonly title = computed(() => this.instance()?.testName ?? '');
  readonly currentItem = computed(() => this.items()[this.currentIndex()] ?? null);
  readonly currentRegion = computed(() => this.regions()[this.currentIndex()] ?? null);

  /** Geçerli maddenin yönergesi (kapsam yönergesi) yoksa sayfa yönergesi (açıklama). */
  readonly instruction = computed(() => {
    const own = this.currentItem()?.question?.passage?.instruction?.trim();
    return own || this.instance()?.worksheetDescription?.trim() || null;
  });

  readonly steps = computed<ActivityStep[]>(() => {
    const results = this.results();
    const answers = this.answers();
    return this.items().map((item) => {
      const result = results[item.id];
      if (result) return { state: result.isCorrect ? 'correct' : 'wrong' };
      return { state: this.hasAnswer(item, answers[item.id]) ? 'answered' : 'empty' };
    });
  });

  /** Boş yerleştirmesi olan maddelerin etiketleri (engellemeyen uyarı). */
  readonly blankItems = computed(() => {
    const answers = this.answers();
    return this.items()
      .map((item, index) => (this.isIncomplete(item, answers[item.id]) ? activityStepLabel(index) : null))
      .filter((label): label is string => label !== null);
  });

  /** Kontrolden sonra en az bir madde değişti (sonucu düştü). */
  readonly stale = computed(() => {
    if (!this.summary()) return false;
    const results = this.results();
    return this.items().some((item) => !results[item.id]);
  });

  readonly currentFeedbackRows = computed<ActivityFeedbackRow[] | null>(() => {
    const item = this.currentItem();
    if (!item) return null;
    return this.results()[item.id]?.rows ?? null;
  });

  readonly currentSolution = computed(() => {
    const item = this.currentItem();
    return item ? this.solutions()?.[item.id] ?? null : null;
  });

  /** Panelde geçerli maddenin satırları (kontrol sonucu ve/veya çözüm). */
  readonly currentRows = computed<ActivityResultRow[]>(() => {
    this.i18nTick();
    const item = this.currentItem();
    if (!item) return [];
    const result = this.results()[item.id];
    const revealed = this.revealed();
    if (!result && !revealed) return [];
    const solution = revealed ? this.solutions()?.[item.id] ?? null : null;
    return this.isDragDrop(item)
      ? this.dragDropRows(item, result, solution)
      : this.mcqRows(item, result, solution);
  });

  /** MCQ (canvas) görünümü için öğrencinin seçimi ve — çözüm açıkken — doğru şık. */
  readonly currentSelectedChoice = computed<AnswerChoice | undefined>(() => {
    const item = this.currentItem();
    const region = this.currentRegion();
    if (!item || !region) return undefined;
    const selected = this.answers()[item.id]?.selectedAnswerId;
    return region.answers.find((a) => a.id === selected);
  });

  readonly currentCorrectChoice = computed<AnswerChoice | undefined>(() => {
    const region = this.currentRegion();
    const solution = this.currentSolution();
    if (!region || !solution || !this.revealed()) return undefined;
    return region.answers.find((a) => a.id === solution.correctAnswerId);
  });

  constructor() {
    this.transloco.selectTranslate('state.loading', {}, ACTIVITY_SCOPE).pipe(takeUntilDestroyed()).subscribe();

    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const id = Number(params.get('testInstanceId'));
      if (!Number.isFinite(id) || id <= 0) {
        void this.router.navigate(['/tests'], { queryParams: { type: 'activities' }, replaceUrl: true });
        return;
      }
      this.instanceId.set(id);
      void this.load();
    });
  }

  // ---------------------------------------------------------------------------
  // Yükleme
  // ---------------------------------------------------------------------------

  async load(): Promise<void> {
    this.resetState();
    this.loadState.set('loading');
    let instance: TestInstance | null;
    try {
      instance = await firstValueFrom(this.testService.getCanvasTestWithAnswers(this.instanceId()));
    } catch {
      this.loadState.set('error');
      return;
    }
    if (!instance || !Array.isArray(instance.testInstanceQuestions)) {
      this.loadState.set('error');
      return;
    }
    if ((instance.kind ?? WorksheetKind.Test) !== WorksheetKind.Activity) {
      void this.router.navigate(['/testsolve', instance.id], { replaceUrl: true });
      return;
    }

    const regions = this.testService.convertTestInstanceToRegions(instance); // soruları `order`'a göre sıralar
    const items = [...instance.testInstanceQuestions];
    const answers: Record<number, ItemAnswer> = {};
    const results: Record<number, ItemResult> = {};
    for (const item of items) {
      answers[item.id] = {
        selectedAnswerId: item.selectedAnswerId || null,
        answerPayload: item.answerPayload?.trim() ? item.answerPayload : null,
      };
      if (item.isCorrect !== null && item.isCorrect !== undefined) {
        results[item.id] = {
          isCorrect: item.isCorrect,
          correctCount: item.correctCount ?? 0,
          totalCount: item.totalCount ?? 0,
          rows: parseActivityFeedback(item.feedback),
        };
      }
      this.timeSpent.set(item.id, item.timeTaken || 0);
    }

    this.instance.set(instance);
    this.items.set(items);
    this.regions.set(regions);
    this.answers.set(answers);
    this.results.set(results);
    this.checkCount.set(instance.checkCount ?? 0);
    this.finished.set(instance.status !== TestStatus.Started);
    this.summary.set(this.summaryFrom(items, results) ?? this.firstCheckSummary(instance));
    const firstEmpty = items.findIndex((item) => !this.hasAnswer(item, answers[item.id]));
    this.currentIndex.set(firstEmpty >= 0 ? firstEmpty : 0);
    this.itemStartedAt = Date.now();
    this.loadState.set('ready');

    if (this.finished()) {
      this.loadNextActivity();
    }
  }

  private resetState(): void {
    this.nextActivitySub?.unsubscribe();
    this.nextActivitySub = null;
    this.inFlight.clear();
    this.queued.clear();
    this.failedItems.clear();
    this.failedAction.set(null);
    this.instance.set(null);
    this.items.set([]);
    this.regions.set([]);
    this.answers.set({});
    this.results.set({});
    this.solutions.set(null);
    this.summary.set(null);
    this.checkCount.set(0);
    this.revealed.set(false);
    this.finished.set(false);
    this.panelError.set(null);
    this.panelErrorMessage.set(null);
    this.saveFailed.set(false);
    this.nextActivity.set(null);
    this.timeSpent.clear();
    this.plans.clear();
  }

  // ---------------------------------------------------------------------------
  // Gezinme
  // ---------------------------------------------------------------------------

  goTo(index: number): void {
    if (index < 0 || index >= this.items().length || index === this.currentIndex()) return;
    this.accumulateTime();
    this.currentIndex.set(index);
  }

  /** "Yanlışları düzelt": ilk yanlış maddeye geçer ve içeriğe odaklar. */
  focusFirstWrong(): void {
    const results = this.results();
    const index = this.items().findIndex((item) => results[item.id]?.isCorrect === false);
    if (index < 0) return;
    this.goTo(index);
    queueMicrotask(() => this.contentRef()?.nativeElement.focus());
  }

  private accumulateTime(): void {
    const item = this.currentItem();
    if (!item) return;
    const now = Date.now();
    const seconds = Math.max(0, Math.round((now - this.itemStartedAt) / 1000));
    this.timeSpent.set(item.id, (this.timeSpent.get(item.id) ?? 0) + seconds);
    this.itemStartedAt = now;
  }

  // ---------------------------------------------------------------------------
  // Cevaplar
  // ---------------------------------------------------------------------------

  onDragDropChange(payload: string, item: TestInstanceQuestion): void {
    if (this.revealed() || this.finished() || this.contentLocked()) return;
    this.updateAnswer(item, { selectedAnswerId: null, answerPayload: payload });
  }

  onChoiceSelected(answerId: number, item: TestInstanceQuestion): void {
    if (this.revealed() || this.finished() || this.contentLocked() || !answerId) return;
    this.updateAnswer(item, { selectedAnswerId: answerId, answerPayload: null });
  }

  private updateAnswer(item: TestInstanceQuestion, answer: ItemAnswer): void {
    const previous = this.answers()[item.id];
    if (previous && previous.selectedAnswerId === answer.selectedAnswerId && previous.answerPayload === answer.answerPayload) {
      return;
    }
    this.answers.update((all) => ({ ...all, [item.id]: answer }));
    // Değişen maddenin kontrol sonucu artık geçerli değil (backend de yeniden yüklemede döndürmez).
    if (this.results()[item.id]) {
      this.results.update((all) => {
        const next = { ...all };
        delete next[item.id];
        return next;
      });
    }
    this.persist(item);
  }

  /** W3: uçuşta kayıt varsa yalnız sıraya işaretler; uçuş bitince son değer gönderilir (sıra güvenliği). */
  private persist(item: TestInstanceQuestion): void {
    if (this.inFlight.has(item.id)) {
      this.queued.add(item.id);
      return;
    }
    this.send(item);
  }

  private send(item: TestInstanceQuestion): void {
    this.accumulateTime();
    const answer = this.answers()[item.id];
    const save = firstValueFrom(
      this.testService.saveAnswer({
        testInstanceId: this.instanceId(),
        testQuestionId: item.id,
        selectedAnswerId: answer?.selectedAnswerId ?? 0,
        answerPayload: answer?.answerPayload ?? undefined,
        timeTaken: this.timeSpent.get(item.id) ?? 0,
      })
    ).then(
      (response) => {
        if (response?.success === false) {
          this.onSaveRejected(item.id, response);
        } else {
          this.failedItems.delete(item.id);
        }
      },
      (error: unknown) => this.onSaveRejected(item.id, error)
    );
    const tracked = save.finally(() => {
      this.inFlight.delete(item.id);
      this.saveFailed.set(this.failedItems.size > 0);
      if (this.queued.delete(item.id) && !this.finished()) {
        this.send(item);
      }
    });
    this.inFlight.set(item.id, tracked);
  }

  private onSaveRejected(itemId: number, source: unknown): void {
    if (activityErrorCode(source) === ACTIVITY_ERROR_CODES.testNotInProgress) {
      this.queued.delete(itemId);
      this.showFinishedElsewhere();
      return;
    }
    // Kontrol de cevapları kaydeder; başarısız kayıt kontrolde ya da bitirmeden önce yeniden gider.
    this.failedItems.add(itemId);
  }

  /** Uçuştaki ve sıradaki tüm kayıtlar bitene kadar bekler. */
  private async flushSaves(): Promise<void> {
    while (this.inFlight.size > 0) {
      await Promise.all(Array.from(this.inFlight.values()));
    }
  }

  private clearPanelError(): void {
    this.panelError.set(null);
    this.panelErrorMessage.set(null);
    this.failedAction.set(null);
  }

  /** W2: panelin "Tekrar dene"si hatanın geldiği adımı yeniden dener. */
  retryFailed(): void {
    switch (this.failedAction()) {
      case 'reveal':
        void this.reveal();
        break;
      case 'finish':
        void this.finish();
        break;
      default:
        void this.runCheck();
    }
  }

  // ---------------------------------------------------------------------------
  // Kontrol / çözüm / bitir
  // ---------------------------------------------------------------------------

  async runCheck(): Promise<void> {
    if (this.checking() || this.finished()) return;
    this.checking.set(true);
    this.clearPanelError();
    await this.flushSaves();

    const items = this.items();
    const answers = this.answers();
    const sent = new Map(items.map((item) => [item.id, this.signature(answers[item.id])]));
    const body = items.map((item) => {
      const answer = answers[item.id];
      return this.isDragDrop(item)
        ? { instanceQuestionId: item.id, selectedAnswerId: null, answerPayload: answer?.answerPayload ?? null }
        : { instanceQuestionId: item.id, selectedAnswerId: answer?.selectedAnswerId ?? null, answerPayload: null };
    });

    try {
      const response = await firstValueFrom(this.activityService.check(this.instanceId(), body));
      if (response?.success === false) {
        this.handleActionError(response, 'check');
        return;
      }
      const current = this.answers();
      const next: Record<number, ItemResult> = {};
      for (const question of response.questions ?? []) {
        // Kontrol uçuştayken değişen maddenin sonucu uygulanmaz.
        if (sent.get(question.instanceQuestionId) !== this.signature(current[question.instanceQuestionId])) continue;
        next[question.instanceQuestionId] = {
          isCorrect: question.isCorrect,
          correctCount: question.correctCount,
          totalCount: question.totalCount,
          rows: parseActivityFeedback(question.feedback),
        };
      }
      this.results.set(next);
      this.checkCount.update((count) => count + 1);
      this.summary.set({ correct: response.correctCount, total: response.totalCount });
      // Kontrol gönderilen cevapları kaydetti: başarısız kayıtlar bu cevaplarla kapandı.
      items.forEach((item) => {
        if (sent.get(item.id) === this.signature(current[item.id])) this.failedItems.delete(item.id);
      });
      this.saveFailed.set(this.failedItems.size > 0);
    } catch (error) {
      this.handleActionError(error, 'check');
    } finally {
      this.checking.set(false);
      this.repersistChangedSince(sent);
    }
  }

  /**
   * CRITICAL 1: kontrol uçuştayken değişen madde (sunucuda kontrolün eski cevabıyla ezilmiş olabilir) yeniden kaydedilir.
   */
  private repersistChangedSince(sent: Map<number, string>): void {
    if (this.finished()) return;
    const current = this.answers();
    for (const item of this.items()) {
      if (sent.has(item.id) && sent.get(item.id) !== this.signature(current[item.id])) {
        this.persist(item);
      }
    }
  }

  async reveal(): Promise<void> {
    if (this.revealing() || this.checkCount() < 1) return;
    if (this.solutions()) {
      this.revealed.set(true);
      return;
    }
    this.revealing.set(true);
    this.clearPanelError();
    try {
      const response = await firstValueFrom(this.activityService.getSolutions(this.instanceId()));
      if (response?.success === false) {
        this.handleActionError(response, 'reveal');
        return;
      }
      const map: Record<number, ActivitySolution> = {};
      (response.questions ?? []).forEach((solution) => (map[solution.instanceQuestionId] = solution));
      this.solutions.set(map);
      this.revealed.set(true);
    } catch (error) {
      this.handleActionError(error, 'reveal');
    } finally {
      this.revealing.set(false);
    }
  }

  restoreOwnAnswer(): void {
    this.revealed.set(false);
  }

  async finish(): Promise<void> {
    if (this.finishing() || this.finished()) return;
    this.finishing.set(true);
    this.clearPanelError();
    this.accumulateTime();
    await this.flushSaves();
    // W1: başarısız kayıtlar bir kez daha gönderilir; yine başarısızsa end-test çağrılmaz.
    if (this.failedItems.size > 0) {
      const items = this.items().filter((item) => this.failedItems.has(item.id));
      items.forEach((item) => this.persist(item));
      await this.flushSaves();
      if (this.finished()) {
        this.finishing.set(false);
        return;
      }
      if (this.failedItems.size > 0) {
        this.failedAction.set('finish');
        this.panelError.set('network');
        this.finishing.set(false);
        return;
      }
    }
    try {
      const response = await firstValueFrom(this.testService.completeTest(this.instanceId()));
      if (response?.success === false) {
        this.handleActionError(response, 'finish');
        return;
      }
      this.markFinished();
    } catch (error) {
      this.handleActionError(error, 'finish');
    } finally {
      this.finishing.set(false);
    }
  }

  private markFinished(): void {
    this.revealed.set(false);
    this.finished.set(true);
    this.loadNextActivity();
  }

  private handleActionError(source: unknown, action: ActivityAction): void {
    this.failedAction.set(action);
    const code = activityErrorCode(source);
    const status = source instanceof HttpErrorResponse ? source.status : null;
    const message = activityErrorMessage(source);

    if (code === ACTIVITY_ERROR_CODES.notAnActivity) {
      void this.router.navigate(['/testsolve', this.instanceId()], { replaceUrl: true });
      return;
    }
    if (code === ACTIVITY_ERROR_CODES.testNotInProgress) {
      this.showFinishedElsewhere();
      return;
    }
    if (status === 429 || code === ACTIVITY_ERROR_CODES.rateLimited) {
      this.panelError.set('rateLimited');
      return;
    }
    if (code === ACTIVITY_ERROR_CODES.activityCheckRequired) {
      if (action === 'reveal') {
        this.panelError.set('checkRequired');
      } else {
        this.panelError.set('generic');
        this.panelErrorMessage.set(message ?? this.tr('finishCheckRequired'));
      }
      return;
    }
    if (status === 0) {
      this.panelError.set('network');
      return;
    }
    this.panelError.set('generic');
    this.panelErrorMessage.set(message);
  }

  /** Oturum başka yerde (başka sekme) bitmiş: özet görünür, bilgi verilir. */
  private showFinishedElsewhere(): void {
    if (this.finished()) return;
    this.snackBar.open(this.tr('alreadyFinished'), this.tr('dismiss'), { duration: 4000 });
    this.markFinished();
  }

  // ---------------------------------------------------------------------------
  // Etkinlik sonu
  // ---------------------------------------------------------------------------

  private loadNextActivity(): void {
    const worksheetId = this.instance()?.worksheetId;
    // S5: yeniden yüklemede önceki istek iptal edilir (eski oturumun sonucu yenisini ezmesin).
    this.nextActivitySub?.unsubscribe();
    this.nextActivitySub = this.testService
      .getActiveAssignments(WorksheetKind.Activity)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (assignments) => {
          const next = (assignments ?? []).find(
            (a) =>
              (a.kind ?? WorksheetKind.Activity) === WorksheetKind.Activity &&
              !a.isCompleted &&
              !a.isFinished &&
              a.worksheetId !== worksheetId
          );
          this.nextActivity.set(next ?? null);
        },
        error: () => this.nextActivity.set(null),
      });
  }

  async openNextActivity(): Promise<void> {
    const next = this.nextActivity();
    if (!next || this.nextBusy()) return;
    this.nextBusy.set(true);
    try {
      const response = await firstValueFrom(this.testService.startTest(next.worksheetId));
      if (response?.success && response.instanceId) {
        await this.router.navigate(activityRoute(response.instanceId));
        return;
      }
      this.snackBar.open(response?.message || this.tr('nextFailed'), this.tr('dismiss'), { duration: 4000 });
    } catch (error) {
      this.snackBar.open(activityErrorMessage(error) ?? this.tr('nextFailed'), this.tr('dismiss'), { duration: 4000 });
    } finally {
      this.nextBusy.set(false);
    }
  }

  backToList(): void {
    void this.router.navigate(['/tests'], { queryParams: { type: 'activities' } });
  }

  // ---------------------------------------------------------------------------
  // Yardımcılar
  // ---------------------------------------------------------------------------

  isDragDrop(item: TestInstanceQuestion): boolean {
    return (item.question?.interactionType || 'mcq') === DRAG_DROP;
  }

  answerFor(item: TestInstanceQuestion): ItemAnswer | undefined {
    return this.answers()[item.id];
  }

  private signature(answer: ItemAnswer | undefined): string {
    return `${answer?.selectedAnswerId ?? ''}|${answer?.answerPayload ?? ''}`;
  }

  private planFor(item: TestInstanceQuestion): DragDropLabelingPlanV1 | null {
    if (!this.plans.has(item.id)) {
      let plan: DragDropLabelingPlanV1 | null = null;
      try {
        const parsed = JSON.parse(item.question?.interactionPlan || 'null') as DragDropLabelingPlanV1 | null;
        plan = parsed && Array.isArray(parsed.dropZones) && Array.isArray(parsed.draggables) ? parsed : null;
      } catch {
        plan = null;
      }
      this.plans.set(item.id, plan);
    }
    return this.plans.get(item.id) ?? null;
  }

  private placementsOf(payload: string | null | undefined): Array<{ dropZoneId: string; draggableId: string }> {
    if (!payload) return [];
    try {
      const parsed = JSON.parse(payload) as { placements?: Array<{ dropZoneId: string; draggableId: string }> } | null;
      return Array.isArray(parsed?.placements) ? parsed!.placements.filter((p) => !!p?.dropZoneId && !!p?.draggableId) : [];
    } catch {
      return [];
    }
  }

  private hasAnswer(item: TestInstanceQuestion, answer: ItemAnswer | undefined): boolean {
    if (this.isDragDrop(item)) return this.placementsOf(answer?.answerPayload).length > 0;
    return !!answer?.selectedAnswerId;
  }

  /** Boş satırı olan madde: MCQ seçilmemiş ya da hedeflerin bir kısmı boş. */
  private isIncomplete(item: TestInstanceQuestion, answer: ItemAnswer | undefined): boolean {
    if (!this.isDragDrop(item)) return !answer?.selectedAnswerId;
    const zones = this.planFor(item)?.dropZones.length ?? 0;
    return this.placementsOf(answer?.answerPayload).length < zones;
  }

  /**
   * S6: yeniden yüklemede özet, kontrol sonrası değişmemiş satırlardan (bir kısmı değiştiyse yalnız onlardan) hesaplanır;
   * değişen maddeler `stale` notuyla belirtilir.
   */
  /**
   * S6: bitmiş etkinlikte satır verisi yoksa bilgi satırı oturumun ilk kontrol sayılarından (yalnız bilgi, puan değil).
   * Devam eden etkinlikte kullanılmaz — orada özet güncel kontrol satırlarını yansıtır.
   */
  private firstCheckSummary(instance: TestInstance): ActivityCheckSummary | null {
    if (!this.finished() || instance.firstCheckCorrectCount == null || instance.firstCheckTotalCount == null) {
      return null;
    }
    return { correct: instance.firstCheckCorrectCount, total: instance.firstCheckTotalCount };
  }

  private summaryFrom(items: TestInstanceQuestion[], results: Record<number, ItemResult>): ActivityCheckSummary | null {
    const checked = items.filter((item) => !!results[item.id]);
    if (checked.length === 0) return null;
    return checked.reduce(
      (acc, item) => ({ correct: acc.correct + results[item.id].correctCount, total: acc.total + results[item.id].totalCount }),
      { correct: 0, total: 0 }
    );
  }

  private dragDropRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.planFor(item);
    if (!plan) return [];
    const labels = new Map(plan.draggables.map((d) => [d.id, d.label]));
    const placed = new Map(this.placementsOf(this.answers()[item.id]?.answerPayload).map((p) => [p.dropZoneId, p.draggableId]));
    const solved = new Map(this.placementsOf(solution?.interactionSolution).map((p) => [p.dropZoneId, p.draggableId]));
    const marks = new Map((result?.rows ?? []).map((row) => [row.id, row.correct]));
    return plan.dropZones.map((zone, index) => {
      const own = placed.get(zone.id);
      const correctId = solved.get(zone.id);
      return {
        label: this.tr('rows.zone', { n: index + 1, content: own ? labels.get(own) ?? own : this.tr('rows.empty') }),
        correct: marks.has(zone.id) ? marks.get(zone.id)! : null,
        solution: solution ? (correctId ? labels.get(correctId) ?? correctId : this.tr('rows.empty')) : null,
      };
    });
  }

  private mcqRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const answers = item.question?.answers ?? [];
    const labelOf = (id: number | null | undefined): string => {
      const index = answers.findIndex((a) => a.id === id);
      if (index < 0) return this.tr('rows.empty');
      const text = (answers[index].text ?? '').replace(/<[^>]*>/g, '').trim();
      const letter = String.fromCharCode(65 + index);
      return text && text !== letter ? `${letter}) ${text}` : letter;
    };
    return [
      {
        label: this.tr('rows.choice', { content: labelOf(this.answers()[item.id]?.selectedAnswerId) }),
        correct: result ? result.isCorrect : null,
        solution: solution ? labelOf(solution.correctAnswerId) : null,
      },
    ];
  }

  private tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ACTIVITY_SCOPE}.${key}`, params) ?? '';
  }
}

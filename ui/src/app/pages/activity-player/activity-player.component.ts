import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  NgZone,
  PLATFORM_ID,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DOCUMENT, isPlatformBrowser } from '@angular/common';
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
import { ActivityProgressView, ActivityTodayStats, toActivityProgressView } from '../../models/activity-summary.model';
import { AssignedWorksheet } from '../../models/assignment';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import {
  MatrixChoicePlanV1,
  isMatrixChoiceType,
  parseMatrixChoicePlan,
  parseMatrixChoiceSelections,
  parseMatrixChoiceSolution,
  selectionOf,
} from '../../models/matrix-choice';
import {
  SlotFillPlanV1,
  isSlotFillType,
  parseSlotFillCipherFeedback,
  parseSlotFillPlan,
  parseSlotFillSolution,
  parseSlotFillValues,
  solutionDisplay,
  solutionOf,
  valueDisplay,
  valueOf,
} from '../../models/slot-fill';
import {
  DragPlacePlanV1,
  cardsIn,
  isDragPlaceType,
  parseDragPlacePlacements,
  parseDragPlacePlan,
  parseDragPlaceSolution,
  placementCount,
} from '../../models/drag-place';
import { MatchPlanV1, isMatchType, openLefts, parseMatchPairs, parseMatchPlan, parseMatchSolution, rightOf } from '../../models/match';
import {
  CrosswordFeedback,
  CrosswordPlanV1,
  clueNumbers,
  clueWord,
  isCrosswordType,
  isEmptyCrosswordGrid,
  parseCrosswordAnswer,
  parseCrosswordFeedback,
  parseCrosswordPlan,
  parseCrosswordSolution,
} from '../../models/crossword';
import {
  LogicGridFeedback,
  LogicGridPlanV1,
  countMarks,
  isLogicGridType,
  pairKey,
  parseLogicGridAnswer,
  parseLogicGridFeedback,
  parseLogicGridPlan,
  parseLogicGridSolution,
  ticks,
} from '../../models/logic-grid';
import {
  RegionPaintFeedback,
  RegionPaintPlanV1,
  countPainted,
  isEmptyPaint,
  isRegionPaintType,
  parseRegionPaintAnswer,
  parseRegionPaintFeedback,
  parseRegionPaintPlan,
  parseRegionPaintSolution,
} from '../../models/region-paint';
import {
  WordSearchFeedback,
  WordSearchPlanV1,
  isWordSearchType,
  parseWordSearchAnswer,
  parseWordSearchFeedback,
  parseWordSearchPlan,
} from '../../models/word-search';
import {
  MazeFeedback,
  MazeTreePlanV1,
  edgeOf,
  edgeTarget,
  isEmptyMazeAnswer,
  isMazeTreeType,
  mazeRoute,
  parseMazeAnswer,
  parseMazeFeedback,
  parseMazeSolution,
  parseMazeTreePlan,
  walkMaze,
} from '../../models/maze-tree';
import {
  TextSelectFeedback,
  TextSelectPlanV1,
  gapAcceptOf,
  isEmptyTextSelectAnswer,
  isTextSelectType,
  parseTextSelectAnswer,
  parseTextSelectPlan,
  parseTextSelectSolution,
  parseTextSelectFeedback,
  textSelectFeedbackFromCounts,
  textValueOf,
} from '../../models/text-select';
import {
  OpenTextStudentReview,
  countOpenTextWords,
  isBlankOpenText,
  isOpenTextType,
  openTextRowStatus,
  parseOpenTextAnswer,
  parseOpenTextFeedbackStatus,
  parseOpenTextStudentReview,
} from '../../models/open-text';
import { TestInstance, TestInstanceQuestion, TestStatus } from '../../models/test-instance';
import { seededShuffle } from '../../shared/utils/seeded-shuffle';
import { ActivityService } from '../../services/activity.service';
import { ActivityLauncherService } from '../../services/activity-launcher.service';
import { BadgeService } from '../../services/badge.service';
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
import {
  ActivitySummaryComponent,
  ActivitySummaryOpenTextItem,
} from '../../shared/components/activity-summary/activity-summary.component';
import {
  DragDropLabelingPlanV1,
  QuestionCanvasDragDropLabelingComponent,
} from '../../shared/components/question-canvas-dragdrop-labeling/question-canvas-dragdrop-labeling.component';
import { QuestionCanvasMatrixChoiceComponent } from '../../shared/components/question-canvas-matrix-choice/question-canvas-matrix-choice.component';
import { QuestionCanvasSlotFillComponent } from '../../shared/components/question-canvas-slot-fill/question-canvas-slot-fill.component';
import { QuestionCanvasDragPlaceComponent } from '../../shared/components/question-canvas-drag-place/question-canvas-drag-place.component';
import { QuestionCanvasMatchComponent } from '../../shared/components/question-canvas-match/question-canvas-match.component';
import { QuestionCanvasTextSelectComponent } from '../../shared/components/question-canvas-text-select/question-canvas-text-select.component';
import { QuestionCanvasOpenTextComponent } from '../../shared/components/question-canvas-open-text/question-canvas-open-text.component';
import { QuestionCanvasCrosswordComponent } from '../../shared/components/question-canvas-crossword/question-canvas-crossword.component';
import { QuestionCanvasLogicGridComponent } from '../../shared/components/question-canvas-logic-grid/question-canvas-logic-grid.component';
import { QuestionCanvasRegionPaintComponent } from '../../shared/components/question-canvas-region-paint/question-canvas-region-paint.component';
import { QuestionCanvasWordSearchComponent } from '../../shared/components/question-canvas-word-search/question-canvas-word-search.component';
import { QuestionCanvasMazeTreeComponent } from '../../shared/components/question-canvas-maze-tree/question-canvas-maze-tree.component';
import { QuestionCanvasViewComponentv5 } from '../../shared/components/question-canvas-view-v5/question-canvas-view-v5.component';
import { ActiveTimeTracker, ACTIVITY_MAX_ACTIVE_SECONDS } from '../../shared/utils/active-time-tracker';
import { currentUserId } from '../../shared/utils/current-user-id.util';
import { createStorageImageResolver } from '../../shared/utils/storage-image-refresh.util';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';

/** Sayfa çevirileri kendi scope'unda: `public/i18n/activity/<lang>.json`. */
export const ACTIVITY_SCOPE = 'activity';

const DRAG_DROP = 'dragDropLabeling';
/** Sonuç panelinin daraltılabildiği dar ekran (#576 E2E; oynatıcıların dokun-dokun eşiğiyle aynı). */
const NARROW_QUERY = '(max-width: 640px)';

/** Issue #572: etkin süreyi sayan etkileşimler (oynatıcı içinde). */
const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'touchstart', 'wheel', 'scroll'] as const;
/**
 * Issue #572 / #575 E2E: bitirdikten sonra rozet işlemesi (outbox → RabbitMQ) asenkron; özet bitirmeden itibaren ~2, 5 ve
 * 10. saniyede yeniden okunur ve bugünkü etkinlik sayısı ya da etkin dakika artınca durur.
 */
export const ACTIVITY_SUMMARY_REFETCH_SCHEDULE_MS = [2000, 5000, 10000] as const;
/** İlk yeniden okuma gecikmesi (ms). */
export const ACTIVITY_SUMMARY_REFETCH_MS = ACTIVITY_SUMMARY_REFETCH_SCHEDULE_MS[0];
const ACTIVE_SECONDS_KEY = 'activity.activeSeconds:';

interface ItemAnswer {
  selectedAnswerId: number | null;
  answerPayload: string | null;
}

interface ItemResult {
  isCorrect: boolean;
  correctCount: number;
  totalCount: number;
  rows: ActivityFeedbackRow[];
  /** Ham `feedback` JSON'u (#582: textSelect sayıları ve `tooMany` buradan okunur). */
  feedback?: string | null;
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
    QuestionCanvasMatrixChoiceComponent,
    QuestionCanvasSlotFillComponent,
    QuestionCanvasTextSelectComponent,
    QuestionCanvasOpenTextComponent,
    QuestionCanvasDragPlaceComponent,
    QuestionCanvasMatchComponent,
    QuestionCanvasMazeTreeComponent,
    QuestionCanvasCrosswordComponent,
    QuestionCanvasWordSearchComponent,
    QuestionCanvasLogicGridComponent,
    QuestionCanvasRegionPaintComponent,
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
  private readonly activityLauncher = inject(ActivityLauncherService);
  private readonly transloco = inject(TranslocoService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly badgeService = inject(BadgeService);
  private readonly zone = inject(NgZone);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly contentRef = viewChild<ElementRef<HTMLElement>>('itemContent');
  /** Issue #575: cevap kutusu oynatıcısı (yazılıp henüz kaydedilmemiş değer kontrol / bitir öncesi alınır). */
  private readonly slotFillPlayer = viewChild(QuestionCanvasSlotFillComponent);
  /** Issue #582: metin işaretleme oynatıcısı (yazılıp henüz kaydedilmemiş düzeltme kontrol / bitir öncesi alınır). */
  private readonly textSelectPlayer = viewChild(QuestionCanvasTextSelectComponent);
  /** Issue #583: açık uçlu cevap oynatıcısı (yazılıp henüz kaydedilmemiş metin kontrol / bitir / madde değişimi öncesi alınır). */
  private readonly openTextPlayer = viewChild(QuestionCanvasOpenTextComponent);

  readonly instanceId = signal(0);
  readonly loadState = signal<LoadState>('loading');
  readonly instance = signal<TestInstance | null>(null);
  readonly items = signal<TestInstanceQuestion[]>([]);
  readonly regions = signal<QuestionRegion[]>([]);
  readonly currentIndex = signal(0);

  readonly answers = signal<Record<number, ItemAnswer>>({});
  readonly results = signal<Record<number, ItemResult>>({});
  /**
   * Issue #576: kontrolden sonra değişen sürükle-yerleştir / eşleştirme maddesinin son kontrol satırları. Sonuç (özet, adım
   * durumu) yine düşer; oynatıcı işaretleri satır id'siyle (`hedef:kart` / sol id) arar, böylece "Havuza geri al" sonrası
   * yalnız taşınan kartın işareti kalkar, diğer kartların ✓/✗'si kalır.
   */
  readonly retainedRows = signal<Record<number, ActivityFeedbackRow[]>>({});
  readonly solutions = signal<Record<number, ActivitySolution> | null>(null);
  /**
   * Issue #583: açık uçlu maddelerin değerlendirme durumu (madde id'si → sunucunun `openTextReview`'ü; yoksa null). Yüklemede ve
   * her kontrolde sunucudan gelir; metin değişince yerelde düşer (değerlendirilmişse "eskidi", değilse gönderilmemiş taslak).
   */
  readonly openTextReviews = signal<Record<number, OpenTextStudentReview | null>>({});
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
  /**
   * Issue #576 (E2E): dar ekranda (≤ 640px) kontrol paneli kontrolden sonra ekranın çoğunu kaplıyordu. "Yanlışları düzelt"e
   * basınca ya da öğrenci maddede bir şeye dokununca / cevabı değiştirince panel kompakt çubuğa iner; çubuktan yeniden
   * açılır. Yeni kontrol sonucu paneli yeniden açar.
   */
  readonly narrowScreen = signal(false);
  readonly panelCollapsed = signal(false);

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
  /**
   * Issue #609: oturum tamamlandı (süresi dolmadı) — özet ekranında "Yeniden çöz". Başka sekmede bitmiş (durumu bilinmeyen)
   * oturumda false; sayfa yeniden yüklenince gerçek durumdan gelir.
   */
  readonly canRetake = signal(false);
  readonly retakeBusy = computed(() => this.activityLauncher.retaking() !== null);
  readonly nextActivity = signal<AssignedWorksheet | null>(null);
  readonly nextBusy = signal(false);
  /** Issue #572: etkinlik sonundaki "Bugün" + rozet kartları; yüklenemezse null (kartlar gizli, hata gösterilmez). */
  readonly progress = signal<ActivityProgressView>({ today: null, badge: null });

  /**
   * Issue #572: etkinliğin etkin süresi (sn) — yalnız sekme görünürken ve son 2 dk içinde oynatıcıda etkileşim varken sayılır,
   * 1800 sn tavanlı; `end-test` gövdesinde `activeSeconds` olarak gider. Ekranda sayaç yok.
   */
  private readonly activeTime = new ActiveTimeTracker();
  /** Sayacın ait olduğu oturum (sonraki etkinliğe geçerken `instanceId` önce değişir). 0 = sayılan oturum yok. */
  private activeInstanceId = 0;
  private progressSub: Subscription | null = null;
  private progressRefetch: ReturnType<typeof setTimeout> | null = null;
  /** Bitirme sonrası özet izleme: karşılaştırma tabanı ve sıradaki yeniden okuma (null = izleme yok). */
  private progressWatch: { baseline: ActivityTodayStats | null; attempt: number } | null = null;
  /** Son okunan ham "Bugün" (görünüm 0 etkinlikte kartı gizler; karşılaştırma ham değerle yapılır). */
  private lastToday: ActivityTodayStats | null = null;

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
  private readonly matrixPlans = new Map<number, MatrixChoicePlanV1 | null>();
  private readonly slotFillPlans = new Map<number, SlotFillPlanV1 | null>();
  private readonly dragPlacePlans = new Map<number, DragPlacePlanV1 | null>();
  private readonly matchPlans = new Map<number, MatchPlanV1 | null>();
  private readonly textSelectPlans = new Map<number, TextSelectPlanV1 | null>();
  private readonly mazePlans = new Map<number, MazeTreePlanV1 | null>();
  private readonly crosswordPlans = new Map<number, CrosswordPlanV1 | null>();
  private readonly wordSearchPlans = new Map<number, WordSearchPlanV1 | null>();
  private readonly logicGridPlans = new Map<number, LogicGridPlanV1 | null>();
  private readonly regionPaintPlans = new Map<number, RegionPaintPlanV1 | null>();

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
      // Issue #583: açık uçlu madde doğru/yanlış sayılmaz (öğretmen değerlendirir): yalnız cevaplandı / boş.
      if (this.isOpenText(item)) return { state: this.hasAnswer(item, answers[item.id]) ? 'answered' : 'empty' };
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

  /** Issue #574: boş maddelerin hepsi sürükle-bırak ise "boş yerleştirme", değilse genel "boş bırakılan yer" metni. */
  readonly blankKind = computed<'placement' | 'generic'>(() => {
    const answers = this.answers();
    const blank = this.items().filter((item) => this.isIncomplete(item, answers[item.id]));
    return blank.length > 0 && blank.every((item) => this.isDragDrop(item)) ? 'placement' : 'generic';
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
    return this.results()[item.id]?.rows ?? this.retainedRows()[item.id] ?? null;
  });

  /**
   * Issue #582: metin işaretleme maddesinin kontrol sonucu — sunucunun `feedback`'i (satırlar, sayılar, `tooMany`); ayrıştırılamazsa
   * satırlardan ve kontrol sayılarından (bulunan = doğru sayısı, fazla = `extra` satırları, kalan = toplam − bulunan − fazla).
   */
  readonly currentTextSelectFeedback = computed<TextSelectFeedback | null>(() => {
    const item = this.currentItem();
    if (!item || this.revealed()) return null;
    const result = this.results()[item.id];
    if (!result) return null;
    return parseTextSelectFeedback(result.feedback) ?? textSelectFeedbackFromCounts(result.rows, result.correctCount, result.totalCount);
  });

  /** Issue #585: yol oyununun kontrol sonucu (durum + sapma düğümü); çözüm açıkken ya da kontrol yoksa null. */
  readonly currentMazeFeedback = computed<MazeFeedback | null>(() => {
    const item = this.currentItem();
    if (!item || this.revealed()) return null;
    return parseMazeFeedback(this.results()[item.id]?.feedback);
  });

  /** Issue #586: bulmacanın kontrol sonucu (ipucu başına doğru/yanlış); çözüm açıkken ya da kontrol yoksa null. */
  readonly currentCrosswordFeedback = computed<CrosswordFeedback | null>(() => {
    const item = this.currentItem();
    if (!item || this.revealed()) return null;
    return parseCrosswordFeedback(this.results()[item.id]?.feedback);
  });

  /** Issue #586: kelime avının kontrol sonucu (kelime başına bulundu mu + çizgi isabetleri); çözüm açıkken ya da kontrol yoksa null. */
  readonly currentWordSearchFeedback = computed<WordSearchFeedback | null>(() => {
    const item = this.currentItem();
    if (!item || this.revealed()) return null;
    return parseWordSearchFeedback(this.results()[item.id]?.feedback);
  });

  /** Issue #586: mantık tablosunun kontrol sonucu ((kişi, kategori) çifti başına); çözüm açıkken ya da kontrol yoksa null. */
  readonly currentLogicGridFeedback = computed<LogicGridFeedback | null>(() => {
    const item = this.currentItem();
    if (!item || this.revealed()) return null;
    return parseLogicGridFeedback(this.results()[item.id]?.feedback);
  });

  /** Issue #586: cevap kutusu şifresinin açılmış harfleri (hücre başına); çözüm açıkken ya da kontrol yoksa null. */
  readonly currentSlotCipher = computed<(string | null)[] | null>(() => {
    const item = this.currentItem();
    if (!item || this.revealed()) return null;
    return parseSlotFillCipherFeedback(this.results()[item.id]?.feedback);
  });

  /** Issue #587: boyamanın kontrol sonucu (renk başına); çözüm açıkken ya da kontrol yoksa null. */
  readonly currentRegionPaintFeedback = computed<RegionPaintFeedback | null>(() => {
    const item = this.currentItem();
    if (!item || this.revealed()) return null;
    return parseRegionPaintFeedback(this.results()[item.id]?.feedback);
  });

  /** Issue #583: geçerli açık uçlu maddenin değerlendirme durumu (yoksa null). */
  readonly currentOpenTextReview = computed<OpenTextStudentReview | null>(() => {
    const item = this.currentItem();
    return item ? this.openTextReviews()[item.id] ?? null : null;
  });

  /**
   * Issue #583: etkinlik sonu özetindeki açık uçlu maddeler (madde etiketi + değerlendirme). Puan / sayım değil: öğrenci
   * öğretmenin değerlendirmesini ve geri bildirimini burada görür.
   */
  readonly openTextSummary = computed<ActivitySummaryOpenTextItem[]>(() => {
    const reviews = this.openTextReviews();
    const answers = this.answers();
    return this.items()
      .map((item, index) => ({ item, index }))
      .filter(({ item }) => this.isOpenText(item))
      .map(({ item, index }) => ({
        label: activityStepLabel(index),
        answered: this.hasAnswer(item, answers[item.id]),
        review: reviews[item.id] ?? null,
      }));
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
    if (this.isMatrix(item)) return this.matrixRows(item, result, solution);
    if (this.isSlotFill(item)) return this.slotFillRows(item, result, solution);
    if (this.isDragPlace(item)) return this.dragPlaceRows(item, result, solution);
    if (this.isMatch(item)) return this.matchRows(item, result, solution);
    if (this.isTextSelect(item)) return this.textSelectRows(item, result, solution);
    if (this.isMazeTree(item)) return this.mazeRows(item, result, solution);
    if (this.isCrossword(item)) return this.crosswordRows(item, result, solution);
    if (this.isWordSearch(item)) return this.wordSearchRows(item, result);
    if (this.isLogicGrid(item)) return this.logicGridRows(item, result, solution);
    if (this.isRegionPaint(item)) return this.regionPaintRows(item, result, solution);
    if (this.isOpenText(item)) return this.openTextRows(item, result);
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
    if (this.isBrowser && typeof window.matchMedia === 'function') {
      const query = window.matchMedia(NARROW_QUERY);
      this.narrowScreen.set(query.matches);
      const listener = (event: MediaQueryListEvent) => this.narrowScreen.set(event.matches);
      query.addEventListener('change', listener);
      this.destroyRef.onDestroy(() => query.removeEventListener('change', listener));
    }
    this.transloco.selectTranslate('state.loading', {}, ACTIVITY_SCOPE).pipe(takeUntilDestroyed()).subscribe();
    this.listenForActivity();
    this.destroyRef.onDestroy(() => {
      this.persistActiveSeconds();
      this.clearProgressRefetch();
    });

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
    const reviews: Record<number, OpenTextStudentReview | null> = {};
    for (const item of items) {
      if (this.isOpenText(item)) reviews[item.id] = parseOpenTextStudentReview(item.openTextReview);
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
          feedback: item.feedback ?? null,
        };
      }
      this.timeSpent.set(item.id, item.timeTaken || 0);
    }

    this.instance.set(instance);
    this.items.set(items);
    this.regions.set(regions);
    this.answers.set(answers);
    this.results.set(results);
    this.openTextReviews.set(reviews);
    this.retainedRows.set({});
    this.checkCount.set(instance.checkCount ?? 0);
    this.finished.set(instance.status !== TestStatus.Started);
    this.canRetake.set(instance.status === TestStatus.Completed);
    this.summary.set(this.summaryFrom(items, results) ?? this.firstCheckSummary(instance));
    const firstEmpty = items.findIndex((item) => !this.hasAnswer(item, answers[item.id]));
    this.currentIndex.set(firstEmpty >= 0 ? firstEmpty : 0);
    this.itemStartedAt = Date.now();
    if (this.finished()) {
      this.activeTime.stop();
    } else {
      this.activeTime.start(this.restoredActiveSeconds(items), this.pageVisible());
      this.activeInstanceId = this.instanceId();
    }
    this.loadState.set('ready');

    if (this.finished()) {
      this.loadNextActivity();
      this.loadProgress();
    }
  }

  private resetState(): void {
    this.persistActiveSeconds();
    this.activeTime.stop();
    this.activeInstanceId = 0;
    this.nextActivitySub?.unsubscribe();
    this.nextActivitySub = null;
    this.progressSub?.unsubscribe();
    this.progressSub = null;
    this.clearProgressRefetch();
    this.progress.set({ today: null, badge: null });
    this.lastToday = null;
    this.inFlight.clear();
    this.queued.clear();
    this.failedItems.clear();
    this.failedAction.set(null);
    this.instance.set(null);
    this.items.set([]);
    this.regions.set([]);
    this.answers.set({});
    this.results.set({});
    this.retainedRows.set({});
    this.solutions.set(null);
    this.openTextReviews.set({});
    this.summary.set(null);
    this.checkCount.set(0);
    this.revealed.set(false);
    this.finished.set(false);
    this.canRetake.set(false);
    this.panelError.set(null);
    this.panelErrorMessage.set(null);
    this.saveFailed.set(false);
    this.nextActivity.set(null);
    this.timeSpent.clear();
    this.plans.clear();
    this.dragPlacePlans.clear();
    this.matchPlans.clear();
    this.textSelectPlans.clear();
    this.mazePlans.clear();
    this.crosswordPlans.clear();
    this.wordSearchPlans.clear();
    this.logicGridPlans.clear();
    this.regionPaintPlans.clear();
  }

  // ---------------------------------------------------------------------------
  // Gezinme
  // ---------------------------------------------------------------------------

  goTo(index: number): void {
    if (index < 0 || index >= this.items().length || index === this.currentIndex()) return;
    // Issue #583: oynatıcı maddeye bağlı yeniden kurulur; yazılıp henüz kaydedilmemiş değer (kutu, düzeltme, metin) önce alınır.
    this.flushPlayers();
    this.accumulateTime();
    this.currentIndex.set(index);
  }

  /** "Yanlışları düzelt": ilk yanlış maddeye geçer ve içeriğe odaklar. */
  focusFirstWrong(): void {
    this.panelCollapsed.set(true);
    const results = this.results();
    const index = this.items().findIndex((item) => !this.isOpenText(item) && results[item.id]?.isCorrect === false);
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

  /** Etkileşimli madde (sürükle-bırak, matris #574): cevap `answerPayload` olarak kaydedilir. */
  onDragDropChange(payload: string, item: TestInstanceQuestion): void {
    if (this.revealed() || this.finished() || this.contentLocked()) return;
    this.updateAnswer(item, { selectedAnswerId: null, answerPayload: payload });
  }

  onChoiceSelected(answerId: number, item: TestInstanceQuestion): void {
    if (this.revealed() || this.finished() || this.contentLocked() || !answerId) return;
    this.updateAnswer(item, { selectedAnswerId: answerId, answerPayload: null });
  }

  /** Maddede etkileşim (dokunma / tıklama): kontrol yapıldıysa sonuç paneli kompakt çubuğa iner. */
  onItemInteract(): void {
    if (this.checkCount() > 0) this.panelCollapsed.set(true);
  }

  private updateAnswer(item: TestInstanceQuestion, answer: ItemAnswer): void {
    this.onItemInteract();
    const previous = this.answers()[item.id];
    if (previous && previous.selectedAnswerId === answer.selectedAnswerId && previous.answerPayload === answer.answerPayload) {
      return;
    }
    this.answers.update((all) => ({ ...all, [item.id]: answer }));
    if (this.isOpenText(item)) this.markOpenTextChanged(item);
    // Değişen maddenin kontrol sonucu artık geçerli değil (backend de yeniden yüklemede döndürmez).
    const previousRows = this.results()[item.id]?.rows;
    if (previousRows && (this.isDragPlace(item) || this.isMatch(item))) {
      this.retainedRows.update((all) => ({ ...all, [item.id]: previousRows }));
    }
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

  /**
   * Issue #575: cevap kutusunda yazılmış ama (duraksama / alan bırakma olmadan) henüz yayınlanmamış değer kayda girsin.
   * Kilit (`checking` / `finishing`) açılmadan ÖNCE çağrılmalı: kilitliyken `onDragDropChange` değişikliği yok sayar.
   */
  private flushPlayers(): void {
    if (!this.contentLocked()) {
      this.slotFillPlayer()?.flush();
      this.textSelectPlayer()?.flush();
      this.openTextPlayer()?.flush();
    }
  }

  async runCheck(): Promise<void> {
    if (this.checking() || this.finished()) return;
    if (this.blockOnTooLongOpenText('check')) return;
    this.flushPlayers();
    this.checking.set(true);
    this.clearPanelError();
    await this.flushSaves();

    const items = this.items();
    const answers = this.answers();
    const sent = new Map(items.map((item) => [item.id, this.signature(answers[item.id])]));
    const body = items.map((item) => {
      const answer = answers[item.id];
      return this.isInteractive(item)
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
      const reviews = { ...this.openTextReviews() };
      const openTextIds = new Set(items.filter((item) => this.isOpenText(item)).map((item) => item.id));
      for (const question of response.questions ?? []) {
        // Kontrol uçuştayken değişen maddenin sonucu uygulanmaz.
        if (sent.get(question.instanceQuestionId) !== this.signature(current[question.instanceQuestionId])) continue;
        // Issue #583: açık uçlu maddenin kontrolü onu öğretmene gönderir; değerlendirme durumu yanıttan (yoksa satırdan).
        if (openTextIds.has(question.instanceQuestionId)) {
          reviews[question.instanceQuestionId] = this.reviewFromCheck(question.openTextReview, question.feedback);
        }
        next[question.instanceQuestionId] = {
          isCorrect: question.isCorrect,
          correctCount: question.correctCount,
          totalCount: question.totalCount,
          rows: parseActivityFeedback(question.feedback),
          feedback: question.feedback,
        };
      }
      this.results.set(next);
      this.openTextReviews.set(reviews);
      this.retainedRows.set({});
      this.panelCollapsed.set(false);
      this.checkCount.update((count) => count + 1);
      // Issue #583: açık uçlu maddeler sayımlara girmez; yalnız onlardan oluşan etkinlikte "0 / 0 doğru" gösterilmez.
      this.summary.set(response.totalCount > 0 ? { correct: response.correctCount, total: response.totalCount } : null);
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
    if (this.blockOnTooLongOpenText('finish')) return;
    this.flushPlayers();
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
    // Bitirmeden önceki "Bugün" (varsa): yeniden okumada artış buna göre aranır.
    const todayBefore = this.lastToday;
    try {
      this.activeTime.activity();
      const activeSeconds = this.activeTime.seconds();
      const response = await firstValueFrom(this.testService.completeTest(this.instanceId(), { activeSeconds }));
      if (response?.success === false) {
        this.handleActionError(response, 'finish');
        return;
      }
      // Rozet değerlendirmesi asenkron: izleme `markFinished`'in ilk okumasından önce başlar (ilk sonuç da sayılır).
      this.clearProgressRefetch();
      this.progressWatch = { baseline: todayBefore, attempt: 0 };
      this.markFinished(true);
    } catch (error) {
      this.handleActionError(error, 'finish');
    } finally {
      this.finishing.set(false);
    }
  }

  /**
   * Issue #583: açık uçlu metin en çok sözcüğün 2 katını aşıyorsa oynatıcı onu kaydetmez; kontrol / bitirme son KAYITLI (eski)
   * metni gönderirdi. Bunun yerine panelde "Metin çok uzun" hatası gösterilir ve işlem yapılmaz (öğrenci kısaltıp yeniden dener).
   */
  private blockOnTooLongOpenText(action: ActivityAction): boolean {
    if (this.contentLocked() || !this.openTextPlayer()?.isBlocked()) return false;
    this.failedAction.set(action);
    this.panelError.set('generic');
    this.panelErrorMessage.set(this.tr('openTextTooLong'));
    this.panelCollapsed.set(false);
    return true;
  }

  private markFinished(completed: boolean): void {
    // Issue #583: oturum bitince cevaplı açık uçlu maddeler gönderilmiş sayılır (sunucu da öyle sayar): değerlendirme bekliyor.
    const answers = this.answers();
    const reviews = { ...this.openTextReviews() };
    let changed = false;
    for (const item of this.items()) {
      if (this.isOpenText(item) && !reviews[item.id] && this.hasAnswer(item, answers[item.id])) {
        reviews[item.id] = { status: 'pending', evaluation: null, feedback: null, reviewedAt: null };
        changed = true;
      }
    }
    if (changed) this.openTextReviews.set(reviews);
    this.canRetake.set(completed);
    this.activeTime.stop();
    this.clearStoredActiveSeconds();
    this.activeInstanceId = 0;
    this.revealed.set(false);
    this.finished.set(true);
    this.loadNextActivity();
    this.loadProgress();
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
    this.markFinished(false);
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

  /** Issue #572: "Bugün" + rozet ilerlemesi. Hata → kartlar gizli kalır (bildirim yok). */
  private loadProgress(): void {
    const userId = currentUserId();
    if (!userId) return;
    this.progressSub?.unsubscribe();
    this.progressSub = this.badgeService
      .getActivitySummary(userId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.progress.set(toActivityProgressView(response));
          this.lastToday = response?.today ?? null;
          this.onProgressLoaded(this.lastToday);
        },
        error: () => {
          this.progress.set({ today: null, badge: null });
          this.onProgressLoaded(null);
        },
      });
  }

  /**
   * Bitirme sonrası izleme: taban yoksa ilk okunan değer taban olur; bugünkü etkinlik sayısı ya da etkin dakika tabandan
   * büyükse durulur, değilse takvimdeki sıradaki ana (2 / 5 / 10 sn) kadar beklenip yeniden okunur.
   */
  private onProgressLoaded(today: ActivityTodayStats | null): void {
    const watch = this.progressWatch;
    if (!watch) return;
    if (watch.baseline === null) {
      watch.baseline = today;
    } else if (today && (today.activityCount > watch.baseline.activityCount || today.activeMinutes > watch.baseline.activeMinutes)) {
      this.progressWatch = null;
      return;
    }
    const schedule = ACTIVITY_SUMMARY_REFETCH_SCHEDULE_MS;
    if (watch.attempt >= schedule.length) {
      this.progressWatch = null;
      return;
    }
    const delay = schedule[watch.attempt] - (watch.attempt > 0 ? schedule[watch.attempt - 1] : 0);
    watch.attempt += 1;
    this.progressRefetch = setTimeout(() => {
      this.progressRefetch = null;
      this.loadProgress();
    }, delay);
  }

  private clearProgressRefetch(): void {
    this.progressWatch = null;
    if (this.progressRefetch !== null) {
      clearTimeout(this.progressRefetch);
      this.progressRefetch = null;
    }
  }

  // ---------------------------------------------------------------------------
  // Etkin süre (#572)
  // ---------------------------------------------------------------------------

  /** Oynatıcıdaki etkileşimler ve sekme görünürlüğü; zone dışında (her `pointermove` değişiklik algılaması tetiklemesin). */
  private listenForActivity(): void {
    if (!this.isBrowser) return;
    const element = this.host.nativeElement;
    const onActivity = () => this.activeTime.activity();
    const onVisibility = () => {
      this.activeTime.setVisible(this.pageVisible());
      if (!this.pageVisible()) this.persistActiveSeconds();
    };
    const onPageHide = () => this.persistActiveSeconds();
    this.zone.runOutsideAngular(() => {
      ACTIVITY_EVENTS.forEach((type) => element.addEventListener(type, onActivity, { capture: true, passive: true }));
      this.document.addEventListener('visibilitychange', onVisibility);
      this.document.defaultView?.addEventListener('pagehide', onPageHide);
    });
    this.destroyRef.onDestroy(() => {
      ACTIVITY_EVENTS.forEach((type) => element.removeEventListener(type, onActivity, { capture: true }));
      this.document.removeEventListener('visibilitychange', onVisibility);
      this.document.defaultView?.removeEventListener('pagehide', onPageHide);
    });
  }

  private pageVisible(): boolean {
    return !this.isBrowser || this.document.visibilityState !== 'hidden';
  }

  /**
   * Yeniden yüklemede başlangıç değeri: aynı sekmede kaydedilmiş etkin süre (sessionStorage), yoksa (başka sekme/cihaz)
   * maddelerin `timeTaken` toplamı — sunucunun `activeSeconds` gelmediğinde kullandığı kaynakla aynı (tavanlı).
   */
  private restoredActiveSeconds(items: TestInstanceQuestion[]): number {
    const stored = Number(this.storage()?.getItem(ACTIVE_SECONDS_KEY + this.instanceId()));
    const seed = Number.isFinite(stored) && stored > 0
      ? stored
      : items.reduce((sum, item) => sum + Math.max(0, item.timeTaken || 0), 0);
    return Math.min(ACTIVITY_MAX_ACTIVE_SECONDS, seed);
  }

  private persistActiveSeconds(): void {
    if (this.activeInstanceId <= 0) return;
    try {
      this.storage()?.setItem(ACTIVE_SECONDS_KEY + this.activeInstanceId, String(this.activeTime.seconds()));
    } catch {
      // Depolama dolu/kapalı: yeniden yüklemede `timeTaken` toplamına düşülür.
    }
  }

  private clearStoredActiveSeconds(): void {
    if (this.activeInstanceId <= 0) return;
    try {
      this.storage()?.removeItem(ACTIVE_SECONDS_KEY + this.activeInstanceId);
    } catch {
      // yok say
    }
  }

  private storage(): Storage | null {
    try {
      return this.isBrowser ? this.document.defaultView?.sessionStorage ?? null : null;
    } catch {
      return null;
    }
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

  /**
   * Issue #609: özet ekranından "Yeniden çöz" — onay + `retake` (aynı oturum sıfırlanır); oynatıcı zaten bu oturumun
   * rotasında olduğundan gezinme yerine oturum yeniden yüklenir.
   */
  retake(): void {
    if (!this.finished() || !this.canRetake()) return;
    this.activityLauncher.retake(this.instanceId(), this.instance()?.worksheetId, () => void this.load());
  }

  backToList(): void {
    void this.router.navigate(['/tests'], { queryParams: { type: 'activities' } });
  }

  // ---------------------------------------------------------------------------
  // Yardımcılar
  // ---------------------------------------------------------------------------

  isDragDrop(item: TestInstanceQuestion): boolean {
    return (item.question?.interactionType || 'mcq').toLowerCase() === DRAG_DROP.toLowerCase();
  }

  isMatrix(item: TestInstanceQuestion): boolean {
    return isMatrixChoiceType(item.question?.interactionType);
  }

  isSlotFill(item: TestInstanceQuestion): boolean {
    return isSlotFillType(item.question?.interactionType);
  }

  isDragPlace(item: TestInstanceQuestion): boolean {
    return isDragPlaceType(item.question?.interactionType);
  }

  isMatch(item: TestInstanceQuestion): boolean {
    return isMatchType(item.question?.interactionType);
  }

  isTextSelect(item: TestInstanceQuestion): boolean {
    return isTextSelectType(item.question?.interactionType);
  }

  isOpenText(item: TestInstanceQuestion): boolean {
    return isOpenTextType(item.question?.interactionType);
  }

  isMazeTree(item: TestInstanceQuestion): boolean {
    return isMazeTreeType(item.question?.interactionType);
  }

  isCrossword(item: TestInstanceQuestion): boolean {
    return isCrosswordType(item.question?.interactionType);
  }

  isLogicGrid(item: TestInstanceQuestion): boolean {
    return isLogicGridType(item.question?.interactionType);
  }

  isRegionPaint(item: TestInstanceQuestion): boolean {
    return isRegionPaintType(item.question?.interactionType);
  }

  isWordSearch(item: TestInstanceQuestion): boolean {
    return isWordSearchType(item.question?.interactionType);
  }

  /** Cevabı `answerPayload` olan (MCQ dışı) madde. */
  private isInteractive(item: TestInstanceQuestion): boolean {
    return (
      this.isDragDrop(item) ||
      this.isMatrix(item) ||
      this.isSlotFill(item) ||
      this.isDragPlace(item) ||
      this.isMatch(item) ||
      this.isTextSelect(item) ||
      this.isOpenText(item) ||
      this.isMazeTree(item) ||
      this.isCrossword(item) ||
      this.isWordSearch(item) ||
      this.isLogicGrid(item) ||
      this.isRegionPaint(item)
    );
  }

  /**
   * Issue #583: açık uçlu metin değişti — sunucuda değerlendirme artık eski metne ait ("outdated"; öğrenci eski değerlendirmeyi
   * notla görür), değerlendirme yoksa metin yeniden kontrol edilene dek gönderilmemiş taslaktır (null → "kontrol et" ipucu).
   */
  private markOpenTextChanged(item: TestInstanceQuestion): void {
    const review = this.openTextReviews()[item.id] ?? null;
    if (!review) return;
    const next: OpenTextStudentReview | null = review.evaluation ? { ...review, status: 'outdated' } : null;
    this.openTextReviews.update((all) => ({ ...all, [item.id]: next }));
  }

  /** Kontrol yanıtındaki değerlendirme; yoksa satır durumundan (cevapsızda null). */
  private reviewFromCheck(raw: unknown, feedback: string | null | undefined): OpenTextStudentReview | null {
    const review = parseOpenTextStudentReview(raw);
    if (review) return review;
    const status = parseOpenTextFeedbackStatus(feedback);
    if (!status) return null;
    return status === 'pending'
      ? { status: 'pending', evaluation: null, feedback: null, reviewedAt: null }
      : { status: 'reviewed', evaluation: status, feedback: null, reviewedAt: null };
  }

  private dragPlacePlanFor(item: TestInstanceQuestion): DragPlacePlanV1 | null {
    if (!this.dragPlacePlans.has(item.id)) {
      this.dragPlacePlans.set(item.id, parseDragPlacePlan(item.question?.interactionPlan));
    }
    return this.dragPlacePlans.get(item.id) ?? null;
  }

  private matchPlanFor(item: TestInstanceQuestion): MatchPlanV1 | null {
    if (!this.matchPlans.has(item.id)) {
      this.matchPlans.set(item.id, parseMatchPlan(item.question?.interactionPlan));
    }
    return this.matchPlans.get(item.id) ?? null;
  }

  private logicGridPlanFor(item: TestInstanceQuestion): LogicGridPlanV1 | null {
    if (!this.logicGridPlans.has(item.id)) {
      this.logicGridPlans.set(item.id, parseLogicGridPlan(item.question?.interactionPlan));
    }
    return this.logicGridPlans.get(item.id) ?? null;
  }

  private regionPaintPlanFor(item: TestInstanceQuestion): RegionPaintPlanV1 | null {
    if (!this.regionPaintPlans.has(item.id)) {
      this.regionPaintPlans.set(item.id, parseRegionPaintPlan(item.question?.interactionPlan));
    }
    return this.regionPaintPlans.get(item.id) ?? null;
  }

  private wordSearchPlanFor(item: TestInstanceQuestion): WordSearchPlanV1 | null {
    if (!this.wordSearchPlans.has(item.id)) {
      this.wordSearchPlans.set(item.id, parseWordSearchPlan(item.question?.interactionPlan));
    }
    return this.wordSearchPlans.get(item.id) ?? null;
  }

  private crosswordPlanFor(item: TestInstanceQuestion): CrosswordPlanV1 | null {
    if (!this.crosswordPlans.has(item.id)) {
      this.crosswordPlans.set(item.id, parseCrosswordPlan(item.question?.interactionPlan));
    }
    return this.crosswordPlans.get(item.id) ?? null;
  }

  private mazePlanFor(item: TestInstanceQuestion): MazeTreePlanV1 | null {
    if (!this.mazePlans.has(item.id)) {
      this.mazePlans.set(item.id, parseMazeTreePlan(item.question?.interactionPlan));
    }
    return this.mazePlans.get(item.id) ?? null;
  }

  private textSelectPlanFor(item: TestInstanceQuestion): TextSelectPlanV1 | null {
    if (!this.textSelectPlans.has(item.id)) {
      this.textSelectPlans.set(item.id, parseTextSelectPlan(item.question?.interactionPlan));
    }
    return this.textSelectPlans.get(item.id) ?? null;
  }

  private slotFillPlanFor(item: TestInstanceQuestion): SlotFillPlanV1 | null {
    if (!this.slotFillPlans.has(item.id)) {
      this.slotFillPlans.set(item.id, parseSlotFillPlan(item.question?.interactionPlan));
    }
    return this.slotFillPlans.get(item.id) ?? null;
  }

  /** Issue #575: doldurulmuş kutu sayısı. */
  private slotFillFilled(item: TestInstanceQuestion, answer: ItemAnswer | undefined): number {
    return Object.keys(parseSlotFillValues(answer?.answerPayload, this.slotFillPlanFor(item))).length;
  }

  private matrixPlanFor(item: TestInstanceQuestion): MatrixChoicePlanV1 | null {
    if (!this.matrixPlans.has(item.id)) {
      this.matrixPlans.set(item.id, parseMatrixChoicePlan(item.question?.interactionPlan));
    }
    return this.matrixPlans.get(item.id) ?? null;
  }

  private matrixAnsweredRows(item: TestInstanceQuestion, answer: ItemAnswer | undefined): number {
    return Object.keys(parseMatrixChoiceSelections(answer?.answerPayload, this.matrixPlanFor(item))).length;
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
    if (this.isMatrix(item)) return this.matrixAnsweredRows(item, answer) > 0;
    if (this.isSlotFill(item)) return this.slotFillFilled(item, answer) > 0;
    if (this.isDragPlace(item)) return placementCount(parseDragPlacePlacements(answer?.answerPayload, this.dragPlacePlanFor(item))) > 0;
    if (this.isMatch(item)) return Object.keys(parseMatchPairs(answer?.answerPayload, this.matchPlanFor(item))).length > 0;
    if (this.isTextSelect(item)) {
      return !isEmptyTextSelectAnswer(parseTextSelectAnswer(answer?.answerPayload, this.textSelectPlanFor(item)));
    }
    if (this.isOpenText(item)) return !isBlankOpenText(parseOpenTextAnswer(answer?.answerPayload));
    if (this.isMazeTree(item)) return !isEmptyMazeAnswer(parseMazeAnswer(answer?.answerPayload, this.mazePlanFor(item)));
    if (this.isRegionPaint(item)) return !isEmptyPaint(parseRegionPaintAnswer(answer?.answerPayload, this.regionPaintPlanFor(item)));
    if (this.isLogicGrid(item)) return countMarks(parseLogicGridAnswer(answer?.answerPayload, this.logicGridPlanFor(item))) > 0;
    if (this.isWordSearch(item)) return parseWordSearchAnswer(answer?.answerPayload, this.wordSearchPlanFor(item)).length > 0;
    if (this.isCrossword(item)) {
      return !isEmptyCrosswordGrid(parseCrosswordAnswer(answer?.answerPayload, this.crosswordPlanFor(item)));
    }
    if (this.isDragDrop(item)) return this.placementsOf(answer?.answerPayload).length > 0;
    return !!answer?.selectedAnswerId;
  }

  /** Boş satırı olan madde: MCQ seçilmemiş ya da hedeflerin bir kısmı boş. */
  private isIncomplete(item: TestInstanceQuestion, answer: ItemAnswer | undefined): boolean {
    if (this.isMatrix(item)) return this.matrixAnsweredRows(item, answer) < (this.matrixPlanFor(item)?.rows.length ?? 0);
    if (this.isSlotFill(item)) return this.slotFillFilled(item, answer) < (this.slotFillPlanFor(item)?.slots.length ?? 0);
    // Issue #576: sürükle-yerleştirde hiç yerleşim yok ya da sıra modunda boş konum var (boş kalması doğru olabilecek
    // kova / fazla hedef sayılmaz); eşleştirmede eşlenmemiş sol öğe = eksik (yalnız uyarı).
    if (this.isDragPlace(item)) {
      const plan = this.dragPlacePlanFor(item);
      const placed = parseDragPlacePlacements(answer?.answerPayload, plan);
      if (!plan) return false;
      if (placementCount(placed) === 0) return true;
      return plan.mode === 'order' && plan.targets.some((t) => cardsIn(placed, t.id).length === 0);
    }
    if (this.isMatch(item)) {
      const plan = this.matchPlanFor(item);
      const pairs = parseMatchPairs(answer?.answerPayload, plan);
      return !!plan && openLefts(plan).some((left) => !rightOf(pairs, left.id));
    }
    // Issue #582: noktalamada boş gap; seçimde hiç seçim yok (hedef sayısı bilinmez); düzeltmede seçili kelimelerin hiçbirine
    // düzeltme yazılmamış. Çok kelimeli hatada ("Sonun da") düzeltme kelimelerden birine yazılır, diğeri boş kalır: eksik değil.
    if (this.isTextSelect(item)) {
      const plan = this.textSelectPlanFor(item);
      if (!plan) return false;
      const parsed = parseTextSelectAnswer(answer?.answerPayload, plan);
      if (plan.mode === 'punctuation') return plan.tokens.some((t) => t.gap && !textValueOf(parsed, t.id));
      if (parsed.selected.length === 0) return true;
      return plan.mode === 'correct' && parsed.selected.every((id) => !textValueOf(parsed, id).trim());
    }
    // Issue #583: açık uçlu maddede metin yok = boş (sözcük sınırı uyarısı oynatıcıda).
    if (this.isOpenText(item)) return isBlankOpenText(parseOpenTextAnswer(answer?.answerPayload));
    // Issue #585: yol bitişe varmadı; taşlarda hiç işaret yok.
    if (this.isMazeTree(item)) {
      const plan = this.mazePlanFor(item);
      if (!plan) return false;
      const parsed = parseMazeAnswer(answer?.answerPayload, plan);
      return plan.variant === 'stones' ? parsed.marked.length === 0 : !walkMaze(plan, parsed.path).finished;
    }
    // Issue #587: boyamada hiç boyanmış kare yok (hedef sayılar öğrenciye gitmez).
    if (this.isRegionPaint(item)) return isEmptyPaint(parseRegionPaintAnswer(answer?.answerPayload, this.regionPaintPlanFor(item)));
    // Issue #586: mantık tablosunda ✓'siz (kişi, kategori) çifti var.
    if (this.isLogicGrid(item)) {
      const plan = this.logicGridPlanFor(item);
      if (!plan) return false;
      const marks = parseLogicGridAnswer(answer?.answerPayload, plan);
      return plan.subject.items.some((s) => plan.categories.some((c) => ticks(plan, marks, s.id, c.id).length === 0));
    }
    // Issue #586: kelime avında kelime sayısından az çizgi var.
    if (this.isWordSearch(item)) {
      const plan = this.wordSearchPlanFor(item);
      return !!plan && parseWordSearchAnswer(answer?.answerPayload, plan).length < plan.words.length;
    }
    // Issue #586: bulmacada boş beyaz hücre kaldı.
    if (this.isCrossword(item)) {
      const plan = this.crosswordPlanFor(item);
      if (!plan) return false;
      const grid = parseCrosswordAnswer(answer?.answerPayload, plan);
      return plan.clues.some((clue) => clueWord(grid, clue).includes(' '));
    }
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
    // Issue #583: yalnız açık uçlu maddeli etkinlikte ilk kontrol 0 / 0'dır (sayılmaz): bilgi satırı gösterilmez.
    if (
      !this.finished() ||
      instance.firstCheckCorrectCount == null ||
      instance.firstCheckTotalCount == null ||
      instance.firstCheckTotalCount <= 0
    ) {
      return null;
    }
    return { correct: instance.firstCheckCorrectCount, total: instance.firstCheckTotalCount };
  }

  private summaryFrom(items: TestInstanceQuestion[], results: Record<number, ItemResult>): ActivityCheckSummary | null {
    const checked = items.filter((item) => !!results[item.id]);
    if (checked.length === 0) return null;
    const summary = checked.reduce(
      (acc, item) => ({ correct: acc.correct + results[item.id].correctCount, total: acc.total + results[item.id].totalCount }),
      { correct: 0, total: 0 }
    );
    // Issue #583: açık uçlu satırlar 0 / 0 gelir; yalnız onlar kontrol edildiyse özet yok.
    return summary.total > 0 ? summary : null;
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

  /** Issue #574: matris satırları — satır adı + öğrencinin seçimi, ✓/✗ ve (çözüm açıkken) doğru kolonlar. */
  private matrixRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.matrixPlanFor(item);
    if (!plan) return [];
    const labels = new Map(plan.columns.map((c) => [c.id, c.label]));
    const selections = parseMatrixChoiceSelections(this.answers()[item.id]?.answerPayload, plan);
    const solved = solution ? parseMatrixChoiceSolution(solution.interactionSolution, plan) : {};
    const marks = new Map((result?.rows ?? []).map((row) => [row.id, row.correct]));
    const join = (ids: string[] | undefined) =>
      ids?.length ? ids.map((id) => labels.get(id) ?? id).join(', ') : this.tr('rows.empty');
    return plan.rows.map((row, index) => {
      const name = row.label?.trim() || this.tr('rows.matrixRowFallback', { n: index + 1 });
      return {
        label: this.tr('rows.matrixRow', { name, content: join(selectionOf(selections, row.id)) }),
        correct: marks.has(row.id) ? marks.get(row.id)! : null,
        solution: solution ? join(selectionOf(solved, row.id)) : null,
      };
    });
  }

  /** Issue #575: kutu satırları — "Kutu n: değer", ✓/✗ ve (çözüm açıkken) kabul edilen değer. */
  private slotFillRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.slotFillPlanFor(item);
    if (!plan) return [];
    const values = parseSlotFillValues(this.answers()[item.id]?.answerPayload, plan);
    const solved = solution ? parseSlotFillSolution(solution.interactionSolution, plan) : null;
    const marks = new Map((result?.rows ?? []).map((row) => [row.id, row.correct]));
    return plan.slots.map((slot, index) => {
      const own = valueOf(values, slot.id);
      const accepted = solved ? solutionDisplay(slot.input, solutionOf(solved, slot.id)) : '';
      return {
        label: this.tr('rows.slot', { n: index + 1, content: own ? valueDisplay(slot.input, own) : this.tr('rows.empty') }),
        correct: marks.has(slot.id) ? marks.get(slot.id)! : null,
        solution: solved ? accepted || this.tr('rows.empty') : null,
      };
    });
  }

  /**
   * Issue #576: hedef satırları — "<hedef>: <kartlar>". Hedefin sonucu, satır id'si `<hedef>:` ile başlayan sunucu satırlarından
   * (yanlış yerleşim ya da eksik kart `"<hedef>:"` varsa ✗); hangi kartın eksik olduğu hesaplanmaz.
   */
  private dragPlaceRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.dragPlacePlanFor(item);
    if (!plan) return [];
    // "Kart n" numarası oynatıcıdaki karışık havuz sırasıyla aynı (tohum = madde id'si).
    const cardName = new Map(
      seededShuffle(plan.cards, `${item.id}`).map((c, i) => [c.id, c.label?.trim() || this.tr('rows.cardFallback', { n: i + 1 })])
    );
    const placed = parseDragPlacePlacements(this.answers()[item.id]?.answerPayload, plan);
    const solved = solution ? parseDragPlaceSolution(solution.interactionSolution, plan) : null;
    const rows = result?.rows ?? [];
    const join = (ids: string[]) => (ids.length ? ids.map((id) => cardName.get(id) ?? id).join(', ') : this.tr('rows.empty'));
    return plan.targets.map((target, index) => {
      const name =
        target.label?.trim() ||
        this.tr(plan.mode === 'order' ? 'rows.positionFallback' : 'rows.targetFallback', { n: index + 1 });
      const own = rows.filter((row) => row.id.startsWith(`${target.id}:`));
      return {
        label: this.tr('rows.place', { name, content: join(cardsIn(placed, target.id)) }),
        correct: own.length === 0 ? null : own.every((row) => row.correct),
        solution: solved ? join(cardsIn(solved, target.id)) : null,
      };
    });
  }

  /** Issue #576: eşleştirme satırları — "<sol>: <sağ>" (örnek çift hariç), ✓/✗ ve (çözüm açıkken) doğru eş. */
  private matchRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.matchPlanFor(item);
    if (!plan) return [];
    const leftName = (id: string) => {
      const index = plan.left.findIndex((i) => i.id === id);
      return plan.left[index]?.label?.trim() || this.tr('rows.leftFallback', { n: index + 1 });
    };
    // "Sağ n" numarası oynatıcıdaki karışık sağ sütun sırasıyla aynı (tohum = madde id'si).
    const rights = seededShuffle(plan.right, `${item.id}`);
    const rightName = (id: string | null) => {
      if (!id) return this.tr('rows.empty');
      const index = rights.findIndex((i) => i.id === id);
      return rights[index]?.label?.trim() || this.tr('rows.rightFallback', { n: index + 1 });
    };
    const pairs = parseMatchPairs(this.answers()[item.id]?.answerPayload, plan);
    const solved = solution ? parseMatchSolution(solution.interactionSolution, plan) : null;
    const marks = new Map((result?.rows ?? []).map((row) => [row.id, row.correct]));
    return openLefts(plan).map((left) => ({
      label: this.tr('rows.match', { name: leftName(left.id), content: rightName(rightOf(pairs, left.id)) }),
      correct: marks.has(left.id) ? marks.get(left.id)! : null,
      solution: solved ? rightName(rightOf(solved, left.id)) : null,
    }));
  }

  /**
   * Issue #582: noktalamada boşluk satırları ("Boşluk n: işaret", ✓/✗, çözüm açıkken doğru işaret(ler)); seçim / düzeltmede
   * kontrol sonrası seçili kelimeler ("«kelime» → düzeltme", fazla seçim ayrıca belirtilir), çözüm açıkken hedefler
   * ("Hedef n: «kelimeler»", kabul edilen düzeltme). Hangi hedefin eksik olduğu kontrol sonucunda söylenmez.
   */
  private textSelectRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.textSelectPlanFor(item);
    if (!plan) return [];
    const answer = parseTextSelectAnswer(this.answers()[item.id]?.answerPayload, plan);
    const solved = solution ? parseTextSelectSolution(solution.interactionSolution, plan) : null;
    const rows = new Map((result?.rows ?? []).map((row) => [row.id, row]));
    const text = new Map(plan.tokens.map((t) => [t.id, t.text]));
    if (plan.mode === 'punctuation') {
      return plan.tokens
        .filter((t) => t.gap)
        .map((gap, index) => {
          const own = textValueOf(answer, gap.id);
          const accept = solved ? gapAcceptOf(solved, gap.id) : [];
          return {
            label: this.tr('rows.textGap', { n: index + 1, content: own || this.tr('rows.empty') }),
            correct: rows.has(gap.id) ? rows.get(gap.id)!.correct : null,
            solution: solved ? accept.join(' / ') || this.tr('rows.empty') : null,
          };
        });
    }
    if (solved) {
      return solved.targets.map((target, index) => ({
        label: this.tr('rows.textTarget', { n: index + 1, word: target.tokenIds.map((id) => text.get(id) ?? id).join(' ') }),
        correct: target.tokenIds.some((id) => rows.get(id)?.correct) ? true : result ? false : null,
        solution: plan.mode === 'correct' ? target.accept.join(' / ') || this.tr('rows.empty') : target.tokenIds.map((id) => text.get(id) ?? id).join(' '),
      }));
    }
    if (answer.selected.length === 0) return result ? [{ label: this.tr('rows.textNone'), correct: false, solution: null }] : [];
    return answer.selected.map((id) => {
      const word = text.get(id) ?? id;
      const row = rows.get(id);
      const fix = textValueOf(answer, id);
      const label =
        row?.reason === 'extra'
          ? this.tr('rows.textWordExtra', { word })
          : plan.mode === 'correct'
            ? this.tr('rows.textWordFix', { word, content: fix || this.tr('rows.empty') })
            : this.tr('rows.textWord', { word });
      return { label, correct: row ? row.correct : null, solution: null };
    });
  }

  /**
   * Issue #585: yol oyununda kontrol sonrası öğrencinin geçtiği karar düğümleri ("<düğüm>: <seçilen dal>", ilk sapmaya kadar),
   * çözüm açıkken rotanın düğümleri ve doğru dal; taşlarda taş başına işaretli / işaretsiz. Yolu hiç kurulmamışsa tek satır.
   */
  /** Issue #586: mantık tablosunda (kişi, kategori) çifti başına satır ("Ali – Renk: Mavi"); çözüm açıkken doğru seçenek. */
  private logicGridRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.logicGridPlanFor(item);
    if (!plan || (!result && !solution)) return [];
    const marks = parseLogicGridAnswer(this.answers()[item.id]?.answerPayload, plan);
    const assign = solution ? parseLogicGridSolution(solution.interactionSolution, plan) : null;
    const rowMarks = new Map((result?.rows ?? []).map((row) => [row.id, row.correct]));
    return plan.subject.items.flatMap((subject) =>
      plan.categories.map((category) => {
        const label = (id: string | undefined) => category.items.find((o) => o.id === id)?.label ?? null;
        const picked = ticks(plan, marks, subject.id, category.id).map((id) => label(id)).join(', ');
        const expected = assign && Object.hasOwn(assign, subject.id) ? label(assign[subject.id][category.id]) : null;
        const key = pairKey(subject.id, category.id);
        return {
          label: this.tr('rows.logicPair', {
            subject: subject.label,
            category: category.label,
            content: picked || this.tr('rows.empty'),
          }),
          correct: rowMarks.has(key) ? rowMarks.get(key)! : null,
          solution: expected,
        };
      })
    );
  }

  /** Issue #587: boyamada renk başına satır ("Mavi: 35 kare"); çözüm açıkken hedef sayı. */
  private regionPaintRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.regionPaintPlanFor(item);
    if (!plan || (!result && !solution)) return [];
    const counts = countPainted(parseRegionPaintAnswer(this.answers()[item.id]?.answerPayload, plan), plan.palette.length);
    const target = solution ? parseRegionPaintSolution(solution.interactionSolution, plan) : null;
    const rowMarks = new Map((result?.rows ?? []).map((row) => [row.id, row.correct]));
    return plan.palette.map((entry, index) => {
      const color = entry.label || this.transloco.translate<string>(`shared.questionCanvas.regionPaint.colors.${entry.color}`);
      const expected = target && Object.hasOwn(target, entry.id) ? target[entry.id] : null;
      return {
        label: this.tr('rows.paintColor', { color, count: counts[index] }),
        correct: rowMarks.has(entry.id) ? rowMarks.get(entry.id)! : null,
        solution: expected === null ? null : this.tr('rows.paintColor', { color, count: expected }),
      };
    });
  }

  /** Issue #586: kelime avında kelime başına satır ("KALP: bulundu"); kontrol yoksa satır yok (çözüm ızgarada gösterilir). */
  private wordSearchRows(item: TestInstanceQuestion, result: ItemResult | undefined): ActivityResultRow[] {
    const plan = this.wordSearchPlanFor(item);
    if (!plan || !result) return [];
    const marks = new Map(result.rows.map((row) => [row.id, row.correct]));
    return plan.words.map((word, i) => ({
      label: this.tr('rows.wordSearchWord', {
        word: word.label || this.tr('rows.mazeNodeFallback', { n: i + 1 }),
        content: this.tr(marks.get(word.id) ? 'rows.wordSearchFound' : 'rows.wordSearchMissing'),
      }),
      correct: marks.has(word.id) ? marks.get(word.id)! : null,
      solution: null,
    }));
  }

  /** Issue #586: bulmacada ipucu başına satır ("1 → ipucu: KAL·"); çözüm açıkken doğru kelime. */
  private crosswordRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.crosswordPlanFor(item);
    if (!plan || (!result && !solution)) return [];
    const grid = parseCrosswordAnswer(this.answers()[item.id]?.answerPayload, plan);
    const answers = solution ? parseCrosswordSolution(solution.interactionSolution, plan) : null;
    const marks = new Map((result?.rows ?? []).map((row) => [row.id, row.correct]));
    const numbers = clueNumbers(plan);
    return plan.clues.map((clue) => {
      const word = clueWord(grid, clue);
      return {
        label: this.tr('rows.crosswordClue', {
          num: numbers.get(clue.id),
          arrow: clue.dir === 'across' ? '→' : '↓',
          text: clue.text ?? '',
          word: word.trim() ? word.replace(/ /g, '·') : this.tr('rows.empty'),
        }),
        correct: marks.has(clue.id) ? marks.get(clue.id)! : null,
        solution: answers && Object.hasOwn(answers, clue.id) ? answers[clue.id] : null,
      };
    });
  }

  private mazeRows(item: TestInstanceQuestion, result: ItemResult | undefined, solution: ActivitySolution | null): ActivityResultRow[] {
    const plan = this.mazePlanFor(item);
    if (!plan) return [];
    const answer = parseMazeAnswer(this.answers()[item.id]?.answerPayload, plan);
    const solved = solution ? parseMazeSolution(solution.interactionSolution, plan) : null;
    const marks = new Map((result?.rows ?? []).map((row) => [row.id, row.correct]));
    const nodeName = (id: string) => {
      const index = plan.nodes.findIndex((n) => n.id === id);
      return plan.nodes[index]?.label || this.tr('rows.mazeNodeFallback', { n: index + 1 });
    };
    const edgeName = (id: string | null | undefined) => (id ? edgeOf(plan, id)?.label ?? id : this.tr('rows.empty'));

    if (plan.variant === 'stones') {
      const marked = new Set(answer.marked);
      const expected = solved ? new Set(solved.marked) : null;
      const state = (on: boolean) => this.tr(on ? 'rows.mazeMarked' : 'rows.mazeUnmarked');
      return plan.nodes.map((stone) => ({
        label: this.tr('rows.mazeStep', { name: nodeName(stone.id), content: state(marked.has(stone.id)) }),
        correct: marks.has(stone.id) ? marks.get(stone.id)! : null,
        solution: expected ? state(expected.has(stone.id)) : null,
      }));
    }

    const walk = walkMaze(plan, answer.path);
    const chosenAt = new Map<string, string>();
    walk.edges.forEach((edgeId, i) => {
      if (!chosenAt.has(walk.nodes[i])) chosenAt.set(walk.nodes[i], edgeId);
    });
    const route = solved ? mazeRoute(plan, solved.choices) : null;
    if (route) {
      let current = plan.start ?? '';
      return route.map((edgeId) => {
        const nodeId = current;
        const edge = edgeOf(plan, edgeId);
        current = (edge && edgeTarget(plan, edge, nodeId)) || current;
        return {
          label: this.tr('rows.mazeStep', { name: nodeName(nodeId), content: edgeName(chosenAt.get(nodeId)) }),
          correct: marks.has(nodeId) ? marks.get(nodeId)! : null,
          solution: edgeName(edgeId),
        };
      });
    }
    if (!result) return [];
    if (result.rows.length === 0) return [{ label: this.tr('rows.mazeNoPath'), correct: false, solution: null }];
    return result.rows.map((row, i) => ({
      label: this.tr('rows.mazeStep', { name: nodeName(row.id), content: edgeName(walk.edges[i]) }),
      correct: row.correct,
      solution: null,
    }));
  }

  /**
   * Issue #583: açık uçlu madde tek satır — "Cevabın: n sözcük" ve doğru/yanlış yerine değerlendirme durumu (bekliyor / Yeterli /
   * Kısmen / Eksik). Kontrol ya da değerlendirme yoksa satır yok; çözüm açıkken örnek cevap oynatıcıda gösterilir.
   */
  private openTextRows(item: TestInstanceQuestion, result: ItemResult | undefined): ActivityResultRow[] {
    const review = this.openTextReviews()[item.id] ?? null;
    const status = openTextRowStatus(review) ?? (result ? parseOpenTextFeedbackStatus(result.feedback) : null);
    if (!status) return [];
    const words = countOpenTextWords(parseOpenTextAnswer(this.answers()[item.id]?.answerPayload));
    return [
      {
        label: words > 0 ? this.tr('rows.openText', { count: words }) : this.tr('rows.openTextEmpty'),
        correct: null,
        status,
        solution: null,
      },
    ];
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

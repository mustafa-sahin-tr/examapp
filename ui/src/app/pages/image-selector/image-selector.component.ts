import {
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  ViewChild,
  computed,
  inject,
  signal,
  ChangeDetectionStrategy
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { timeout } from 'rxjs';
import { CommonModule } from '@angular/common';
import { QuestionRegion, AnswerChoice, RegionOrAnswerHit, ClassificationSource } from '../../models/draws';
import { PASSAGE_INSTRUCTION_MAX_LENGTH, PassageKind } from '../../models/activity';
import {
  MATRIX_CHOICE,
  MATRIX_CHOICE_LIMITS,
  MATRIX_COLUMN_PRESETS,
  MatrixBox,
  MatrixChoiceColumn,
  MatrixChoicePlanV1,
  MatrixColumnPreset,
  MatrixSelections,
  emptyMatrixSelections,
  parseMatrixChoicePlan,
} from '../../models/matrix-choice';
import {
  SLOT_FILL,
  SLOT_FILL_LIMITS,
  SLOT_PALETTE_PRESETS,
  SlotBox,
  SlotKind,
  SlotPalettePreset,
  parseSlotFillPlan,
} from '../../models/slot-fill';
import { isSlotAnswerCorrect } from '../../shared/utils/slot-normalizer';
import {
  COMMA_SEPARATOR,
  LINE_SEPARATOR,
  SPACE_SEPARATOR,
  SlotAuthoring,
  SlotFillAuthoringState,
  SlotFillInspector,
  buildSlotFillInspector,
  buildSlotFillPlan,
  buildSlotFillSolution,
  copySlotSettings,
  createSlotFillState,
  newSlot,
  nextSlotId,
  parseList,
  slotFillIssues,
  slotIssues,
  slotFillStateFromPlan,
  slotSolution,
} from './slot-fill-authoring';
import { DRAG_PLACE, DragPlaceMode, parseDragPlacePlan } from '../../models/drag-place';
import { MATCH, MatchSide, parseMatchPlan } from '../../models/match';
import { TEXT_SELECT, TextSelectMode, TextSelectStyle, parseTextSelectPlan } from '../../models/text-select';
import { OPEN_TEXT, OPEN_TEXT_LIMITS } from '../../models/open-text';
import { PaintColor, PaintPoint, REGION_PAINT, REGION_PAINT_LIMITS, RegionPaintStyle } from '../../models/region-paint';
import {
  PaintDrawShape,
  PaintRegionsAuthoringState,
  PaintRegionsInspector,
  addPaintColor,
  addPaintRegion,
  addPendingPoint,
  buildPaintRegionsInspector,
  buildPaintRegionsPlan,
  buildPaintRegionsSolution,
  cancelPendingPolygon,
  clearPaintRegions,
  createPaintRegionsState,
  ellipsePoints,
  finishPendingPolygon,
  paintRegionAt,
  paintRegionsIssues,
  rectPoints,
  removePaintColor,
  removePaintRegion,
  setPaintBrush,
  setPaintColor,
  setPaintLabel,
  setPaintRegionFill,
  setPaintRegionLabel,
  setPaintShape,
  setPaintStyle,
  togglePaintRegionFill,
} from './paint-regions-authoring';
import {
  OpenTextAuthoringState,
  OpenTextInspector,
  buildOpenTextInspector,
  buildOpenTextPlan,
  buildOpenTextSolution,
  createOpenTextState,
  openTextIssues,
  parseLimitInput,
  setOpenTextLimit,
  setOpenTextSolutionText,
} from './open-text-authoring';
import { InteractionBox } from '../../models/interaction-ids';
import {
  DragPlaceAuthoringState,
  DragPlaceInspector,
  addPlaceCardBox,
  addPlaceCardsFromText,
  addPlaceTarget,
  buildDragPlaceInspector,
  buildDragPlacePlan,
  buildDragPlaceSolution,
  createDragPlaceState,
  dragPlaceIssues,
  dragPlaceStateFromPlan,
  fillOrderFromCards,
  removePlaceCard,
  removePlaceTarget,
  selectPlaceTarget,
  setDragPlaceMode,
  setDragPlaceReuse,
  setPlaceCardLabel,
  setPlaceTargetCapacity,
  setPlaceTargetLabel,
  togglePlaceAssignment,
} from './drag-place-authoring';
import {
  MatchAuthoringState,
  MatchInspector,
  addMatchItem,
  addMatchItemsFromText,
  buildMatchInspector,
  buildMatchPlan,
  buildMatchSolution,
  createMatchState,
  matchIssues,
  matchStateFromPlan,
  removeMatchItem,
  selectMatchLeft,
  setMatchItemLabel,
  setMatchPair,
  toggleMatchPrelinked,
} from './match-authoring';
import {
  TextSelectAuthoringState,
  TextSelectInspector,
  addTextLine,
  buildTextSelectInspector,
  buildTextSelectPlan,
  buildTextSelectSolution,
  createTextSelectState,
  parseAcceptText,
  parsePaletteText,
  removeTextTarget,
  removeTextToken,
  selectTextToken,
  setPendingTextLine,
  setTextSelectMode,
  setTextSelectPalette,
  setTextSelectStyle,
  setTextTargetAccept,
  setTextTargetSuffixes,
  setTextTokenText,
  splitTextTarget,
  textSelectIssues,
  textSelectStateFromPlan,
  toggleGapSymbol,
  toggleTextTarget,
} from './text-select-authoring';
import { QuillModule } from 'ngx-quill';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { QuestionDetectorService } from '../../services/question-detector.service';
import {
  ActivityComponent,
  BlockInteractionType,
  BlockKind,
  Prediction,
  PredictionList,
  blockKindOf,
  interactionForComponent,
  isActivityComponent,
  isBlockInteractionType,
  PredictionMask,
  isConsistentInteraction,
  isExcludedBlockKind,
} from '../../models/prediction';
import { DetectionSource } from '../../models/labeling';
import { LabelingService } from '../../services/labeling.service';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { QuestionNavigatorComponent } from '../../shared/components/question-navigator/question-navigator.component';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { QuestionCanvasViewComponentv5 } from '../../shared/components/question-canvas-view-v5/question-canvas-view-v5.component';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';
import { SubjectService } from '../../services/subject.service';
import { MatDialog } from '@angular/material/dialog';
import {
  ClassificationSelectorComponent,
  ClassificationSelection,
} from '../../shared/components/classification-selector/classification-selector.component';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import {
  COORDINATE_JSON_MAX_BYTES,
  CoordinateQuestion,
  buildRegionsFromCoordinates,
  coordinateFileKey,
  isCoordinateJsonFile,
  parseCoordinateJson,
  selectCoordinateImageFiles,
} from './coordinate-json';

/**
 * Soru kırpma/yükleme akışının metinleri kendi scope'unda: `public/i18n/image-selector/<lang>.json`
 * (issue #183). Komponent hem `question-canvas` hem `question-canvas-preview` içinden kullanıldığı
 * için scope'u kendisi sağlar; Transloco scope provider'ları çoklu olduğundan ebeveynin scope'u
 * (question) da erişilebilir kalır.
 */
const IMAGE_SELECTOR_SCOPE = 'image-selector';
/** question-detector analizi için üst süre; aşılırsa hata dalı (analysisError) çalışır. */
const PREDICT_TIMEOUT_MS = 120_000;

interface WarningMarker {
  id: number;
  x: number;
  y: number;
  messages: string[];
  type?: 'error' | 'warning' | 'info';
}

export type QuestionInteractionType =
  | 'mcq'
  | 'dragDropLabeling'
  | typeof MATRIX_CHOICE
  | typeof SLOT_FILL
  | typeof DRAG_PLACE
  | typeof MATCH
  | typeof TEXT_SELECT
  | typeof OPEN_TEXT
  | typeof REGION_PAINT;

/**
 * Tuvalde çizim modu. Issue #574: `matrixRow` satır kutusu, `matrixCell` (isteğe bağlı) hücre kutusu çizer.
 * Issue #575: `slot` cevap kutusu çizer.
 */
export type CanvasSelectionMode =
  | 'passage'
  | 'question'
  | 'answer'
  | 'dropzone'
  | 'matrixRow'
  | 'matrixCell'
  | 'slot'
  // Issue #576: sürükle-yerleştir hedef kutusu / kart kırpması, eşleştirme sol / sağ öğe kırpması.
  | 'placeTarget'
  | 'placeCard'
  | 'matchLeft'
  | 'matchRight'
  // Issue #581: basılı cevabı öğrenci görselinde örten maske dikdörtgeni.
  | 'mask'
  // Issue #582: metin üzerinde işaretleme — "satır ekle" için satır kutusu.
  | 'textLine'
  // Issue #587: boyama bölgesi — sürükleyerek dikdörtgen / elips, ya da köşe köşe tıklayarak çokgen.
  | 'paintRegion'
  | 'paintPolygon';

/** Issue #576: kutu çizimi soru bölgesinde zorunlu olan modlar (boş alanda çizilmez). */
const BOX_MODES: ReadonlySet<CanvasSelectionMode> = new Set<CanvasSelectionMode>([
  'answer',
  'paintRegion',
  'paintPolygon',
  'dropzone',
  'matrixRow',
  'matrixCell',
  'slot',
  'placeTarget',
  'placeCard',
  'matchLeft',
  'matchRight',
  'mask',
  'textLine',
]);

/**
 * Issue #581: basılı cevap maskesi — soru bölgesine göreli (sayfa pikseli). Sunucu kırpılan soru görselinin üstüne opak
 * beyaz boyar; öğrenci basılı cevabı görmez. Bölge başına en çok `MAX_MASKS_PER_REGION`.
 */
export type MaskBox = PredictionMask;

/** Issue #581: bölge başına maske üst sınırı (sunucu sınırıyla aynı). */
export const MAX_MASKS_PER_REGION = 20;

/**
 * Issue #579: `applyInteractionDraft` sonucu — `applied` yüklendi, `kept` elle yazarlık korundu (taslak bekliyor),
 * `invalid` plan o tipe uymuyor / boş.
 */
export type InteractionDraftApply = 'applied' | 'kept' | 'invalid';

/** Kutuların kapsayan kutusu (boş listede null). */
function boundingBox(boxes: MatrixBox[]): MatrixBox | null {
  if (boxes.length === 0) return null;
  const x = Math.min(...boxes.map((b) => b.x));
  const y = Math.min(...boxes.map((b) => b.y));
  return {
    x,
    y,
    width: Math.max(...boxes.map((b) => b.x + b.width)) - x,
    height: Math.max(...boxes.map((b) => b.y + b.height)) - y,
  };
}

/** MCQ dışı (etkileşimli) tür: şık çizilmez, eğitime (send-to-fix) gönderilmez, doğruluk `interactionSolution`'da. */
export function isInteractiveType(type: QuestionInteractionType): boolean {
  return type !== 'mcq';
}

/** Issue #574: matris yazarlık durumu (soru başına). Kutular soru bölgesine göredir. */
interface MatrixChoiceAuthoringState {
  multiPerRow: boolean;
  columns: MatrixChoiceColumn[];
  rows: Array<{ id: string; label: string; box: MatrixBox; cells: Record<string, MatrixBox> }>;
  solution: MatrixSelections;
  /** Hücre çiziminde yeni kutunun bağlanacağı kolon; her çizimden sonra sıradaki kolona geçer. */
  activeColumnId: string | null;
}

/** Müfettişin matris bölümü (`ActiveInspector.matrix`). */
export interface MatrixInspector {
  multiPerRow: boolean;
  columns: MatrixChoiceColumn[];
  rows: Array<{ id: string; no: number; label: string; solution: string[]; cellCount: number }>;
  activeColumnId: string | null;
  /** En az bir hücre kutusu çizildi. */
  hasCells: boolean;
  /** Tüm satırlarda tüm kolonların hücresi var (öğrenci görsel üstünde dokunur). */
  allCells: boolean;
  missingSolutionCount: number;
  limits: typeof MATRIX_CHOICE_LIMITS;
}

interface DragDropLabelingPlanV1 {
  version: 1;
  type: 'dragDropLabeling';
  dropZones: Array<{ id: string; x: number; y: number; width: number; height: number }>;
  draggables: Array<{ id: string; label: string }>;
}

interface DragDropLabelingAuthoringState {
  labelCount: number;
  dropZones: Array<{ id: string; x: number; y: number; width: number; height: number }>;
  draggables: Array<{ id: string; label: string }>;
  placements: Array<{ draggableId: string; dropZoneId: string }>;
}

/** Gönderim öncesi soru doğrulamasında tek bir sorunun sorunları (`validateQuestions`). */
export interface QuestionValidationIssue {
  /** `regions()` içindeki indeks. */
  index: number;
  /** Kullanıcıya gösterilen 1-tabanlı soru numarası (tuvaldeki `S{no}`, müfettişteki `no`). */
  no: number;
  name: string;
  messages: string[];
}

export interface QuestionValidationOptions {
  /** true: MCQ'da doğru cevap işaretli olmalı (Kaydet). false: yalnız şık sayısı (Eğit). */
  requireCorrectAnswer: boolean;
  /**
   * true: etkileşimli (MCQ dışı) sorular hiç denetlenmez — çağıran onları zaten göndermiyorsa (işaretleme ekranının
   * bankaya kaydı). Varsayılan: Kaydet'te (requireCorrectAnswer) matris soruları denetlenir (#574).
   */
  skipInteractive?: boolean;
}

/** `trained` olayı: eğitim veri setine yazılan sayfa (question-detector `page_id` = sha256(görsel)[:16]). */
export interface TrainedPage {
  pageId: string;
  /** Hiç soru işaretlenmeden (boş sayfa / arka plan) gönderildi. */
  emptyPage: boolean;
  /** Gönderilen soru sayısı. */
  added: number;
}

/**
 * Tuvalde çizilen kapsam (passage). Issue #573: `kind` (0 Okuma metni, 1 Etkinlik) ve `instruction` (etkinlik yönergesi)
 * toplu kayıtta `BulkPassageDto.Kind`/`Instruction` olarak gider; verilmezse backend Okuma metni varsayar.
 */
export interface CanvasPassage {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  showPassageFirst?: boolean;
  kind?: PassageKind | null;
  instruction?: string | null;
}

export interface ActiveInspector {
  index: number;
  no: number;
  region: QuestionRegion;
  name: string;
  interactionType: QuestionInteractionType;
  /** Issue #578: blok türü (Claude tespiti ya da müfettişte elle); varsayılan `mcq_question`. */
  blockKind: BlockKind;
  /** Issue #578: yalnız `activity` bloğunda (A–I). */
  activityComponent: ActivityComponent | null;
  answers: AnswerChoice[];
  warnings: string[];
  dropZones: Array<{ id: string; x: number; y: number; width: number; height: number }>;
  draggables: Array<{ id: string; label: string }>;
  zoneAssignments: Record<string, string>;
  labelCount: number;
  /** Issue #574: yalnız matrixChoice sorusunda dolu. */
  matrix: MatrixInspector | null;
  /** Issue #575: yalnız slotFill sorusunda dolu. */
  slotFill: SlotFillInspector | null;
  /** Issue #576: yalnız dragPlace / match sorusunda dolu. */
  dragPlace: DragPlaceInspector | null;
  match: MatchInspector | null;
  /** Issue #582: yalnız textSelect sorusunda dolu. */
  textSelect: TextSelectInspector | null;
  /** Issue #583: yalnız openText sorusunda dolu. */
  openText: OpenTextInspector | null;
  /** Issue #587: yalnız regionPaint (bölge modu) sorusunda dolu. */
  paintRegions: PaintRegionsInspector | null;
  /** Issue #581: basılı cevap maskeleri (bölgeye göreli). */
  masks: MaskBox[];
  isExample: boolean;
  exampleAnswer: string;
  difficulty: number | null;
  passages: CanvasPassage[];
  selectedPassageId: string;
  passageFirst: boolean;
}
@Component({
  selector: 'app-image-selector',
  standalone: true,
  imports: [
    CommonModule,
    QuillModule,
    MatDividerModule,
    MatIconModule,
    FormsModule,
    MatMenuModule,
    MatButtonModule,
    MatSlideToggleModule,
    MatProgressSpinnerModule,
    QuestionNavigatorComponent,
    QuestionCanvasViewComponent,
    QuestionCanvasViewComponentv5,
    ClassificationSelectorComponent,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(IMAGE_SELECTOR_SCOPE)],
  templateUrl: './image-selector.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./image-selector.component.scss'],
})
export class ImageSelectorComponent {
  @Input() previewMetaProvider?: () => {
    subjectId?: number | null;
    topicId?: number | null;
    subtopicId?: number | null;
  };

  @Output() previewQuestionChange = new EventEmitter<{
    index: number;
    questionId: number;
    subjectId: number | null;
    topicId: number | null;
    subtopicId: number | null;
    classificationSource?: ClassificationSource;
  }>();
  @ViewChild('canvas', { static: false }) canvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('fileInput', { static: false }) fileInput!: ElementRef<HTMLInputElement>;
  @ViewChild('menuTrigger') menuTrigger!: MatMenuTrigger;
  @ViewChild('menuTrigger', { read: ElementRef }) triggerElementRef!: ElementRef;

  private ctx!: CanvasRenderingContext2D | null;
  private img = new Image();

  public autoAlign = signal<boolean>(false);
  public autoMode = signal<boolean>(false);
  /** Sayfa question-detector'da analiz ediliyor (predict). Tuvalin üstünde örtü gösterilir, düzenleme kilitlenir. */
  public inProgress = signal<boolean>(false);
  /** Sayfa eğitim verisi olarak gönderiliyor (sendToFix). Sürerken ikinci gönderim yapılmaz. */
  public readonly sendingToFix = signal(false);
  /** Yeni sayfanın görseli okunuyor (FileReader + img.onload); analiz başlayana kadarki boşluğu kapatır. */
  public readonly pageLoading = signal(false);
  /**
   * Son sayfa okunamadı / çözülemedi. Açıkken `imageData` boştur: eğitime ya da kayda önceki sayfanın
   * görseli gitmez. Yeni yükleme başlayınca kapanır.
   */
  public readonly pageLoadFailed = signal(false);
  /**
   * Bu sayfanın son analizi (predict) hata / zaman aşımı ile bitti. Tuvaldeki soru listesi boşsa bu "boş sayfa"
   * değil "tespit yok" demektir; eğitime boş sayfa olarak gönderilmemeli. Yeni yüklemede ve başarılı analizde kapanır.
   */
  public readonly predictFailed = signal(false);
  /** Son başlatılan analiz; sayfa değiştikten sonra dönen eski yanıt yeni sayfanın bölgelerini ezmesin. */
  private predictSeq = 0;
  /** Son başlatılan sayfa yüklemesi; eski yüklemenin reader/img olayları yenisini ezmesin. */
  private loadSeq = 0;
  /**
   * Üst komponent (question-canvas) kayıt / eğitime gönderim / geçiş sürerken sayfa değiştirmeyi kilitler;
   * tuvalin kenarındaki önceki/sonraki alanları da pasif olur.
   */
  @Input() navigationLocked = false;
  /**
   * Başarılı eğitime gönderimden (sendToFix) sonra bu bileşen kendi `nextImage()`'ı ile ilerlesin mi.
   * Soru tuvali için true (bugünkü davranış); işaretleme modu false verir ve sonraki sayfayı `trained`
   * olayında kendisi seçer.
   */
  @Input() advanceOnTrain = true;
  /** Sayfa eğitim veri setine kaydedildi (send-to-fix başarılı). `pageId` yanıttaki `imageFile`'dır. */
  @Output() trained = new EventEmitter<TrainedPage>();
  /**
   * İşaretleme modu: sayfa yüklenince tespit (predict) kendiliğinden başlamaz, `pageReady` yayılır; üst komponent
   * kaynağı (manuel kayıt / YOLO / Claude) seçip `predict()` ya da `applyExternalPrediction()` çağırır.
   */
  @Input() externalDetection = false;
  /**
   * Issue #581: soru tuvali etkinlik bloklarını yazar — tipli etkinliğin yazarlık uyarıları (matris / kutu / hedef …)
   * tuvalde de gösterilir. İşaretleme ekranında kapalı (orada yazarlık yapılmaz).
   */
  @Input() authorActivities = false;
  /** `externalDetection` açıkken sayfa görseli hazır (değer: `imageData`, FileReader data URL'i). */
  @Output() pageReady = new EventEmitter<string>();
  /** Manuel kayıt exam API'ye yazıldı (değer: kaydedilen sayfanın `imageData`'sı). */
  @Output() manualSaved = new EventEmitter<string>();
  /** Tuvaldeki kutuların kaynağı; eğitime gönderilince manuel kayda başlangıç kaynağı olarak yazılır. */
  public readonly detectionSource = signal<DetectionSource | null>(null);
  /**
   * Issue #579: tuvaldeki bölgeler yeni bir tespitle (ya da yeni sayfayla) baştan kuruldukça artar. Bölge adları her
   * tespitte "Soru 1"den başladığı için üst komponent bölgeye bağlı durumu (etkinlik taslakları) bununla sıfırlar.
   */
  public readonly detectionRevision = signal(0);
  /** Bu sayfanın son YOLO tahmini (eğitime gönderimde Model anlık görüntüsü olarak saklanır). */
  private lastModelPrediction: { imageData: string; list: PredictionList } | null = null;
  /** Son uygulanan tespitin kutuları; `hasUnsavedEdits` bununla karşılaştırır. */
  private appliedSnapshot: string | null = null;
  /** Yok edildi: eğitime gönderim yanıtı geç dönerse snackbar / sayfa geçişi yapılmaz. */
  private destroyed = false;
  public onlyQuestionMode = signal<boolean>(false);
  public answerCount = signal<number>(4);
  public selectionMode = signal<CanvasSelectionMode | null>(null);
  public selectionModeLocked = signal<CanvasSelectionMode | null>(null);
  public passages = signal<CanvasPassage[]>([]);
  public selectedPassageMap = new Map<string, string>(); // 🗂 Soru ID -> Passage ID eşlemesi
  public imageData = signal<string | null>(null); // 🖼️ Base64 formatında resim verisi
  public bookName = signal<string | null>(null);
  public regions = signal<QuestionRegion[]>([]);
  public imageSrc = signal<string | null>(null);
  public imagName = signal<string | null>(null);
  /**
   * Issue #454: seçilen klasörde `question-coordinates.json` bulunduysa açık. Açıkken sayfalar JSON'da
   * geçen görsellerdir ve bölgeler question-detector yerine JSON'dan kurulur.
   */
  public readonly coordinateJsonMode = signal(false);
  /** JSON modunda sayfa kayıtları (anahtar: küçük harfli dosya adı). */
  private coordinatePages = new Map<string, CoordinateQuestion[]>();
  /** Üst üste dosya seçiminde geç biten JSON okumasının yeni seçimi ezmemesi için sayaç. */
  private fileSelectionSeq = 0;
  questionDetectorService = inject(QuestionDetectorService);
  private readonly labelingService = inject(LabelingService);
  questionService = inject(QuestionService);
  testService = inject(TestService);
  private subjectService = inject(SubjectService);
  private snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  /** Scope'a göreli anahtarı senkron çevirir (sözlük scope provider'ı ile yüklenir). */
  private tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${IMAGE_SELECTOR_SCOPE}.${key}`, params) ?? '';
  }
  private dialog = inject(MatDialog);
  private destroyRef = inject(DestroyRef);

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      // Süren sayfa yüklemesi / analizi geçersiz: geç gelen olaylar yok edilmiş komponente dokunmasın.
      this.loadSeq++;
      this.predictSeq++;
    });
  }

  // Cache: topicId -> gradeId (topic carries gradeId)
  private topicGradeIdCache = new Map<number, number>();
  public previewCurrentIndex = signal(0);
  private startX = 0;
  private startY = 0;
  protected isDrawing = false;
  private selectionStarted = false;
  public selectedQuestionIndex = -1;
  /** mousedown anındaki hit — endSelection'da tık/sürükleme ayrımı için. */
  private pendingHit: RegionOrAnswerHit = null;
  /** Sağ panel (Soru Müfettişi) senkronizasyonu için seçili soru bildirimi. */
  @Output() selectedQuestionChange = new EventEmitter<{ index: number; region: QuestionRegion | null }>();
  /** Tuvale yeni bir sayfa/görsel yüklenmeye başladığında yayınlanır (sayfaya özel ebeveyn durumu sıfırlansın). */
  @Output() imageChange = new EventEmitter<void>();

  contextMenuType: 'region' | 'answer' | 'worksheet' | 'dropzone' | 'passage' | 'matrix' | 'slot' | 'place' | 'match' | 'text' = 'region'; // ✅ Sağ tıklama menüsünün türü
  selectedRegion: number | null = null;
  selectedAnswer: number | null = null; // ✅ Seçili cevap indeksi
  selectedDropZoneId: string | null = null;
  /** Issue #574: sağ tıklanan matris satırı / hücresinin kolonu. */
  selectedMatrixRowId: string | null = null;
  selectedMatrixColumnId: string | null = null;
  /** Issue #575: sağ tıklanan cevap kutusu. */
  selectedSlotId: string | null = null;
  /** Issue #576: sağ tıklanan sürükle-yerleştir hedefi / kartı ya da eşleştirme öğesi. */
  selectedPlaceItem: { kind: 'target' | 'card'; id: string } | null = null;
  selectedMatchItem: { side: MatchSide; id: string } | null = null;
  /** Issue #582: sağ tıklanan metin token'ı. */
  selectedTextTokenId: string | null = null;
  previewMode = signal(false); // ✅ Önizleme açma durumu
  currentTestId = signal<number | null>(null); // Test ID'yi sakla

  public exampleAnswers = new Map<string, string>(); // 📜 Her soru için örnek cevapları sakla
  public exampleFlags = new Map<string, boolean>(); // ✅ Her soru için "isExample" flag'ini sakla

  // Interaction authoring (per region.name)
  public interactionTypes = new Map<string, QuestionInteractionType>();
  public labelingState = new Map<string, DragDropLabelingAuthoringState>();
  public matrixState = new Map<string, MatrixChoiceAuthoringState>();
  public slotFillState = new Map<string, SlotFillAuthoringState>();
  public dragPlaceState = new Map<string, DragPlaceAuthoringState>();
  public matchState = new Map<string, MatchAuthoringState>();
  public textSelectState = new Map<string, TextSelectAuthoringState>();
  /** Issue #583: açık uçlu cevap planı + örnek cevap / rubrik. */
  public openTextState = new Map<string, OpenTextAuthoringState>();
  /** Issue #587: görsel üzerinde boyama bölgeleri + çözüm. */
  public paintRegionsState = new Map<string, PaintRegionsAuthoringState>();
  /** Issue #579: bölge + tip → son uygulanan taslaktan sonraki yazarlık izi (elle düzeltme ayrımı). */
  private draftFingerprints = new Map<string, string>();
  /** Issue #581: bölge adı → basılı cevap maskeleri (bölgeye göreli). */
  public masks = new Map<string, MaskBox[]>();
  /**
   * Issue #578: bölge adına göre blok türü (+ etkinlik bileşeni). Kayıt yoksa `mcq_question`. `info_box` / `answer_key` /
   * `noise` kırpılmaz (eğitime, bankaya, teste gitmez); `activity` etkileşimli soru gibi eğitime gitmez.
   */
  public blockKinds = new Map<string, { kind: BlockKind; component: ActivityComponent | null }>();
  /**
   * Issue #578: tuvale uygulanmadan saklanan soru dışı bloklar (soru tuvali yalnız MCQ uygular). Aynı sayfanın manuel
   * kaydına değiştirilmeden eklenir; yeni tespitte ve sayfa değişince silinir.
   */
  private carriedBlocks: { imageData: string | null; blocks: Prediction[] } | null = null;

  // Classification selection for preview mode
  public currentClassification = signal<ClassificationSelection | null>(null);

  public readonly previewDifficultyLevel = computed<number | null>(() => {
    if (!this.previewMode()) {
      return null;
    }

    const regions = this.regions();
    const index = this.previewCurrentIndex();
    const rawValue = regions?.[index]?.difficultyLevel;

    if (typeof rawValue !== 'number' || !Number.isFinite(rawValue)) {
      return null;
    }

    return Math.min(10, Math.max(1, Math.round(rawValue)));
  });

  public readonly previewDifficultyTone = computed<'easy' | 'medium' | 'hard' | null>(() => {
    const level = this.previewDifficultyLevel();

    if (level == null) {
      return null;
    }

    if (level <= 3) {
      return 'easy';
    }

    if (level <= 7) {
      return 'medium';
    }

    return 'hard';
  });

  private currentX = 0;
  private currentY = 0;

  warningIconHeight = 48;

  imageFiles: File[] = [];
  currentImageIndex = 0;
  currentImageSrc: string | null = null;

  contextMenuVisible = false;
  contextMenuX = 0;
  contextMenuY = 0;
  selectedPassageId: string | null = null;
  warningMarkers: WarningMarker[] = [];
  selectedWarning: WarningMarker | null = null;
  warningMenuVisible = false;
  warningMenuX = 0;
  warningMenuY = 0;

  // Sayfa içinde herhangi bir yere tıklandığında menüyü kapat
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    console.log('Entered onDocumentClick. IsDrawing :', this.isDrawing);
    const target = event.target as HTMLElement;
    // Eğer tıklanan yer menünün içi değilse menüyü kapat
    if (!target.closest('.custom-context-menu')) {
      this.contextMenuVisible = false;
    }

    this.warningMenuVisible = false;
    this.contextMenuVisible = false;
    console.log('Exited  onDocumentClick. IsDrawing :', this.isDrawing);
  }

  openWarningContextMenu(marker: WarningMarker, event: MouseEvent) {
    event.stopPropagation();
    this.selectedWarning = marker;
    this.warningMenuVisible = true;
    this.warningMenuX = marker.x + 20;
    this.warningMenuY = marker.y;
  }

  dismissWarning(event: MouseEvent) {
    this.stopEvent(event);
    this.warningMenuVisible = false;
  }

  get previewQuestions() {
    let questions: { status: 'correct' | 'incorrect' | 'unknown' }[] = [];
    for (let i = 0; i < this.regions().length; i++) {
      const region = this.regions()[i];
      questions.push({ status: 'unknown' });
    }
    return questions;
  }

  questionSelected(index: number) {
    this.previewCurrentIndex.set(index);
    this.emitPreviewQuestionMeta(index);

    // Load classification for the selected question
    const region = this.regions()[index];
    if (region) {
      const toNullable = (x: unknown): number | null => {
        const n = Number(x);
        return Number.isFinite(n) && n > 0 ? n : null;
      };

      const subjectId = toNullable(region.subjectId);
      const classificationSource = region.classificationSource ?? ClassificationSource.Human;
      const topicId = toNullable(region.topicId);
      const subtopicIds = (region.subtopicIds ?? (region.subtopicId ? [region.subtopicId] : []))
        .map((id) => Number(id) || 0)
        .filter((id) => id > 0);

      const cachedGradeId = topicId ? (this.topicGradeIdCache.get(topicId) ?? null) : null;

      // Set immediately so UI updates on every question change.
      // If gradeId is missing, ClassificationSelectorComponent will auto-resolve it.
      this.currentClassification.set({
        gradeId: cachedGradeId,
        subjectId,
        topicId,
        subtopicIds,
        classificationSource, // Sınıflandırma kaynağını da geçelim
      });

      // Also try to resolve gradeId here so initialSelection is complete (gradeId non-null).
      // This avoids extra work in the selector and makes grade chips preselect instantly.
      if (!cachedGradeId && subjectId && topicId) {
        this.subjectService.getTopicsBySubject(subjectId).subscribe({
          next: (topics) => {
            const match = topics.find((t) => t.id === topicId);
            const gradeId = match?.gradeId ?? null;
            if (!gradeId) return;

            this.topicGradeIdCache.set(topicId, gradeId);

            // Only apply if user is still on the same question.
            if (this.previewCurrentIndex() !== index) return;

            const current = this.currentClassification();
            this.currentClassification.set({
              gradeId,
              subjectId: current?.subjectId ?? subjectId,
              topicId: current?.topicId ?? topicId,
              subtopicIds: current?.subtopicIds ?? subtopicIds,
              classificationSource: current?.classificationSource ?? classificationSource,
            });
          },
          error: () => {
            // Ignore; selector can still attempt to resolve.
          },
        });
      }
    }
  }

  onClassificationChange(selection: ClassificationSelection) {
    const current = this.currentClassification();
    const sameArray = (a: number[] = [], b: number[] = []) => a.length === b.length && a.every((v, i) => v === b[i]);

    if (
      current &&
      current.gradeId === selection.gradeId &&
      current.subjectId === selection.subjectId &&
      current.topicId === selection.topicId &&
      sameArray(current.subtopicIds ?? [], selection.subtopicIds ?? [])
    ) {
      return;
    }

    this.currentClassification.set(selection);

    // Cache gradeId resolved by selector for faster subsequent preselect.
    if (selection.topicId && selection.gradeId) {
      this.topicGradeIdCache.set(selection.topicId, selection.gradeId);
    }

    // Keep outer (QuestionCanvas) comboboxes in sync (single subtopic expectation)
    const index = this.previewCurrentIndex();
    const region = this.regions()[index];
    if (!region) return;

    this.previewQuestionChange.emit({
      index,
      questionId: region.id,
      subjectId: selection.subjectId,
      topicId: selection.topicId,
      subtopicId: selection.subtopicIds?.length ? selection.subtopicIds[0] : null,
      classificationSource: ClassificationSource.Human,
    });
  }

  onApplyClassification(evt: {
    questionId: number;
    correctAnswerId: number;
    scale: number;
    subjectId: number | null;
    topicId: number | null;
    subtopicIds?: number[];
    subtopicId?: number | null;
  }) {
    const normalizedSubtopicIds = (evt.subtopicIds ?? (evt.subtopicId ? [evt.subtopicId] : []))
      .map((id) => Number(id) || 0)
      .filter((id) => id > 0);

    this.questionService
      .updateClassification(evt.questionId, evt.subjectId, evt.topicId, null, normalizedSubtopicIds)
      .subscribe({
        next: () => {
          this.snackBar.open(this.tr('messages.classificationApplied'), this.tr('common.close'), { duration: 2000 });
        },
        error: () => {
          this.snackBar.open(this.tr('messages.classificationError'), this.tr('common.close'), { duration: 3000 });
        },
      });
  }

  private emitPreviewQuestionMeta(index: number) {
    const region = this.regions()[index];
    if (!region) {
      return;
    }

    const toNullable = (x: any): number | null => {
      const n = Number(x);
      return Number.isFinite(n) && n > 0 ? n : null;
    };

    this.previewQuestionChange.emit({
      index,
      questionId: region.id,
      subjectId: toNullable(region.subjectId),
      topicId: toNullable(region.topicId),
      subtopicId: toNullable(region.subtopicId),
    });
  }

  public togglePreviewMode(testId: number) {
    if (!this.previewMode()) {
      this.previewMode.set(true);
      this.previewCurrentIndex.set(0);
      this.currentTestId.set(testId); // Test ID'yi sakla
      // get questions and sets region
      this.questionService
        .getAll(testId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (response) => {
            const regions = this.testService.convertQuestionsToRegions(response);
            this.regions.set(regions);
            this.questionSelected(0);
          },
          error: (err) => {
            console.error('Önizleme yüklenemedi:', err);
            this.snackBar.open(this.tr('messages.previewLoadError'), this.tr('common.ok'), { duration: 3000 });
            this.previewMode.set(false);
            this.currentTestId.set(null);
            this.loadCurrentImage();
          },
        });
    } else {
      this.resetRegions();
      this.previewMode.set(!this.previewMode());
      this.currentTestId.set(null); // Test ID'yi temizle
      this.loadCurrentImage();
    }
  }

  getRegionOrAnswerAtPosition(x: number, y: number): RegionOrAnswerHit {
    // Passage hit-test first (so passage context menu works even if it overlaps other drawings)
    for (const passage of this.passages()) {
      if (x >= passage.x && x <= passage.x + passage.width && y >= passage.y && y <= passage.y + passage.height) {
        return {
          type: 'passage',
          value: {
            passageId: String(passage.id),
          },
        };
      }
    }

    const regions = this.regions();

    for (let qi = 0; qi < regions.length; qi++) {
      const region = regions[qi];

      // DragDropLabeling: check drop-zones first
      if (this.getInteractionType(region.name) === 'dragDropLabeling') {
        const plan = this.buildInteractionPlan(region.name);
        const dropZones = plan?.dropZones ?? [];
        for (const z of dropZones) {
          const zx1 = region.x + z.x;
          const zy1 = region.y + z.y;
          const zx2 = zx1 + z.width;
          const zy2 = zy1 + z.height;
          if (x >= zx1 && x <= zx2 && y >= zy1 && y <= zy2) {
            return {
              type: 'dropzone',
              value: {
                questionIndex: qi,
                dropZoneId: z.id,
              },
            };
          }
        }
      }

      // Issue #574: matris — önce hücre, sonra satır kutusu.
      if (this.getInteractionType(region.name) === MATRIX_CHOICE) {
        const matrix = this.ensureMatrixState(region.name);
        const inside = (b: MatrixBox) =>
          x >= region.x + b.x && x <= region.x + b.x + b.width && y >= region.y + b.y && y <= region.y + b.y + b.height;
        for (const row of matrix.rows) {
          for (const [columnId, cell] of Object.entries(row.cells)) {
            if (inside(cell)) {
              return { type: 'matrixCell', value: { questionIndex: qi, rowId: row.id, columnId } };
            }
          }
        }
        for (const row of matrix.rows) {
          if (inside(row.box)) {
            return { type: 'matrixRow', value: { questionIndex: qi, rowId: row.id } };
          }
        }
      }

      // Issue #575: cevap kutuları (sonra çizilen üstte).
      if (this.getInteractionType(region.name) === SLOT_FILL) {
        const slots = this.ensureSlotFillState(region.name).slots;
        for (let si = slots.length - 1; si >= 0; si--) {
          const b = slots[si].box;
          if (x >= region.x + b.x && x <= region.x + b.x + b.width && y >= region.y + b.y && y <= region.y + b.y + b.height) {
            return { type: 'slot', value: { questionIndex: qi, slotId: slots[si].id } };
          }
        }
      }

      // Issue #576: sürükle-yerleştir — önce kartlar (genelde hedeflerin içinde küçük), sonra hedefler; eşleştirme öğeleri.
      const inBox = (b: InteractionBox | null) =>
        !!b && x >= region.x + b.x && x <= region.x + b.x + b.width && y >= region.y + b.y && y <= region.y + b.y + b.height;
      if (this.getInteractionType(region.name) === DRAG_PLACE) {
        const place = this.ensureDragPlaceState(region.name);
        const card = [...place.cards].reverse().find((c) => inBox(c.box));
        if (card) return { type: 'placeCard', value: { questionIndex: qi, cardId: card.id } };
        const target = [...place.targets].reverse().find((t) => inBox(t.box));
        if (target) return { type: 'placeTarget', value: { questionIndex: qi, targetId: target.id } };
      }
      if (this.getInteractionType(region.name) === MATCH) {
        const match = this.ensureMatchState(region.name);
        for (const side of ['right', 'left'] as const) {
          const item = [...match[side]].reverse().find((i) => inBox(i.box));
          if (item) return { type: 'matchItem', value: { questionIndex: qi, side, itemId: item.id } };
        }
      }
      // Issue #582: metin token'ları (tirelenmiş kelimenin her kutusu aynı token).
      if (this.getInteractionType(region.name) === TEXT_SELECT) {
        const token = this.ensureTextSelectState(region.name).tokens.find((t) => t.boxes.some((b) => inBox(b)));
        if (token) return { type: 'textToken', value: { questionIndex: qi, tokenId: token.id } };
      }
      // Issue #587: boyama bölgesi (iç içe bölgelerde en küçüğü).
      if (this.getInteractionType(region.name) === REGION_PAINT) {
        const area = paintRegionAt(this.ensurePaintRegionsState(region.name), x - region.x, y - region.y);
        if (area) return { type: 'paintRegion', value: { questionIndex: qi, regionId: area.id } };
      }

      // Önce answer'ları kontrol edelim
      for (let ai = 0; ai < region.answers.length; ai++) {
        const answer = region.answers[ai];

        const answerX = answer.x;
        const answerY = answer.y;

        // Eğer answer koordinatları region’a göre relative ise:
        // const answerX = region.x + answer.x;
        // const answerY = region.y + answer.y;

        if (x >= answerX && x <= answerX + answer.width && y >= answerY && y <= answerY + answer.height) {
          return {
            type: 'answer',
            value: {
              questionIndex: qi,
              answerIndex: ai,
            },
          };
        }
      }

      // Answer yok ama region içindeyse
      if (x >= region.x && x <= region.x + region.width && y >= region.y && y <= region.y + region.height) {
        return {
          type: 'question',
          value: {
            questionIndex: qi,
            answerIndex: null,
          },
        };
      }
    }

    return null;
  }

  onRightClick(event: MouseEvent) {
    console.log('Entered onRightClick. IsDrawing :', this.isDrawing);
    event.preventDefault();

    const { x: clickX, y: clickY } = this.toCanvasPoint(event);

    const hit = this.getRegionOrAnswerAtPosition(clickX, clickY);
    const menuWidth = 260;
    const menuMaxHeight = 320;
    this.contextMenuX =
      event.clientX + menuWidth > window.innerWidth ? Math.max(8, event.clientX - menuWidth) : event.clientX;
    this.contextMenuY =
      event.clientY + menuMaxHeight > window.innerHeight
        ? Math.max(8, window.innerHeight - menuMaxHeight - 8)
        : event.clientY;

    if (hit === null) {
      this.contextMenuType = 'worksheet';
      this.selectedRegion = null;
      this.selectedAnswer = null;
      this.selectedDropZoneId = null;
      console.log('Exited  onRightClick hit === null. IsDrawing :', this.isDrawing);
      this.selectedPassageId = null;
      this.contextMenuVisible = true;
      return;
    }

    if (hit.type === 'passage') {
      this.contextMenuType = 'passage';
      this.selectedPassageId = hit.value.passageId;
      this.selectedRegion = null;
      this.selectedAnswer = null;
      this.selectedDropZoneId = null;
      this.contextMenuVisible = true;
      return;
    }

    if (hit.type === 'answer') {
      this.contextMenuType = 'answer';
      this.selectedAnswer = hit.value.answerIndex;
      this.selectedRegion = hit.value.questionIndex;
      this.selectedPassageId = null;
      this.selectedDropZoneId = null;
    } else if (hit.type === 'dropzone') {
      this.contextMenuType = 'dropzone';
      this.selectedRegion = hit.value.questionIndex;
      this.selectedAnswer = null;
      this.selectedDropZoneId = hit.value.dropZoneId;
      this.selectedPassageId = null;
    } else if (hit.type === 'matrixRow' || hit.type === 'matrixCell') {
      this.contextMenuType = 'matrix';
      this.selectedRegion = hit.value.questionIndex;
      this.selectedAnswer = null;
      this.selectedDropZoneId = null;
      this.selectedPassageId = null;
      this.selectedMatrixRowId = hit.value.rowId;
      this.selectedMatrixColumnId = hit.type === 'matrixCell' ? hit.value.columnId : null;
    } else if (hit.type === 'slot') {
      this.contextMenuType = 'slot';
      this.selectedRegion = hit.value.questionIndex;
      this.selectedAnswer = null;
      this.selectedDropZoneId = null;
      this.selectedPassageId = null;
      this.selectedSlotId = hit.value.slotId;
    } else if (hit.type === 'placeTarget' || hit.type === 'placeCard') {
      this.contextMenuType = 'place';
      this.selectedRegion = hit.value.questionIndex;
      this.selectedAnswer = null;
      this.selectedDropZoneId = null;
      this.selectedPassageId = null;
      this.selectedPlaceItem =
        hit.type === 'placeTarget' ? { kind: 'target', id: hit.value.targetId } : { kind: 'card', id: hit.value.cardId };
    } else if (hit.type === 'matchItem') {
      this.contextMenuType = 'match';
      this.selectedRegion = hit.value.questionIndex;
      this.selectedAnswer = null;
      this.selectedDropZoneId = null;
      this.selectedPassageId = null;
      this.selectedMatchItem = { side: hit.value.side, id: hit.value.itemId };
    } else if (hit.type === 'textToken') {
      this.contextMenuType = 'text';
      this.selectedRegion = hit.value.questionIndex;
      this.selectedAnswer = null;
      this.selectedDropZoneId = null;
      this.selectedPassageId = null;
      this.selectedTextTokenId = hit.value.tokenId;
    } else if (hit.type === 'question' || hit.type === 'paintRegion') {
      // Issue #587: boyama bölgesinin kendi menüsü yok (müfettişten düzenlenir); soru menüsü açılır.
      this.contextMenuType = 'region';
      this.selectedPassageId = null;
      this.selectedRegion = hit.value.questionIndex;
      this.selectedAnswer = null;
      this.selectedDropZoneId = null;
    }
    console.log('Exited  onRightClick hit.type === question. IsDrawing :', this.isDrawing);

    this.contextMenuVisible = true;

    if (this.selectedRegion != null && this.selectedRegion >= 0 && this.selectedRegion < this.regions().length) {
      this.selectedQuestionIndex = this.selectedRegion;
      this.selectedQuestionChange.emit({
        index: this.selectedRegion,
        region: this.regions()[this.selectedRegion] ?? null,
      });
      this.drawImage();
    }
  }

  closeContextMenu() {
    this.contextMenuVisible = false;
    this.selectedRegion = null; // Seçili bölgeyi sıfırla
    this.selectedAnswer = null; // Seçili cevabı sıfırla
    this.selectedDropZoneId = null;
    this.selectedPassageId = null;
    this.selectedMatrixRowId = null;
    this.selectedMatrixColumnId = null;
    this.selectedSlotId = null;
    this.selectedPlaceItem = null;
    this.selectedMatchItem = null;
    this.selectedTextTokenId = null;
  }

  stopEvent(event?: MouseEvent) {
    event?.stopPropagation();
    event?.preventDefault();
  }

  handleMenuAction(action: string, event?: MouseEvent) {
    console.log('Selected action:', action);
    this.stopEvent(event);
    if (action === 'removeQuestion') {
      this.removeQuestion(this.selectedRegion!);
    } else if (action === 'removeAnswer') {
      this.removeAnswer(this.selectedRegion!, this.selectedAnswer!);
    } else if (action === 'selectQuestion') {
      this.selectionMode.set('question');
    } else if (action === 'selectAnswerMode') {
      this.selectAnswerMode(this.selectedRegion!);
    } else if (action === 'selectDropZoneMode') {
      this.selectDropZoneMode(this.selectedRegion!);
    } else if (action === 'removeDropZone') {
      const idx = this.selectedRegion ?? -1;
      if (idx >= 0 && this.selectedDropZoneId) {
        const regionName = this.regions()[idx]?.name;
        if (regionName) {
          this.removeDropZone(regionName, this.selectedDropZoneId);
        }
      }
    } else if (action === 'removeMatrixQuestion') {
      if (this.selectedRegion != null && this.selectedRegion >= 0) this.removeQuestion(this.selectedRegion);
    } else if (action === 'selectMatrixRowMode') {
      this.selectMatrixRowMode(this.selectedRegion!);
    } else if (action === 'removeMatrixRow' || action === 'removeMatrixCell') {
      const regionName = this.regions()[this.selectedRegion ?? -1]?.name;
      const rowId = this.selectedMatrixRowId;
      if (regionName && rowId) {
        if (action === 'removeMatrixRow') {
          this.removeMatrixRow(regionName, rowId);
        } else if (this.selectedMatrixColumnId) {
          this.removeMatrixCell(regionName, rowId, this.selectedMatrixColumnId);
        }
      }
    } else if (action === 'selectSlotMode') {
      this.selectSlotFillMode(this.selectedRegion!);
    } else if (action === 'removeSlot') {
      const regionName = this.regions()[this.selectedRegion ?? -1]?.name;
      if (regionName && this.selectedSlotId) this.removeSlot(regionName, this.selectedSlotId);
    } else if (action === 'selectPlaceMode') {
      this.selectDragPlaceMode(this.selectedRegion!, 'placeTarget');
    } else if (action === 'selectMatchMode') {
      this.selectMatchMode(this.selectedRegion!, 'matchLeft');
    } else if (action === 'removePlaceItem') {
      const regionName = this.regions()[this.selectedRegion ?? -1]?.name;
      const item = this.selectedPlaceItem;
      if (regionName && item) {
        if (item.kind === 'target') this.removeDragPlaceTarget(regionName, item.id);
        else this.removeDragPlaceCard(regionName, item.id);
      }
    } else if (action === 'removeMatchItem') {
      const regionName = this.regions()[this.selectedRegion ?? -1]?.name;
      const item = this.selectedMatchItem;
      if (regionName && item) this.removeMatchItemFrom(regionName, item.side, item.id);
    } else if (action === 'removeTextToken') {
      const regionName = this.regions()[this.selectedRegion ?? -1]?.name;
      if (regionName && this.selectedTextTokenId) this.removeTextSelectToken(regionName, this.selectedTextTokenId);
    } else if (action === 'selectTextLineMode') {
      this.selectTextLineMode(this.selectedRegion!);
    } else if (action === 'alignAnswers') {
      this.alignAnswers(this.selectedRegion!, true);
    } else if (action === 'predict') {
      this.predict();
    } else if (action === 'selectPassage') {
      this.selectionMode.set('passage');
    } else if (action === 'removePassage') {
      if (this.selectedPassageId) {
        this.removePassageById(this.selectedPassageId);
      }
    } else if (action === 'toggleShowPassageFirst') {
      if (this.selectedPassageId) {
        this.toggleShowPassageFirstForPassage(this.selectedPassageId);
      }
    } else if (action === 'removeAll') {
      this.removeAllQuestions();
    }

    this.contextMenuVisible = false;
  }

  private removePassageById(passageId: string) {
    const normalizedId = String(passageId);
    // Remove passage region
    this.passages.set(this.passages().filter((p) => String(p.id) !== normalizedId));

    // Unassign from questions and clear the flag
    const nextRegions = this.regions().map((r) => {
      if (String(r.passageId) !== normalizedId) return r;

      // Keep selectedPassageMap consistent
      this.selectedPassageMap.delete(r.name);

      return {
        ...r,
        passageId: '0',
        showPassageFirst: false,
      };
    });

    this.regions.set(nextRegions);
    this.drawImage();
  }

  public toggleShowPassageFirstForPassage(passageId: string) {
    const normalizedId = String(passageId);
    const passages = this.passages();
    const target = passages.find((p) => String(p.id) === normalizedId);
    if (!target) return;

    const currentlyEnabled = !!target.showPassageFirst;
    const desired = !currentlyEnabled;

    // 1) Persist on passage itself (menu label uses this)
    this.passages.set(passages.map((p) => (String(p.id) === normalizedId ? { ...p, showPassageFirst: desired } : p)));

    // 2) Propagate to any questions linked to this passage
    const nextRegions = this.regions().map((r) => {
      if (String(r.passageId) !== normalizedId) return r;
      return {
        ...r,
        showPassageFirst: desired,
      };
    });

    this.regions.set(nextRegions);
    this.drawImage();
  }

  /** Issue #573: kapsam türü (Okuma metni / Etkinlik). Okuma metnine dönülünce yönerge korunur ama gönderilmez. */
  public setPassageKind(passageId: string, kind: PassageKind) {
    const normalizedId = String(passageId);
    this.passages.set(this.passages().map((p) => (String(p.id) === normalizedId ? { ...p, kind } : p)));
  }

  /** Issue #573: etkinlik yönergesi (en çok 2000 karakter; fazlası kırpılır). */
  public setPassageInstruction(passageId: string, instruction: string) {
    const normalizedId = String(passageId);
    const value = (instruction ?? '').slice(0, PASSAGE_INSTRUCTION_MAX_LENGTH);
    this.passages.set(this.passages().map((p) => (String(p.id) === normalizedId ? { ...p, instruction: value } : p)));
  }

  /** Toplu kayıt gövdesindeki kapsam: tür ve (yalnız etkinlikte) yönerge. */
  private toBulkPassage(p: CanvasPassage) {
    const box = this.roundBox(p);
    const kind = p.kind === PassageKind.Activity ? PassageKind.Activity : PassageKind.Reading;
    const instruction = kind === PassageKind.Activity ? (p.instruction ?? '').trim() || null : null;
    return { ...box, kind, instruction };
  }

  public isPassageFirstEnabledForPassage(passageId: string | null): boolean {
    if (!passageId) return false;
    const normalizedId = String(passageId);
    return this.passages().some((p) => String(p.id) === normalizedId && !!p.showPassageFirst);
  }

  public ensureDraggables(regionName: string) {
    const state = this.ensureLabelingState(regionName);
    if (state.draggables.length > 0) return;
    this.generateDraggables(regionName);
  }

  public setZoneSolutionFromMenu(regionName: string, dropZoneId: string, draggableId: string) {
    this.setZoneSolution(regionName, dropZoneId, draggableId);
    this.drawImage();
  }

  public removeDropZone(regionName: string, dropZoneId: string) {
    const state = this.ensureLabelingState(regionName);
    state.dropZones = state.dropZones.filter((z) => z.id !== dropZoneId);
    state.placements = state.placements.filter((p) => p.dropZoneId !== dropZoneId);
    this.labelingState.set(regionName, { ...state });
    this.drawImage();
  }

  handleFilesInput2(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const files = Array.from(input.files);
      const seq = ++this.fileSelectionSeq;
      const coordinateFile = files.find((file) => isCoordinateJsonFile(file));
      if (coordinateFile) {
        void this.startCoordinateJsonMode(coordinateFile, files, seq);
        return;
      }
      this.startNormalMode(files);
    }
  }

  /** Bugünkü davranış: klasördeki tüm görseller, detector ile (issue #454 kriter 7). */
  private startNormalMode(files: File[]) {
    this.disableCoordinateJsonMode();
    this.imageFiles = files
      .filter((file) => file.type.startsWith('image/'))
      .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }));
    this.openFirstPage();
  }

  private openFirstPage() {
    const firstFolder = this.extractBookName(this.imageFiles[0]);
    this.bookName.set(firstFolder);
    this.currentImageIndex = 0;
    this.loadCurrentImage();
  }

  private disableCoordinateJsonMode() {
    this.coordinateJsonMode.set(false);
    this.coordinatePages = new Map();
  }

  /**
   * Issue #454: JSON'u okuyup doğrular; kullanılabilir kayıt yoksa normal moda düşer. Atlanan kayıtlar ve
   * JSON'da olup klasörde bulunmayan görseller snackbar ile bildirilir.
   */
  private async startCoordinateJsonMode(jsonFile: File, files: File[], seq: number): Promise<void> {
    let text: string | null = null;
    if (jsonFile.size <= COORDINATE_JSON_MAX_BYTES) {
      try {
        text = await jsonFile.text();
      } catch {
        text = null;
      }
    }
    if (seq !== this.fileSelectionSeq) {
      return; // bu arada başka bir seçim yapıldı
    }

    const parsed = text === null ? null : parseCoordinateJson(text);
    if (!parsed || parsed.invalidJson || parsed.validCount === 0) {
      this.notify('coordinateJson.unusable');
      this.startNormalMode(files);
      return;
    }

    const selection = selectCoordinateImageFiles(files, parsed.pages);
    if (selection.files.length === 0) {
      this.notify('coordinateJson.noMatchingImages');
      this.startNormalMode(files);
      return;
    }

    this.coordinatePages = parsed.pages;
    this.coordinateJsonMode.set(true);
    this.imageFiles = selection.files;

    const notes = [
      this.tr('coordinateJson.activated', { questions: parsed.validCount, pages: selection.files.length }),
    ];
    if (parsed.skippedCount > 0) {
      notes.push(this.tr('coordinateJson.skippedEntries', { count: parsed.skippedCount }));
    }
    if (selection.missing.length > 0) {
      const shown = selection.missing.slice(0, 5).join(', ');
      const more = selection.missing.length > 5 ? ` (+${selection.missing.length - 5})` : '';
      notes.push(
        this.tr('coordinateJson.missingImages', { count: selection.missing.length, names: `${shown}${more}` })
      );
    }
    const hasProblems = parsed.skippedCount > 0 || selection.missing.length > 0;
    this.snackBar.open(notes.join(' '), this.tr('common.close'), { duration: hasProblems ? 10000 : 4000 });

    this.openFirstPage();
  }

  private notify(key: string) {
    this.snackBar.open(this.tr(key), this.tr('common.close'), { duration: 5000 });
  }

  /** Issue #454: JSON modunda "Hizala" — detector'a gitmeden mevcut bölgelerin şıklarını hizalar. */
  public alignAllAnswers() {
    const count = this.regions().length;
    for (let i = 0; i < count; i++) {
      this.alignAnswers(i, true);
    }
    this.regions.set([...this.regions()]);
    this.recomputeWarnings();
    this.drawImage();
  }

  /** Issue #454: JSON modunda sayfanın bölgelerini detector'ı çağırmadan JSON'dan kurar. */
  private applyCoordinateRegions(fileName: string) {
    const questions = this.coordinatePages.get(coordinateFileKey(fileName)) ?? [];
    this.regions.set(
      buildRegionsFromCoordinates(questions, { width: this.img.naturalWidth, height: this.img.naturalHeight })
    );
    this.recomputeWarnings();
  }

  /**
   * Issue #454: JSON modunda kullanıcı müfettiş panelinde bir sorunun sınıflandırmasını değiştirirse o
   * sorunun soru bazında kaydedilecek sınıflandırmasını günceller (kaynak AI kalır). JSON modu kapalıyken
   * hiçbir şey yapmaz (bugünkü davranış).
   */
  public updateRegionClassification(
    index: number,
    selection: { subjectId: number | null; topicId: number | null; subtopicIds: number[] }
  ) {
    if (!this.coordinateJsonMode()) {
      return;
    }
    const regions = this.regions();
    const region = regions[index];
    if (!region) {
      return;
    }
    const subtopicIds = selection.subtopicIds.filter((id) => Number.isInteger(id) && id > 0);
    // Kullanıcı kararı: JSON modundaki sorular Gemini'ye hiç gitmesin (consumer yalnız "AI"yı atlar),
    // bu yüzden elle düzeltmede de kaynak AI kalır.
    const updated: QuestionRegion = {
      ...region,
      subjectId: selection.subjectId ?? undefined,
      topicId: selection.topicId ?? undefined,
      subtopicId: subtopicIds[0],
      subtopicIds,
      classificationSource: ClassificationSource.AI,
      classification: {
        subjectId: selection.subjectId,
        topicId: selection.topicId,
        subTopicIds: subtopicIds,
        source: 'AI',
      },
    };
    const next = [...regions];
    next[index] = updated;
    this.regions.set(next);
  }

  private extractBookName(file: File | undefined): string | null {
    if (!file) {
      return null;
    }

    const relativePath = file.webkitRelativePath?.trim();
    if (relativePath) {
      const [folderName] = relativePath.split('/').filter(Boolean);
      if (folderName) {
        return folderName;
      }
    }

    return null;
  }

  loadCurrentImage() {
    this.resetRegions();
    const file = this.imageFiles[this.currentImageIndex];
    if (!file) return;
    this.imageChange.emit();
    // Önceki sayfanın süren analizi artık geçersiz: yanıtı yok sayılır, örtü kalkar.
    this.cancelPendingPredict();
    const load = this.startPageLoad();
    const reader = new FileReader();
    this.imagName.set(file.name);
    if (!this.bookName()) {
      this.bookName.set(this.extractBookName(file));
    }
    this.regions.set([]);

    reader.onerror = () => this.failPageLoad(load);
    reader.onload = () => {
      if (load !== this.loadSeq) {
        return;
      }
      //this.currentImageSrc = reader.result as string;
      this.imageSrc.set(reader.result as string);
      this.imageData.set(reader.result as string); // 📂 Resmi base64 olarak sakla
      // Her yüklemeye ayrı Image: src değişince eskisinin olayları (iptal/error) yeni yüklemeye karışmasın.
      this.img = new Image();
      this.img.onerror = () => this.failPageLoad(load);
      this.img.src = this.imageSrc()!;

      this.img.onload = () => {
        if (load !== this.loadSeq) {
          return;
        }
        this.pageLoading.set(false);
        if (this.selectionModeLocked() !== 'answer') {
          if (this.coordinateJsonMode()) {
            this.applyCoordinateRegions(file.name);
          } else if (this.externalDetection) {
            this.pageReady.emit(this.imageData()!);
          } else {
            this.predict();
          }
        } else {
          this.regions.set([]);
          const region = {
            name: 'Soru',
            x: 0,
            y: 0,
            width: this.img.width - 1,
            height: this.img.height - 1,
            answers: [],
            passageId: '0',
            imageId: '',
            imageUrl: '',
            id: 0,
            isExample: false,
            exampleAnswer: null,
          };
          this.regions.set([...this.regions(), region]);
          this.selectQuestion(0);
        }
        this.drawImage();
      };
    };
    reader.readAsDataURL(file); // 📦 Sadece o anki resmi belleğe yükle
  }

  /** Yeni sayfa yüklemesini başlatır; dönen sıra numarası olay işleyicilerinde eski yüklemeyi ayırt eder. */
  private startPageLoad(): number {
    this.pageLoading.set(true);
    this.pageLoadFailed.set(false);
    this.predictFailed.set(false);
    this.detectionSource.set(null);
    this.lastModelPrediction = null;
    this.appliedSnapshot = null;
    this.detectionRevision.update((n) => n + 1);
    return ++this.loadSeq;
  }

  /** Görsel okunamadı / çözülemedi: meşgul durumu kapanır, kullanıcı bilgilendirilir (eski yüklemeyse yok sayılır). */
  private failPageLoad(load: number) {
    if (load !== this.loadSeq) {
      return;
    }
    this.pageLoading.set(false);
    this.pageLoadFailed.set(true);
    // Görsel çözülemediyse reader'ın yazdığı veri de geçersiz; tuvalde eski sayfa kalmasın, gönderilecek görsel yok.
    this.imageData.set(null);
    this.imageSrc.set(null);
    this.snackBar.open(this.tr('messages.pageLoadError'), this.tr('common.ok'), { duration: 4000 });
  }

  /**
   * Tek bir görseli (klasör listesi olmadan) tuvale yükler; işaretleme modu sayfaları kendisi seçtiği için
   * bunu kullanır. Koordinat JSON modu kapanır, sayfa her zamanki gibi analiz edilir.
   */
  public loadFile(file: File) {
    this.fileSelectionSeq++;
    this.disableCoordinateJsonMode();
    this.imageFiles = [file];
    this.currentImageIndex = 0;
    this.bookName.set(this.extractBookName(file));
    this.loadCurrentImage();
  }

  goToPage(pageStr: string) {
    const page = parseInt(pageStr, 10);
    if (isNaN(page) || this.isDrawing) return;
    this.currentImageIndex = Math.max(0, Math.min(page - 1, this.imageFiles.length - 1));
    this.loadCurrentImage();
  }

  nextImage() {
    if (this.isDrawing) {
      return;
    }
    if (this.imageFiles.length === 0) return;

    this.currentImageIndex++;
    if (this.currentImageIndex >= this.imageFiles.length) {
      this.currentImageIndex = 0; // döngüsel olarak başa sar
    }
    this.loadCurrentImage();
  }

  previousImage() {
    if (this.isDrawing) {
      return;
    }
    if (this.imageFiles.length === 0) return;

    this.currentImageIndex--;
    if (this.currentImageIndex < 0) {
      this.currentImageIndex = this.imageFiles.length - 1; // döngüsel olarak sona sar
    }
    this.loadCurrentImage();
  }

  handleFileInput(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];

    if (file) {
      this.fileSelectionSeq++;
      this.disableCoordinateJsonMode();
      this.imageChange.emit();
      this.cancelPendingPredict();
      const load = this.startPageLoad();
      const reader = new FileReader();
      this.imagName.set(file.name);
      this.bookName.set(this.extractBookName(file));
      reader.onerror = () => this.failPageLoad(load);
      reader.onload = (e: ProgressEvent<FileReader>) => {
        if (load !== this.loadSeq) {
          return;
        }
        this.imageSrc.set(e.target?.result as string);
        this.imageData.set(e.target?.result as string); // 📂 Resmi base64 olarak sakla
        // Her yüklemeye ayrı Image: src değişince eskisinin olayları (iptal/error) yeni yüklemeye karışmasın.
        this.img = new Image();
        this.img.onerror = () => this.failPageLoad(load);
        this.img.src = this.imageSrc()!;
        this.img.onload = () => {
          if (load !== this.loadSeq) {
            return;
          }
          this.pageLoading.set(false);
          if (this.selectionModeLocked() !== 'answer') {
            if (this.externalDetection) {
              this.pageReady.emit(this.imageData()!);
            } else {
              this.predict();
            }
          } else {
            this.regions.set([]);
            const region = {
              name: 'Soru',
              x: 0,
              y: 0,
              width: this.img.width - 1,
              height: this.img.height - 1,
              answers: [],
              passageId: '0',
              imageId: '',
              imageUrl: '',
              id: 0,
              isExample: false,
              exampleAnswer: null,
            };
            this.regions.set([...this.regions(), region]);
            this.selectQuestion(0);
          }
          this.drawImage();
        };
      };

      reader.readAsDataURL(file); // ✅ Resmi base64 formatına çevir
      this.regions.set([]);
    }
  }

  drawImage2() {
    if (!this.canvas) return;
    const canvasEl = this.canvas.nativeElement;
    this.ctx = canvasEl.getContext('2d');
    if (this.ctx) {
      canvasEl.width = this.img.width;
      canvasEl.height = this.img.height;
      this.ctx.drawImage(this.img, 0, 0);

      // Tüm bölgeleri tekrar çiz
      this.drawRegions();
    }
  }

  toggleAlignMode() {
    this.autoAlign.set(!this.autoAlign());
  }

  lockSelectionMode(mode: CanvasSelectionMode | null) {
    this.selectionModeLocked.set(mode);
    this.toggleSelectionMode(mode);
  }

  toggleOnlyQuestionMode() {
    this.onlyQuestionMode.set(!this.onlyQuestionMode());
  }

  tapCorrectAnswer(event: MouseEvent) {
    console.log('Entered tapCorrectAnswer.IsDrawing :', this.isDrawing);
    if (this.isDrawing) {
      console.log('Exited  tapCorrectAnswer isDrawing :', this.isDrawing);
      return;
    }
    // Issue #581: maske çoğu zaman basılı cevabın (şıkkın) üstüne çizilir; çizim bitişindeki tık doğru cevabı değiştirmesin.
    if (this.selectionMode() === 'mask') {
      return;
    }

    const { x: mouseX, y: mouseY } = this.toCanvasPoint(event);

    for (const region of this.regions()) {
      for (const answer of region.answers) {
        if (
          mouseX >= answer.x &&
          mouseX <= answer.x + answer.width &&
          mouseY >= answer.y &&
          mouseY <= answer.y + answer.height
        ) {
          const questionIndex = this.regions().findIndex((q) => q.name === region.name);
          const answerIndex = region.answers.findIndex((a) => a.label === answer.label);
          this.setCorrectAnswer(questionIndex, answerIndex, 1);
          this.selectedRegion = questionIndex;
          this.selectedQuestionIndex = questionIndex;
          this.selectedQuestionChange.emit({ index: questionIndex, region: this.regions()[questionIndex] ?? null });
          this.drawImage();
        }
      }
    }
    console.log('Exited  tapCorrectAnswer.IsDrawing :', this.isDrawing);
  }

  onMouseMove(event: MouseEvent) {
    if (!this.isDrawing) {
      const canvas = this.canvas.nativeElement;
      const ctx = canvas.getContext('2d');
      if (!ctx) {
        return;
      }

      // Tüm resmi yeniden çiz
      this.drawImage();

      const { x: mouseX, y: mouseY } = this.toCanvasPoint(event);

      for (const question of this.regions()) {
        for (const answer of question.answers) {
          if (
            mouseX >= answer.x &&
            mouseX <= answer.x + answer.width &&
            mouseY >= answer.y &&
            mouseY <= answer.y + answer.height
          ) {
            // Transparan yeşil ile dikdörtgeni doldur
            ctx.fillStyle = 'rgba(0, 255, 0, 0.3)';
            ctx.fillRect(answer.x, answer.y, answer.width, answer.height);
            break;
          }
        }
      }

      return;
    }

    const current = this.toCanvasPoint(event);
    this.currentX = current.x;
    this.currentY = current.y;
    this.drawTemporaryRectangle();
  }

  drawTemporaryRectangle() {
    if (!this.ctx) return;
    const canvas = this.canvas.nativeElement;

    this.ctx.clearRect(0, 0, canvas.width, canvas.height);

    // Tüm çizimleri tekrar çiz (önceden çizilen dikdörtgenler kaybolmasın)
    this.drawImage();

    const width = this.currentX - this.startX;
    const height = this.currentY - this.startY;

    this.ctx.strokeStyle = this.getStrokeStyle();
    this.ctx.lineWidth = 2;
    this.ctx.strokeRect(this.startX, this.startY, width, height);
  }

  getStrokeStyle() {
    switch (this.selectionMode()) {
      case 'passage':
        return 'purple';
      case 'question':
        return 'red';
      case 'answer':
        return 'blue';
      case 'dropzone':
        return 'orange';
      case 'matrixRow':
        return this.getThemeColors().matrixRow;
      case 'matrixCell':
        return this.getThemeColors().matrixCell;
      case 'slot':
        return this.getThemeColors().slotBox;
      case 'placeTarget':
        return this.getThemeColors().placeTarget;
      case 'placeCard':
        return this.getThemeColors().placeCard;
      case 'matchLeft':
        return this.getThemeColors().matchLeft;
      case 'matchRight':
        return this.getThemeColors().matchRight;
      case 'mask':
        return this.getThemeColors().mask;
      default:
        return 'black';
    }
  }

  /** Şık sayısı gibi ayar değişince tüm bölgelerin uyarılarını tazele. */
  public recomputeWarnings() {
    this.regions().forEach((region) => this.checkRegionWarning(region));
    this.drawImage();
  }

  checkRegionWarning(region: QuestionRegion) {
    if (this.warningMarkers.find((w) => w.id === region.id)) {
      // remove warning
      this.warningMarkers = this.warningMarkers.filter((w) => w.id !== region.id);
    }
    // Uyarı ikonu her zaman doğru cevap kontrolünü de gösterir; dragDrop uyarıları da yalnız görseldir.
    const type = this.getInteractionType(region.name);
    // Issue #578: soru dışı blokta (Claude / işaretleme ekranı türü) uyarı yok: anlatım, gürültü, tipsiz etkinlikte şık kuralı
    // yok; tipli etkinliğin yazarlığı bu ekranlarda yapılmaz (#581), boş yazarlık durumu hata sayılmaz. Soru tuvalinde
    // tipi elle seçilen sorular (tür: Soru) uyarılarını göstermeye devam eder.
    // Issue #581: soru tuvalinde (`authorActivities`) tipli etkinlik yazarlığı yapılır; uyarıları da görünür.
    const kind = this.getBlockKind(region.name);
    const authoredActivity = this.authorActivities && kind === 'activity' && isInteractiveType(type);
    const messages = kind !== 'mcq_question' && !authoredActivity
      ? []
      : type === 'dragDropLabeling'
        ? this.dragDropIssues(region)
        : type === MATRIX_CHOICE
          ? [...this.matrixIssues(region.name), ...this.matrixNotices(region.name)]
          : type === SLOT_FILL
            ? this.slotFillMessages(region.name)
            : type === DRAG_PLACE
              ? this.dragPlaceMessages(region.name)
              : type === MATCH
                ? this.matchMessages(region.name)
                : type === TEXT_SELECT
                  ? this.textSelectMessages(region.name)
                  : type === OPEN_TEXT
                    ? this.openTextMessages(region.name)
                    : type === REGION_PAINT
                      ? this.paintRegionsMessages(region.name)
                    : this.mcqIssues(region, { requireCorrectAnswer: true });

    if (messages.length > 0) {
      this.warningMarkers.push({
        id: region.id,
        x: region.x + region.width - this.warningIconHeight / 2, // mat-icon width
        y: region.y - this.warningIconHeight / 2,
        messages: messages,
        type: 'error',
      });
    }
  }

  /** MCQ kuralları — uyarı ikonu ve gönderim öncesi doğrulamanın ortak kaynağı. */
  private mcqIssues(region: QuestionRegion, opts: QuestionValidationOptions): string[] {
    const messages: string[] = [];
    const answerCount = this.answerCount();
    if ((region.answers?.length ?? 0) !== answerCount) {
      messages.push(this.tr('warnings.answerCountMismatch', { count: answerCount }));
    }
    if (opts.requireCorrectAnswer && !region.answers?.some((a) => a.isCorrect)) {
      messages.push(this.tr('warnings.noCorrectAnswer'));
    }
    return messages;
  }

  /** dragDropLabeling uyarıları — yalnız ikon için; gönderimi engellemez. */
  private dragDropIssues(region: QuestionRegion): string[] {
    const messages: string[] = [];
    const plan = this.buildInteractionPlan(region.name);
    if (!plan || plan.dropZones.length === 0) {
      messages.push(this.tr('warnings.noDropZone'));
    }
    if (!plan || plan.draggables.length === 0) {
      messages.push(this.tr('warnings.noDraggables'));
    }
    if (region.isExample) {
      const placements = this.getInteractionType(region.name) === 'dragDropLabeling'
        ? this.ensureLabelingState(region.name).placements
        : [];
      if (placements.length === 0) {
        messages.push(this.tr('warnings.noSolutionPlacements'));
      }
    }
    return messages;
  }

  /**
   * Issue #574: matris kuralları — backend aynı kuralla reddeder (2..10 kolon, 1..60 satır, her satırın çözümü). Kaydet'te
   * engelleyici; uyarı ikonunda da görünür.
   */
  private matrixIssues(regionName: string): string[] {
    const state = this.ensureMatrixState(regionName);
    const limits = MATRIX_CHOICE_LIMITS;
    const messages: string[] = [];
    if (state.rows.length < limits.minRows) {
      messages.push(this.tr('warnings.matrixNoRows'));
    }
    if (state.rows.length > limits.maxRows) {
      messages.push(this.tr('warnings.matrixTooManyRows', { max: limits.maxRows }));
    }
    if (state.columns.length < limits.minColumns || state.columns.length > limits.maxColumns) {
      messages.push(this.tr('warnings.matrixColumnCount', { min: limits.minColumns, max: limits.maxColumns }));
    }
    if (state.columns.some((c) => !c.label.trim())) {
      messages.push(this.tr('warnings.matrixEmptyColumnLabel'));
    }
    const missing = state.rows.filter((r) => !(state.solution[r.id]?.length)).length;
    if (missing > 0) {
      messages.push(this.tr('warnings.matrixMissingSolution', { count: missing }));
    }
    return messages;
  }

  /** Matris bilgi notları — gönderimi engellemez (eksik hücre kutusu: öğrenci listeden seçer). */
  private matrixNotices(regionName: string): string[] {
    const state = this.ensureMatrixState(regionName);
    const cellCount = state.rows.reduce((sum, r) => sum + Object.keys(r.cells).length, 0);
    const expected = state.rows.length * state.columns.length;
    return cellCount > 0 && cellCount < expected ? [this.tr('warnings.matrixPartialCells')] : [];
  }

  /**
   * Gönderim (Eğit / Kaydet) öncesi soru bazında engelleyici sorunlar.
   * MCQ kuralları her zaman; matris kuralları yalnız Kaydet'te (requireCorrectAnswer) — etkileşimli sorular Eğit'e hiç
   * gönderilmez. dragDropLabeling uyarıları kasıtlı olarak engelleyici değildir (yalnız ikonda görünür).
   */
  public validateQuestions(opts: QuestionValidationOptions): QuestionValidationIssue[] {
    const issues: QuestionValidationIssue[] = [];
    this.regions().forEach((region, index) => {
      const type = this.getInteractionType(region.name);
      // Issue #578: etkileşimsiz soru dışı bloklar (anlatım, cevap anahtarı, gürültü, tipsiz etkinlik) hiçbir yere soru
      // olarak gitmez; denetlenmez.
      if (this.getBlockKind(region.name) !== 'mcq_question' && !isInteractiveType(type)) {
        return;
      }
      if (isInteractiveType(type)) {
        if (type === MATRIX_CHOICE && opts.requireCorrectAnswer && !opts.skipInteractive) {
          const matrixMessages = this.matrixIssues(region.name);
          if (matrixMessages.length > 0) {
            issues.push({ index, no: index + 1, name: region.name, messages: matrixMessages });
          }
        }
        // Issue #575: cevap kutusu kuralları da yalnız Kaydet'te (kutusu / doğru değeri eksik soru kaydedilmez).
        if (type === SLOT_FILL && opts.requireCorrectAnswer && !opts.skipInteractive) {
          const slotMessages = this.slotFillMessages(region.name);
          if (slotMessages.length > 0) {
            issues.push({ index, no: index + 1, name: region.name, messages: slotMessages });
          }
        }
        // Issue #576: sürükle-yerleştir / eşleştirme kuralları da yalnız Kaydet'te.
        if (
          (type === DRAG_PLACE || type === MATCH || type === TEXT_SELECT || type === OPEN_TEXT || type === REGION_PAINT) &&
          opts.requireCorrectAnswer &&
          !opts.skipInteractive
        ) {
          const messages =
            type === DRAG_PLACE
              ? this.dragPlaceMessages(region.name)
              : type === MATCH
                ? this.matchMessages(region.name)
                : type === TEXT_SELECT
                  ? this.textSelectMessages(region.name)
                  : type === REGION_PAINT
                    ? this.paintRegionsMessages(region.name)
                    : this.openTextMessages(region.name);
          if (messages.length > 0) {
            issues.push({ index, no: index + 1, name: region.name, messages });
          }
        }
        return;
      }
      const messages = this.mcqIssues(region, opts);
      if (messages.length > 0) {
        issues.push({ index, no: index + 1, name: region.name, messages });
      }
    });
    return issues;
  }

  /**
   * `validateQuestions` sonucunu kullanıcıya bildirir: sorun varsa snackbar gösterir, ilk sorunlu
   * soruyu seçer ve false döner. Sorun yoksa (ya da hiç soru yoksa) true.
   */
  public ensureQuestionsValid(opts: QuestionValidationOptions): boolean {
    const issues = this.validateQuestions(opts);
    if (issues.length === 0) {
      return true;
    }
    const shownLimit = 3;
    const lines = issues
      .slice(0, shownLimit)
      .map((i) => this.tr('warnings.questionIssue', { no: i.no, messages: i.messages.join(', ') }));
    if (issues.length > shownLimit) {
      lines.push(this.tr('warnings.moreQuestions', { count: issues.length - shownLimit }));
    }
    this.snackBar.open(lines.join(' · '), this.tr('common.close'), { duration: 6000 });
    this.selectQuestion(issues[0].index);
    return false;
  }

  private cssVar(name: string): string {
    if (typeof getComputedStyle === 'undefined') return '';
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  }

  /** Çizimde kullanılan token değerleri — bir kez çözümlenip cache'lenir (sıcak yol). */
  private themeColors?: {
    accent: string;
    label: string;
    fontFamily: string;
    /** Issue #574: matris satırı / hücresi / doğru hücre dolgusu ve etiket zemini. */
    matrixRow: string;
    matrixCell: string;
    matrixSolution: string;
    surface: string;
    /** Issue #575: cevap kutusu çerçevesi / eksik değerli kutu. */
    slotBox: string;
    slotMissing: string;
    /** Issue #576: sürükle-yerleştir hedefi / kartı, eşleştirme sol / sağ öğesi. */
    placeTarget: string;
    placeCard: string;
    matchLeft: string;
    matchRight: string;
    /** Issue #582: metin token'ı / hedef token'ı / boşluk (gap). */
    textToken: string;
    textTarget: string;
    textGap: string;
    /** Issue #578: blok türü renkleri ve tür etiketinin metni. */
    kinds: Record<BlockKind, string>;
    kindOn: string;
    /** Issue #581: basılı cevap maskesinin tarama çizgisi. */
    mask: string;
  };

  private getThemeColors() {
    if (!this.themeColors) {
      this.themeColors = {
        accent: this.cssVar('--ms-action-border') || this.cssVar('--primaryColor') || 'red',
        label: this.cssVar('--ms-text-strong') || 'white',
        fontFamily: this.cssVar('--mainFontFamily') || 'sans-serif',
        matrixRow: this.cssVar('--ms-colors-code-turquoise') || 'teal',
        matrixCell: this.cssVar('--ms-colors-code-orange') || 'darkorange',
        matrixSolution: this.cssVar('--ms-colors-status-success') || 'green',
        surface: this.cssVar('--ms-colors-white') || 'white',
        slotBox: this.cssVar('--ms-colors-code-purple') || 'purple',
        slotMissing: this.cssVar('--ms-colors-status-error') || 'crimson',
        placeTarget: this.cssVar('--ms-colors-code-blue') || 'steelblue',
        placeCard: this.cssVar('--ms-colors-code-sand') || 'sienna',
        matchLeft: this.cssVar('--ms-colors-code-green') || 'olivedrab',
        matchRight: this.cssVar('--ms-colors-code-yellow') || 'goldenrod',
        textToken: this.cssVar('--ms-colors-code-blue') || 'steelblue',
        textTarget: this.cssVar('--ms-colors-status-success') || 'green',
        textGap: this.cssVar('--ms-colors-code-orange') || 'darkorange',
        kinds: {
          mcq_question: this.cssVar('--ms-block-kind-question') || 'red',
          activity: this.cssVar('--ms-block-kind-activity') || 'steelblue',
          info_box: this.cssVar('--ms-block-kind-info') || 'goldenrod',
          answer_key: this.cssVar('--ms-block-kind-answer-key') || 'purple',
          noise: this.cssVar('--ms-block-kind-noise') || 'gray',
        },
        kindOn: this.cssVar('--ms-block-kind-on') || 'white',
        mask: this.cssVar('--ms-danger-border') || 'crimson',
      };
    }
    return this.themeColors;
  }

  /** Aktif bölgeyi (Soru Müfettişi seçimi) canvas görünür alanına kaydır. */
  scrollActiveRegionIntoView() {
    const region = this.currentRegion;
    const canvasEl = this.canvas?.nativeElement;
    if (!region || !canvasEl) return;

    const scroller = canvasEl.closest(
      '.qc-canvas__stage, .ms-canvas-wrapper, .form-column-4'
    ) as HTMLElement | null;
    if (!scroller) return;

    const scale = canvasEl.clientWidth / (canvasEl.width || 1) || 1;
    const top = region.y * scale;
    const bottom = (region.y + region.height) * scale;

    if (top < scroller.scrollTop || bottom > scroller.scrollTop + scroller.clientHeight) {
      scroller.scrollTo({ top: Math.max(0, top - 48), behavior: 'smooth' });
    }
  }

  /**
   * Tuval CSS ile alana sığdırılır (genişlik), çizim ise resmin gerçek pikselinde kalır.
   * Ekran piksel / tuval piksel oranı; fare koordinatları ve işaretler bununla çevrilir.
   */
  get canvasDisplayScale(): number {
    const canvasEl = this.canvas?.nativeElement;
    if (!canvasEl || !canvasEl.width || !canvasEl.clientWidth) return 1;
    return canvasEl.clientWidth / canvasEl.width;
  }

  /** Fare olayını tuvalin (resmin) piksel koordinatına çevirir. */
  private toCanvasPoint(event: MouseEvent): { x: number; y: number } {
    const canvasEl = this.canvas.nativeElement;
    const rect = canvasEl.getBoundingClientRect();
    const scaleX = rect.width ? canvasEl.width / rect.width : 1;
    const scaleY = rect.height ? canvasEl.height / rect.height : 1;
    return {
      x: (event.clientX - rect.left) * scaleX,
      y: (event.clientY - rect.top) * scaleY,
    };
  }

  drawImage() {
    if (!this.canvas) return;
    const canvasEl = this.canvas.nativeElement;
    this.ctx = canvasEl.getContext('2d');
    if (this.ctx) {
      canvasEl.width = this.img.width;
      canvasEl.height = this.img.height;
      this.ctx.drawImage(this.img, 0, 0);

      // 🟪 **Passage alanlarını mor renkte çiz**
      for (const passage of this.passages()) {
        this.ctx.strokeStyle = 'purple';
        this.ctx.lineWidth = 2;
        this.ctx.strokeRect(passage.x, passage.y, passage.width, passage.height);
      }

      // 🔴 **Soru alanlarını çiz — aktif bölge vurgulu**
      const activeIndex = this.activeRegionIndex;
      const theme = this.getThemeColors();
      const accent = theme.accent;
      this.regions().forEach((region, i) => {
        if (!this.ctx) return;
        const isActive = i === activeIndex;
        // Issue #578: soru dışı bloklar türünün rengiyle, etiketi her zaman görünür çizilir.
        const kind = this.getBlockKind(region.name);
        if (kind !== 'mcq_question') {
          this.drawKindBox(region, i, kind, isActive);
          this.checkRegionWarning(region);
          return;
        }

        if (isActive) {
          this.ctx.save();
          this.ctx.globalAlpha = 0.1;
          this.ctx.fillStyle = accent;
          this.ctx.fillRect(region.x, region.y, region.width, region.height);
          this.ctx.restore();

          this.ctx.strokeStyle = accent;
          this.ctx.lineWidth = 4;
        } else {
          this.ctx.strokeStyle = theme.kinds.mcq_question;
          this.ctx.globalAlpha = 0.4;
          this.ctx.lineWidth = 2;
        }
        this.ctx.strokeRect(region.x, region.y, region.width, region.height);
        this.ctx.globalAlpha = 1;

        if (isActive) {
          const label = `S${i + 1}`;
          this.ctx.font = `bold 15px ${theme.fontFamily}`;
          const textW = this.ctx.measureText(label).width;
          this.ctx.fillStyle = accent;
          this.ctx.fillRect(region.x, Math.max(0, region.y - 20), textW + 12, 20);
          this.ctx.fillStyle = theme.label;
          this.ctx.fillText(label, region.x + 6, Math.max(11, region.y - 6));
        }

        this.checkRegionWarning(region);
      });

      // 🟦 **Şık alanlarını mavi renkte çiz**
      if (!this.onlyQuestionMode()) {
        for (const region of this.regions()) {
          // Etkileşimli sorularda (sürükle-bırak, matris) ve soru dışı bloklarda (#578) şık çizimi gerekmiyor
          if (isInteractiveType(this.getInteractionType(region.name)) || this.getBlockKind(region.name) !== 'mcq_question') {
            continue;
          }

          for (const answer of region.answers) {
            if (answer.isCorrect) {
              this.ctx.fillStyle = 'rgba(0, 255, 0, 0.3)';
              this.ctx.fillRect(answer.x, answer.y, answer.width, answer.height);
            }
            // this.ctx.strokeStyle = 'blue';
            // this.ctx.lineWidth = 2;
            // this.ctx.strokeRect(answer.x, answer.y, answer.width, answer.height);
            const borderRadius = 0; // Math.min(answer.width, answer.height) * 0.3; // ✅ Yuvarlak köşe oranı
            this.ctx.beginPath();
            this.ctx.roundRect(answer.x, answer.y, answer.width, answer.height, borderRadius);
            this.ctx.closePath();

            this.ctx.strokeStyle = 'blue'; // ✅ Mavi Kenarlık
            this.ctx.lineWidth = 2;
            this.ctx.stroke();
          }
        }
      }

      // 🟧 **DragDropLabeling drop-zone'larını turuncu çiz**
      for (const region of this.regions()) {
        if (this.getInteractionType(region.name) !== 'dragDropLabeling') {
          continue;
        }

        const plan = this.buildInteractionPlan(region.name);
        const dropZones = plan?.dropZones ?? [];
        for (const z of dropZones) {
          this.ctx.strokeStyle = 'orange';
          this.ctx.lineWidth = 2;
          this.ctx.strokeRect(region.x + z.x, region.y + z.y, z.width, z.height);
        }
      }

      this.drawMatrixOverlays();
      this.drawSlotFillOverlays();
      this.drawDragPlaceOverlays();
      this.drawMatchOverlays();
      this.drawTextSelectOverlays();
      this.drawPaintRegionOverlays();
      this.drawMaskOverlays();
    }
  }

  /**
   * Issue #581: maske — sunucu bu alanı opak beyaza boyar; tuvalde yarı opak yüzey + çapraz tarama ile gösterilir (altındaki
   * basılı cevap öğretmene hâlâ seçilebilir).
   */
  private drawMaskOverlays() {
    const ctx = this.ctx;
    if (!ctx) return;
    const theme = this.getThemeColors();
    for (const region of this.regions()) {
      const masks = this.masks.get(region.name) ?? [];
      masks.forEach((mask, i) => {
        // Kayıttaki gibi bölgenin yuvarlanmış köşesine göre.
        const x = Math.round(region.x) + mask.x;
        const y = Math.round(region.y) + mask.y;
        ctx.save();
        ctx.globalAlpha = 0.7;
        ctx.fillStyle = theme.surface;
        ctx.fillRect(x, y, mask.width, mask.height);
        ctx.globalAlpha = 0.9;
        ctx.beginPath();
        ctx.rect(x, y, mask.width, mask.height);
        ctx.clip();
        ctx.beginPath();
        ctx.strokeStyle = theme.mask;
        ctx.lineWidth = 1.5;
        const step = 8;
        for (let d = -mask.height; d < mask.width; d += step) {
          ctx.moveTo(x + d, y + mask.height);
          ctx.lineTo(x + d + mask.height, y);
        }
        ctx.stroke();
        ctx.restore();
        ctx.save();
        ctx.setLineDash([4, 3]);
        ctx.strokeStyle = theme.mask;
        ctx.lineWidth = 2;
        ctx.strokeRect(x, y, mask.width, mask.height);
        ctx.setLineDash([]);
        ctx.font = `bold 11px ${theme.fontFamily}`;
        const tag = `M${i + 1}`;
        const tagW = ctx.measureText(tag).width + 8;
        ctx.fillStyle = theme.mask;
        ctx.fillRect(x, y, tagW, 15);
        ctx.fillStyle = theme.surface;
        ctx.fillText(tag, x + 4, y + 11);
        ctx.restore();
      });
    }
  }

  /** Issue #578: soru dışı blok — türün rengiyle çerçeve, açık dolgu ve `S{n} · tür` etiketi. */
  private drawKindBox(region: QuestionRegion, index: number, kind: BlockKind, isActive: boolean) {
    const ctx = this.ctx;
    if (!ctx) return;
    const theme = this.getThemeColors();
    const color = theme.kinds[kind];
    ctx.save();
    ctx.globalAlpha = isActive ? 0.16 : 0.08;
    ctx.fillStyle = color;
    ctx.fillRect(region.x, region.y, region.width, region.height);
    ctx.globalAlpha = isActive ? 1 : 0.75;
    ctx.strokeStyle = color;
    ctx.lineWidth = isActive ? 4 : 2;
    ctx.setLineDash(isExcludedBlockKind(kind) ? [8, 5] : []);
    ctx.strokeRect(region.x, region.y, region.width, region.height);
    ctx.setLineDash([]);
    ctx.globalAlpha = 1;
    const label = `S${index + 1} · ${this.blockKindLabel(region.name)}`;
    ctx.font = `bold ${isActive ? 15 : 13}px ${theme.fontFamily}`;
    const height = isActive ? 20 : 18;
    const textW = ctx.measureText(label).width;
    ctx.fillStyle = color;
    ctx.fillRect(region.x, Math.max(0, region.y - height), textW + 12, height);
    ctx.fillStyle = theme.kindOn;
    ctx.fillText(label, region.x + 6, Math.max(height - 6, region.y - 6));
    ctx.restore();
  }

  /** Tuvaldeki kısa tür etiketi: "Etkinlik · A", "Anlatım", … */
  public blockKindLabel(regionName: string): string {
    const kind = this.getBlockKind(regionName);
    const label = this.tr(`blockKinds.${kind}`);
    const component = kind === 'activity' ? this.getActivityComponent(regionName) : null;
    return component ? `${label} ${component}` : label;
  }

  /** Kutu + köşe etiketi (açık kontur altta: her sayfa renginde seçilebilsin). */
  private drawTaggedBox(
    x: number,
    y: number,
    box: InteractionBox,
    color: string,
    tag: string,
    opts: { dashed?: boolean; bold?: boolean; fill?: number } = {}
  ) {
    const ctx = this.ctx;
    if (!ctx) return;
    const theme = this.getThemeColors();
    ctx.save();
    ctx.globalAlpha = opts.fill ?? 0.1;
    ctx.fillStyle = color;
    ctx.fillRect(x, y, box.width, box.height);
    ctx.globalAlpha = 1;
    ctx.setLineDash([]);
    ctx.strokeStyle = theme.surface;
    ctx.lineWidth = opts.bold ? 7 : 5;
    ctx.strokeRect(x, y, box.width, box.height);
    ctx.setLineDash(opts.dashed ? [6, 4] : []);
    ctx.strokeStyle = color;
    ctx.lineWidth = opts.bold ? 4 : 2;
    ctx.strokeRect(x, y, box.width, box.height);
    ctx.setLineDash([]);
    ctx.font = `bold 11px ${theme.fontFamily}`;
    const tagW = ctx.measureText(tag).width + 8;
    ctx.fillStyle = color;
    ctx.fillRect(x, y - 15, tagW, 15);
    ctx.fillStyle = theme.surface;
    ctx.fillText(tag, x + 4, y - 4);
    ctx.restore();
  }

  /**
   * Issue #576: hedef kutuları (düz, "H n ×kapasite"; seçili hedef kalın) ve kart kırpmaları (kesik, "K n"; seçili hedefin
   * doğru kartı dolgulu).
   */
  private drawDragPlaceOverlays() {
    const theme = this.getThemeColors();
    for (const region of this.regions()) {
      if (this.getInteractionType(region.name) !== DRAG_PLACE) continue;
      const state = this.ensureDragPlaceState(region.name);
      const selectedCards = new Set(
        state.selectedTargetId && Object.hasOwn(state.solution, state.selectedTargetId) ? state.solution[state.selectedTargetId] : []
      );
      state.targets.forEach((target, i) => {
        if (!target.box) return;
        const tag = state.mode === 'order' ? `${i + 1}.` : `H${i + 1}${target.capacity > 1 ? ` ×${target.capacity}` : ''}`;
        this.drawTaggedBox(region.x + target.box.x, region.y + target.box.y, target.box, theme.placeTarget, tag, {
          bold: state.selectedTargetId === target.id,
        });
      });
      state.cards.forEach((card, i) => {
        if (!card.box) return;
        this.drawTaggedBox(region.x + card.box.x, region.y + card.box.y, card.box, theme.placeCard, `K${i + 1}`, {
          dashed: true,
          fill: selectedCards.has(card.id) ? 0.35 : 0.1,
        });
      });
    }
  }

  /** Issue #576: eşleştirme sol ("L n") / sağ ("R n", kesik) kırpmaları ve bağlı çiftlerin arasındaki çizgi. */
  private drawMatchOverlays() {
    const ctx = this.ctx;
    if (!ctx) return;
    const theme = this.getThemeColors();
    for (const region of this.regions()) {
      if (this.getInteractionType(region.name) !== MATCH) continue;
      const state = this.ensureMatchState(region.name);
      const center = (b: InteractionBox) => ({ x: region.x + b.x + b.width / 2, y: region.y + b.y + b.height / 2 });
      state.left.forEach((item, i) => {
        if (item.box) {
          this.drawTaggedBox(region.x + item.box.x, region.y + item.box.y, item.box, theme.matchLeft, `L${i + 1}`, {
            bold: state.selectedLeftId === item.id,
          });
        }
      });
      state.right.forEach((item, i) => {
        if (item.box) {
          this.drawTaggedBox(region.x + item.box.x, region.y + item.box.y, item.box, theme.matchRight, `R${i + 1}`, { dashed: true });
        }
      });
      state.left.forEach((item) => {
        const rightId = Object.hasOwn(state.pairs, item.id) ? state.pairs[item.id] : null;
        const right = state.right.find((r) => r.id === rightId);
        if (!item.box || !right?.box) return;
        const a = center(item.box);
        const b = center(right.box);
        ctx.save();
        ctx.setLineDash(state.prelinked.includes(item.id) ? [2, 6] : []);
        ctx.strokeStyle = theme.surface;
        ctx.lineWidth = 5;
        ctx.beginPath();
        ctx.moveTo(a.x, a.y);
        ctx.lineTo(b.x, b.y);
        ctx.stroke();
        ctx.strokeStyle = theme.matchLeft;
        ctx.lineWidth = 2;
        ctx.stroke();
        ctx.restore();
      });
    }
  }

  /**
   * Issue #582: metin token'ları (ince çerçeve), hedef token'ları (yeşil dolgu, hedef numarası), boşluklar (turuncu kesik; doğru
   * sembol(ler) etikette), seçili token kalın, bekleyen satır kutusu kesik.
   */
  private drawTextSelectOverlays() {
    const ctx = this.ctx;
    if (!ctx) return;
    const theme = this.getThemeColors();
    for (const region of this.regions()) {
      if (this.getInteractionType(region.name) !== TEXT_SELECT) continue;
      const state = this.ensureTextSelectState(region.name);
      const targetNo = new Map<string, number>();
      state.targets.forEach((t, i) => t.tokenIds.forEach((id) => targetNo.set(id, i + 1)));
      const punctuation = state.mode === 'punctuation';
      for (const token of state.tokens) {
        const selected = state.selectedTokenId === token.id;
        const no = targetNo.get(token.id);
        token.boxes.forEach((b, bi) => {
          const x = region.x + b.x;
          const y = region.y + b.y;
          if (token.gap) {
            const accept = Object.hasOwn(state.gapAccept, token.id) ? state.gapAccept[token.id] : [];
            this.drawTaggedBox(x, y, b, theme.textGap, accept.length ? accept.join(' ') : '?', { dashed: !accept.length, bold: selected, fill: 0.15 });
            return;
          }
          const target = !punctuation && no !== undefined;
          ctx.save();
          ctx.globalAlpha = target ? 0.25 : 0.06;
          ctx.fillStyle = target ? theme.textTarget : theme.textToken;
          ctx.fillRect(x, y, b.width, b.height);
          ctx.globalAlpha = 1;
          ctx.strokeStyle = target ? theme.textTarget : theme.textToken;
          ctx.lineWidth = selected ? 3 : 1;
          ctx.strokeRect(x, y, b.width, b.height);
          if (target && bi === 0) {
            ctx.font = `bold 10px ${theme.fontFamily}`;
            const tag = `${no}`;
            const tagW = ctx.measureText(tag).width + 6;
            ctx.fillStyle = theme.textTarget;
            ctx.fillRect(x, y - 12, tagW, 12);
            ctx.fillStyle = theme.surface;
            ctx.fillText(tag, x + 3, y - 3);
          }
          ctx.restore();
        });
      }
      // Çok kelimeli hedef: kelimelerini (satır başına) çevreleyen tek kesik çerçeve — tek hata mı, ayrı hatalar mı görünsün.
      if (!punctuation) {
        const byId = new Map(state.tokens.map((t) => [t.id, t]));
        for (const target of state.targets) {
          if (target.tokenIds.length < 2) continue;
          const perLine = new Map<number, InteractionBox[]>();
          for (const id of target.tokenIds) {
            for (const b of byId.get(id)?.boxes ?? []) {
              const key = Math.round(b.y);
              perLine.set(key, [...(perLine.get(key) ?? []), b]);
            }
          }
          ctx.save();
          ctx.setLineDash([5, 3]);
          ctx.strokeStyle = theme.textTarget;
          ctx.lineWidth = 2;
          for (const boxes of perLine.values()) {
            const frame = boundingBox(boxes);
            if (frame) ctx.strokeRect(region.x + frame.x - 3, region.y + frame.y - 3, frame.width + 6, frame.height + 6);
          }
          ctx.restore();
        }
      }
      if (state.pendingLine) {
        const b = state.pendingLine;
        this.drawTaggedBox(region.x + b.x, region.y + b.y, b, theme.textToken, '+', { dashed: true, fill: 0.08 });
      }
    }
  }

  /**
   * Issue #575: cevap kutuları — numara + tür etiketi; doğru değeri / ayarı eksik kutu kesik kırmızı, seçili kutu kalın.
   * Renkler temadan (`getThemeColors`); açık kontur her sayfa renginde seçilebilsin diye altta.
   */
  private drawSlotFillOverlays() {
    const ctx = this.ctx;
    if (!ctx) return;
    const theme = this.getThemeColors();
    const kindTag: Record<SlotKind, string> = { number: '123', text: 'Aa', select: '▾', palette: '∠' };
    for (const region of this.regions()) {
      if (this.getInteractionType(region.name) !== SLOT_FILL) continue;
      const state = this.ensureSlotFillState(region.name);
      state.slots.forEach((slot, index) => {
        const x = region.x + slot.box.x;
        const y = region.y + slot.box.y;
        const missing = slotIssues(slot).length > 0;
        const selected = state.selectedSlotId === slot.id;
        const color = missing ? theme.slotMissing : theme.slotBox;
        ctx.save();
        ctx.globalAlpha = 0.12;
        ctx.fillStyle = color;
        ctx.fillRect(x, y, slot.box.width, slot.box.height);
        ctx.globalAlpha = 1;
        ctx.setLineDash([]);
        ctx.strokeStyle = theme.surface;
        ctx.lineWidth = selected ? 7 : 5;
        ctx.strokeRect(x, y, slot.box.width, slot.box.height);
        ctx.setLineDash(missing ? [6, 4] : []);
        ctx.strokeStyle = color;
        ctx.lineWidth = selected ? 4 : 2;
        ctx.strokeRect(x, y, slot.box.width, slot.box.height);
        ctx.setLineDash([]);
        ctx.font = `bold 11px ${theme.fontFamily}`;
        const tag = `${index + 1} ${kindTag[slot.kind]}`;
        const tagW = ctx.measureText(tag).width + 8;
        ctx.fillStyle = color;
        ctx.fillRect(x, y - 15, tagW, 15);
        ctx.fillStyle = theme.surface;
        ctx.fillText(tag, x + 4, y - 4);
        ctx.restore();
      });
    }
  }

  /**
   * Issue #574: matris satırları (düz, numara etiketli), hücreleri (kalın kesik çizgi + açık zemin üstünde kolon etiketi) ve
   * doğru hücreler (dolgu). Renkler temadan (`getThemeColors`); hücre çizgisinin altına açık bir kontur çizilir ki her
   * sayfa renginde seçilebilsin.
   */
  private drawMatrixOverlays() {
    const ctx = this.ctx;
    if (!ctx) return;
    const theme = this.getThemeColors();
    for (const region of this.regions()) {
      if (this.getInteractionType(region.name) !== MATRIX_CHOICE) continue;
      const state = this.ensureMatrixState(region.name);
      const labels = new Map(state.columns.map((c) => [c.id, c.label]));
      state.rows.forEach((row, ri) => {
        const rx = region.x + row.box.x;
        const ry = region.y + row.box.y;
        ctx.save();
        ctx.setLineDash([]);
        ctx.strokeStyle = theme.matrixRow;
        ctx.lineWidth = 2;
        ctx.strokeRect(rx, ry, row.box.width, row.box.height);
        ctx.font = `bold 12px ${theme.fontFamily}`;
        const tag = `${ri + 1}`;
        const tagW = ctx.measureText(tag).width + 8;
        ctx.fillStyle = theme.matrixRow;
        ctx.fillRect(rx - tagW, ry, tagW, 16);
        ctx.fillStyle = theme.surface;
        ctx.fillText(tag, rx - tagW + 4, ry + 12);

        const solution = state.solution[row.id] ?? [];
        for (const [columnId, cell] of Object.entries(row.cells)) {
          const cx = region.x + cell.x;
          const cy = region.y + cell.y;
          ctx.globalAlpha = solution.includes(columnId) ? 0.35 : 0.12;
          ctx.fillStyle = solution.includes(columnId) ? theme.matrixSolution : theme.matrixCell;
          ctx.fillRect(cx, cy, cell.width, cell.height);
          ctx.globalAlpha = 1;
          ctx.setLineDash([]);
          ctx.strokeStyle = theme.surface;
          ctx.lineWidth = 5;
          ctx.strokeRect(cx, cy, cell.width, cell.height);
          ctx.setLineDash([6, 4]);
          ctx.strokeStyle = solution.includes(columnId) ? theme.matrixSolution : theme.matrixCell;
          ctx.lineWidth = 3;
          ctx.strokeRect(cx, cy, cell.width, cell.height);
          ctx.setLineDash([]);
          const text = labels.get(columnId) || columnId;
          ctx.font = `bold 11px ${theme.fontFamily}`;
          const textW = ctx.measureText(text).width + 6;
          ctx.fillStyle = theme.surface;
          ctx.fillRect(cx + 2, cy + 2, textW, 14);
          ctx.fillStyle = theme.matrixCell;
          ctx.fillText(text, cx + 5, cy + 13);
        }
        ctx.restore();
      });
    }
  }

  setPassageForQuestion(questionId: string, passageId: string) {
    const normalizedPassageId = String(passageId);
    this.selectedPassageMap.set(questionId, normalizedPassageId);

    const passage = this.passages().find((p) => String(p.id) === normalizedPassageId);
    const inheritedShowPassageFirst = normalizedPassageId !== '0' ? !!passage?.showPassageFirst : false;

    // 🔄 Soruların içindeki passageId'yi güncelle
    const updatedRegions = this.regions().map((region) => {
      if (region.name === questionId) {
        return {
          ...region,
          passageId: normalizedPassageId, // ✅ Yeni passageId ile güncelle
          showPassageFirst: inheritedShowPassageFirst,
        };
      }
      return region;
    });

    this.regions.set(updatedRegions); // 📌 Güncellenmiş listeyi kaydet
    this.drawImage();
  }

  onPassageChange(event: Event, questionId: string) {
    const selectElement = event.target as HTMLSelectElement;
    const passageId = selectElement.value;
    this.setPassageForQuestion(questionId, passageId);
  }

  drawRegions() {
    if (!this.ctx) return;

    // 🟥 **Soru bölgelerini kırmızı çerçeve ile göster**
    for (const region of this.regions()) {
      this.ctx.strokeStyle = 'red';
      this.ctx.lineWidth = 2;
      this.ctx.strokeRect(region.x, region.y, region.width, region.height);
    }

    // 🟦 **Şık bölgelerini mavi çerçeve ile göster**
    for (const region of this.regions()) {
      for (const answer of region.answers) {
        this.ctx.strokeStyle = 'blue';
        this.ctx.lineWidth = 2;
        this.ctx.strokeRect(answer.x, answer.y, answer.width, answer.height);
      }
    }
  }

  startSelection(event: MouseEvent) {
    console.log('Entered startSelection.IsDrawing :', this.isDrawing);
    // Sağ tık ise hiçbir şey yapma
    if (event.button === 2) {
      console.log('Exited startSelection event.button === 2 .IsDrawing :', this.isDrawing);
      return;
    }
    const start = this.toCanvasPoint(event);
    this.startX = start.x;
    this.startY = start.y;
    const hit = this.getRegionOrAnswerAtPosition(this.startX, this.startY);
    this.pendingHit = hit;

    const mode = this.selectionMode();

    // Issue #581: maske basılı cevabın (çoğu zaman bir şıkkın) üstüne çizilir; hedef bölge isabetle değil, başlangıç noktasını
    // içeren bölgeyle belirlenir (önce seçili soru).
    if (mode === 'mask') {
      const index = this.regionIndexAt(this.startX, this.startY);
      if (index < 0) {
        return;
      }
      this.selectedQuestionIndex = index;
      this.isDrawing = true;
      return;
    }

    if (
      hit &&
      (hit.type === 'question' ||
        hit.type === 'matrixRow' ||
        hit.type === 'matrixCell' ||
        hit.type === 'slot' ||
        hit.type === 'placeTarget' ||
        hit.type === 'placeCard' ||
        hit.type === 'matchItem' ||
        hit.type === 'textToken' ||
        hit.type === 'paintRegion')
    ) {
      // Şık / drop-zone / matris çizimi hangi soruya ait olacak — modu ZORLAMADAN sadece hedef soruyu belirle.
      // Mod artık üstteki segmented kontrolden geliyor.
      this.selectedQuestionIndex = hit.value.questionIndex;
    } else if (!hit) {
      // Boş alanda şık / zone / matris kutusu çizilemez.
      if (mode && BOX_MODES.has(mode)) {
        console.log('Exited startSelection hit === null. IsDrawing :', this.isDrawing);
        return;
      }
    }
    if (this.selectionMode()) this.isDrawing = true;
    console.log('Exited startSelection. IsDrawing :', this.isDrawing);
  }

  generateFirstAvailableName() {
    const prefix = 'Soru ';

    const usedNumbers = this.regions()
      .map((r) => r.name)
      .filter((name) => name.startsWith(prefix))
      .map((name) => parseInt(name.replace(prefix, ''), 10))
      .filter((num) => !isNaN(num))
      .sort((a, b) => a - b);

    // 1'den başlayarak ilk boş olan numarayı bul
    let candidate = 1;
    for (let i = 0; i < usedNumbers.length; i++) {
      if (usedNumbers[i] !== candidate) {
        break;
      }
      candidate++;
    }

    return `${prefix}${candidate}`;
  }

  endSelection(event: MouseEvent) {
    console.log('Entered EndSelection.IsDrawing :', this.isDrawing);
    this.stopEvent(event);
    const questionWidthThreshold = 250;
    const questionHeightThreshold = 100;
    const answerHeightThreshold = 20;
    const answerWidthThreshold = 20;
    if (!this.selectionMode() || !this.ctx || !this.isDrawing) return;
    const { x: endX, y: endY } = this.toCanvasPoint(event);
    const width = endX - this.startX;
    const height = endY - this.startY;

    // --- Tık mı sürükleme mi? (~4px) ---
    const isClick = Math.hypot(endX - this.startX, endY - this.startY) < 4;
    const hit = this.pendingHit;

    // Issue #587: çokgen modunda her tık seçili sorunun içinde bir köşe ekler (bölgeye tık köşe sayılır, çözüm değiştirmez).
    if (isClick && this.selectionMode() === 'paintPolygon' && this.selectedQuestionIndex !== -1) {
      this.isDrawing = false;
      this.pendingHit = null;
      this.addPaintPolygonPoint(this.selectedQuestionIndex, this.startX, this.startY);
      return;
    }

    // Bir bölgenin gövdesine tık (veya "Soru" modunda bölge içinde sürükleme) → o bölgeyi SEÇ, çizme.
    if (
      hit &&
      hit.type === 'question' &&
      (isClick || this.selectionMode() === 'question')
    ) {
      this.isDrawing = false;
      this.pendingHit = null;
      this.selectQuestion(hit.value.questionIndex);
      return;
    }

    // Şık üstüne tık → o soruyu seç (doğru cevap ayarını tapCorrectAnswer yapar).
    if (isClick) {
      this.isDrawing = false;
      this.pendingHit = null;
      const mode = this.selectionMode();
      // Issue #581: maske modunda kısa tık çözüm / seçim değiştirmez (hücre, kart, öğe atamaları korunur).
      if (mode === 'mask') {
        this.drawImage();
        return;
      }
      if (hit && hit.type === 'matrixCell' && (mode === 'matrixRow' || mode === 'matrixCell')) {
        // Issue #574: matris modunda hücreye tık = o hücrenin kolonunu satırın çözümü olarak işaretle / kaldır (mod değişmez).
        const regionName = this.regions()[hit.value.questionIndex]?.name;
        if (regionName) this.toggleMatrixSolution(regionName, hit.value.rowId, hit.value.columnId);
        this.focusQuestionKeepingMode(hit.value.questionIndex);
      } else if (hit && hit.type === 'matrixCell') {
        this.selectQuestion(hit.value.questionIndex);
      } else if (hit && hit.type === 'slot') {
        // Issue #575: kutuya tık = müfettişte o kutuyu düzenle (mod değişmez).
        const regionName = this.regions()[hit.value.questionIndex]?.name;
        if (regionName) this.selectSlot(regionName, hit.value.slotId);
        this.focusQuestionKeepingMode(hit.value.questionIndex);
      } else if (hit && hit.type === 'paintRegion') {
        // Issue #587: boyama bölgesine tık = fırçadaki rengi çözüm olarak ver / kaldır (mod değişmez).
        const regionName = this.regions()[hit.value.questionIndex]?.name;
        if (regionName) this.updatePaintRegions(regionName, (s) => togglePaintRegionFill(s, hit.value.regionId));
        this.focusQuestionKeepingMode(hit.value.questionIndex);
      } else if (hit && hit.type === 'textToken') {
        // Issue #582: token'a tık = müfettişte seç; select / correct'te ayrıca hedef işaretle / kaldır (mod değişmez).
        const regionName = this.regions()[hit.value.questionIndex]?.name;
        if (regionName) this.clickTextSelectToken(regionName, hit.value.tokenId);
        this.focusQuestionKeepingMode(hit.value.questionIndex);
      } else if (hit && (hit.type === 'placeTarget' || hit.type === 'placeCard' || hit.type === 'matchItem')) {
        // Issue #576: hedefe tık = çözüm atamasında seç; karta tık = seçili hedefin doğru kartı yap / kaldır;
        // sol öğeye tık = seç; sağ öğeye tık = seçili sola bağla (mod değişmez).
        const regionName = this.regions()[hit.value.questionIndex]?.name;
        if (regionName) this.onAuthoringItemClick(regionName, hit);
        this.focusQuestionKeepingMode(hit.value.questionIndex);
      } else if (hit && (hit.type === 'answer' || hit.type === 'dropzone' || hit.type === 'matrixRow')) {
        this.selectQuestion(hit.value.questionIndex);
      } else {
        this.drawImage();
      }
      return;
    }

    this.pendingHit = null;

    if (this.selectionMode() === 'passage') {
      if (questionWidthThreshold > width || questionHeightThreshold > height) {
        console.log(
          'Exited  EndSelection questionWidthThreshold > width || questionHeightThreshold > height. IsDrawing :',
          this.isDrawing
        );
        this.isDrawing = false;
        this.drawImage();
        return;
      }
      const id = `p${this.passages().length + 1}`;
      this.passages.set([
        ...this.passages(),
        { id, x: this.startX, y: this.startY, width, height, showPassageFirst: false },
      ]);
    } else if (this.selectionMode() === 'question') {
      const name = this.generateFirstAvailableName();
      if (questionWidthThreshold > width || questionHeightThreshold > height) {
        console.log(
          'Exited  EndSelection questionWidthThreshold > width || questionHeightThreshold > height. IsDrawing :',
          this.isDrawing
        );
        this.isDrawing = false;
        this.drawImage();
        return;
      }
      this.regions.set([
        ...this.regions(),
        {
          name,
          x: this.startX,
          y: this.startY,
          width,
          height,
          answers: [],
          passageId: '0',
          imageId: '',
          imageUrl: '',
          id: 0,
          isExample: false,
          exampleAnswer: null,
        },
      ]);
      this.sortRegionsByName();
      this.selectQuestionByName(name);
      /// this.selectQuestion(this.regions().length - 1);
    } else if (this.selectionMode() === 'answer' && this.selectedQuestionIndex !== -1) {
      if (answerWidthThreshold > width || answerHeightThreshold > height) {
        console.log(
          'Exited  EndSelection questionWidthThreshold > width || questionHeightThreshold > height. IsDrawing :',
          this.isDrawing
        );
        this.isDrawing = false;
        this.drawImage();
        return;
      }
      const answerIdx = this.selectedQuestionIndex;
      this.regions()[answerIdx].answers.push({
        label: String.fromCharCode(65 + this.regions()[answerIdx].answers.length),
        x: this.startX,
        y: this.startY,
        width,
        height,
        isCorrect: false,
        id: 0,
        imageUrl: '',
      });
      // Her şık çiziminde harflendirmeyi güncelle (A, B, C, D, E) — son şık da doğru adlansın.
      this.renameAnswers(answerIdx);
      if (this.autoAlign() && this.regions()[answerIdx].answers.length >= 2) {
        this.alignAnswers(answerIdx);
      }
      this.regions.set([...this.regions()]);
    } else if (this.selectionMode() === 'dropzone' && this.selectedQuestionIndex !== -1) {
      const region = this.regions()[this.selectedQuestionIndex];
      const minW = 10;
      const minH = 10;
      if (minW > Math.abs(width) || minH > Math.abs(height)) {
        this.isDrawing = false;
        this.drawImage();
        return;
      }

      // normalize width/height to positive
      const absX1 = Math.min(this.startX, endX);
      const absY1 = Math.min(this.startY, endY);
      const absX2 = Math.max(this.startX, endX);
      const absY2 = Math.max(this.startY, endY);
      const absW = absX2 - absX1;
      const absH = absY2 - absY1;

      // must be inside question region
      const within =
        absX1 >= region.x && absY1 >= region.y && absX2 <= region.x + region.width && absY2 <= region.y + region.height;

      if (!within) {
        this.snackBar.open(this.tr('messages.dropZoneOutsideQuestion'), this.tr('common.ok'), { duration: 2000 });
      } else {
        const relX = absX1 - region.x;
        const relY = absY1 - region.y;
        this.addDropZone(region.name, relX, relY, absW, absH);
      }
    } else if (
      (this.selectionMode() === 'matrixRow' || this.selectionMode() === 'matrixCell') &&
      this.selectedQuestionIndex !== -1
    ) {
      this.addMatrixBoxFromDrag(this.selectedQuestionIndex, this.startX, this.startY, endX, endY);
    } else if (this.selectionMode() === 'mask' && this.selectedQuestionIndex !== -1) {
      this.addMaskFromDrag(this.selectedQuestionIndex, this.startX, this.startY, endX, endY);
    } else if (this.selectionMode() === 'slot' && this.selectedQuestionIndex !== -1) {
      this.addSlotFromDrag(this.selectedQuestionIndex, this.startX, this.startY, endX, endY);
    } else if (this.selectionMode() === 'textLine' && this.selectedQuestionIndex !== -1) {
      this.setTextLineFromDrag(this.selectedQuestionIndex, this.startX, this.startY, endX, endY);
    } else if (this.selectionMode() === 'paintRegion' && this.selectedQuestionIndex !== -1) {
      this.addPaintRegionFromDrag(this.selectedQuestionIndex, this.startX, this.startY, endX, endY);
    } else if (
      (this.selectionMode() === 'placeTarget' ||
        this.selectionMode() === 'placeCard' ||
        this.selectionMode() === 'matchLeft' ||
        this.selectionMode() === 'matchRight') &&
      this.selectedQuestionIndex !== -1
    ) {
      this.addInteractionBoxFromDrag(this.selectionMode()!, this.selectedQuestionIndex, this.startX, this.startY, endX, endY);
    }

    this.isDrawing = false;
    this.drawImage();

    console.log('Exited  EndSelection. IsDrawing :', this.isDrawing);
  }

  toggleSelectionMode(mode: CanvasSelectionMode | null) {
    if (this.selectionModeLocked()) {
      this.selectionMode.set(this.selectionModeLocked());
      return;
    }

    this.selectionMode.set(mode); // TODO: Seçim modunu ayarla

    if (mode === 'answer' && this.selectedQuestionIndex < 0) {
      alert(this.tr('messages.selectQuestionFirst'));
    }
  }

  // -------- Interaction authoring helpers --------
  public getInteractionType(regionName: string): QuestionInteractionType {
    return this.interactionTypes.get(regionName) ?? 'mcq';
  }

  public onInteractionTypeChange(event: Event, regionName: string) {
    const select = event.target as HTMLSelectElement;
    const type = (select.value as QuestionInteractionType) || 'mcq';
    this.setInteractionTypeFor(regionName, type);
    this.drawImage();
  }

  // -------- Issue #578: blok türü --------
  public getBlockKind(regionName: string): BlockKind {
    return this.blockKinds.get(regionName)?.kind ?? 'mcq_question';
  }

  public getActivityComponent(regionName: string): ActivityComponent | null {
    return this.blockKinds.get(regionName)?.component ?? null;
  }

  /** Çoktan seçmeli soru bloğu ve MCQ etkileşimi: eğitime (send-to-fix) ve işaretleme ekranından bankaya giden. */
  public isMcqRegion(regionName: string): boolean {
    return this.getBlockKind(regionName) === 'mcq_question' && !isInteractiveType(this.getInteractionType(regionName));
  }

  /**
   * Soru tuvalinde soru olarak kaydedilebilir: soru bloğu ya da etkileşim tipi seçilmiş etkinlik. Anlatım / cevap
   * anahtarı / gürültü ve tipsiz etkinlik kaydedilmez.
   */
  public isSavableRegion(regionName: string): boolean {
    const kind = this.getBlockKind(regionName);
    return kind === 'mcq_question' || (kind === 'activity' && isInteractiveType(this.getInteractionType(regionName)));
  }

  /**
   * Müfettişten tür değişikliği. Soru'ya dönünce MCQ davranışı (şıklar, eğitim) geri gelir; Etkinlik'te bileşen korunur
   * ve etkileşim tipi bileşenden türetilir; diğer türlerde etkileşim tipi kalkar.
   */
  public setBlockKind(questionIndex: number, kind: BlockKind) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    const name = region.name;
    if (kind === 'activity') {
      const component = this.getActivityComponent(name);
      this.blockKinds.set(name, { kind, component });
      this.applyComponentInteraction(name, component);
    } else {
      if (kind === 'mcq_question') {
        this.blockKinds.delete(name);
      } else {
        this.blockKinds.set(name, { kind, component: null });
      }
      this.interactionTypes.delete(name);
    }
    this.regions.set([...this.regions()]);
    this.checkRegionWarning(region);
    this.drawImage();
  }

  /** Etkinlik bileşeni (A–I ya da null); etkileşim tipi bileşenle tutarlı olacak şekilde güncellenir. */
  public setActivityComponent(questionIndex: number, component: ActivityComponent | null) {
    const region = this.regions()[questionIndex];
    if (!region || this.getBlockKind(region.name) !== 'activity') return;
    this.blockKinds.set(region.name, { kind: 'activity', component });
    this.applyComponentInteraction(region.name, component);
    this.regions.set([...this.regions()]);
    this.checkRegionWarning(region);
    this.drawImage();
  }

  private applyComponentInteraction(regionName: string, component: ActivityComponent | null) {
    const current = this.getInteractionType(regionName);
    const type = interactionForComponent(component, current);
    if (type) {
      this.setInteractionTypeFor(regionName, type);
    } else {
      this.interactionTypes.delete(regionName);
    }
  }

  /**
   * Issue #579: C bileşeninde (sürükle-yerleştir ailesi) etkileşim tipi seçimi — dragPlace | match. Bileşenle tutarsız tip
   * (A'da match gibi) yok sayılır.
   */
  public setActivityInteraction(questionIndex: number, type: BlockInteractionType) {
    const region = this.regions()[questionIndex];
    if (!region || this.getBlockKind(region.name) !== 'activity') return;
    if (!isConsistentInteraction(this.getActivityComponent(region.name), type)) return;
    this.setInteractionTypeFor(region.name, type);
    this.regions.set([...this.regions()]);
    this.checkRegionWarning(region);
    this.drawImage();
  }

  /**
   * Issue #581: soru tuvalinde etkinlik bloğunun tipi. Tipli seçim bileşeni tutarlı kılar (matris → A, cevap kutusu → B,
   * sürükle-yerleştir / eşleştirme → C); `mcq` bloğu çoktan seçmeli soruya çevirir (aksi halde tipsiz etkinlik kaydedilmez);
   * eski `dragDropLabeling` bileşene dokunmadan atanır. Etkinlik olmayan bölgede normal tip değişikliğidir.
   */
  public setRegionInteraction(questionIndex: number, type: QuestionInteractionType) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    const name = region.name;
    if (this.getBlockKind(name) !== 'activity') {
      this.setInteractionTypeFor(name, type);
      this.regions.set([...this.regions()]);
      this.checkRegionWarning(region);
      this.drawImage();
      return;
    }
    if (type === 'mcq') {
      this.setBlockKind(questionIndex, 'mcq_question');
      return;
    }
    if (isBlockInteractionType(type)) {
      const component: ActivityComponent =
        type === MATRIX_CHOICE ? 'A' : type === SLOT_FILL ? 'B' : type === TEXT_SELECT ? 'D' : type === OPEN_TEXT ? 'H' : 'C';
      this.blockKinds.set(name, { kind: 'activity', component });
    } else if (type === REGION_PAINT) {
      // Issue #587: boyama bileşen E; Claude sayfa işaretlemesi E'ye tip türetmez (blok tipi değil).
      this.blockKinds.set(name, { kind: 'activity', component: 'E' });
    }
    this.setInteractionTypeFor(name, type);
    this.regions.set([...this.regions()]);
    this.checkRegionWarning(region);
    this.drawImage();
  }

  // -------- Issue #581: basılı cevap maskesi --------

  public getMasks(regionName: string): MaskBox[] {
    return this.masks.get(regionName) ?? [];
  }

  /** Maske çizim modu (bölge seçilir; çizimler o bölgeye göre saklanır). */
  public selectMaskMode(questionIndex: number) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) return;
    this.selectedQuestionIndex = questionIndex;
    this.selectedRegion = questionIndex;
    if (this.selectionModeLocked()) this.selectionModeLocked.set('mask');
    this.selectionMode.set('mask');
    this.drawImage();
  }

  public removeMask(regionName: string, index: number) {
    const masks = this.getMasks(regionName);
    if (index < 0 || index >= masks.length) return;
    const next = masks.filter((_, i) => i !== index);
    if (next.length > 0) {
      this.masks.set(regionName, next);
    } else {
      this.masks.delete(regionName);
    }
    this.regions.set([...this.regions()]);
    this.drawImage();
  }

  public clearMasks(regionName: string) {
    if (!this.masks.has(regionName)) return;
    this.masks.delete(regionName);
    this.regions.set([...this.regions()]);
    this.drawImage();
  }

  /** Noktayı içeren bölge (önce seçili soru, sonra sırayla); yoksa -1. */
  private regionIndexAt(x: number, y: number): number {
    const regions = this.regions();
    const inside = (r: QuestionRegion) => x >= r.x && x <= r.x + r.width && y >= r.y && y <= r.y + r.height;
    const selected = regions[this.selectedQuestionIndex];
    if (selected && inside(selected)) return this.selectedQuestionIndex;
    return regions.findIndex(inside);
  }

  /** Sürüklenen dikdörtgen bölgeye kırpılır ve bölgeye göreli, yuvarlanmış saklanır. */
  private addMaskFromDrag(questionIndex: number, x1: number, y1: number, x2: number, y2: number) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    // Kayıtta bölge yuvarlanır (`roundBox`); maske o köşeye göre ve çizilen alanı tam kapsayacak şekilde (başlangıç aşağı,
    // bitiş yukarı yuvarlanır) saklanır.
    const rx = Math.round(region.x);
    const ry = Math.round(region.y);
    const rRight = rx + Math.max(1, Math.round(region.width));
    const rBottom = ry + Math.max(1, Math.round(region.height));
    const left = Math.max(Math.floor(Math.min(x1, x2)), rx);
    const top = Math.max(Math.floor(Math.min(y1, y2)), ry);
    const right = Math.min(Math.ceil(Math.max(x1, x2)), rRight);
    const bottom = Math.min(Math.ceil(Math.max(y1, y2)), rBottom);
    if (right - left < 4 || bottom - top < 4) return;
    const masks = this.getMasks(region.name);
    if (masks.length >= MAX_MASKS_PER_REGION) {
      this.snackBar.open(this.tr('messages.maskLimit', { max: MAX_MASKS_PER_REGION }), this.tr('common.ok'), {
        duration: 2500,
      });
      return;
    }
    const box: MaskBox = { x: left - rx, y: top - ry, width: right - left, height: bottom - top };
    this.masks.set(region.name, [...masks, box]);
    this.regions.set([...this.regions()]);
    this.focusQuestionKeepingMode(questionIndex);
    if (this.maskCoversAnswerBox(region, { x: left, y: top, width: right - left, height: bottom - top })) {
      this.snackBar.open(this.tr('messages.maskOverAnswer'), this.tr('common.ok'), { duration: 6000 });
    }
  }

  /**
   * Maske (sayfa pikseli) bir şık kutusunun ya da etkinlik öğesinin (hücre, cevap kutusu, hedef, kart, eşleştirme öğesi)
   * en az yarısını kapatıyor mu — öğrenci o şıkkı / öğeyi göremez; maske yalnız basılı cevap yazısını örtmeli.
   */
  private maskCoversAnswerBox(region: QuestionRegion, mask: MaskBox): boolean {
    const boxes: MaskBox[] = (region.answers ?? []).map((a) => ({ x: a.x, y: a.y, width: a.width, height: a.height }));
    const rel = (b: { x: number; y: number; width: number; height: number } | null | undefined) => {
      if (b) boxes.push({ x: region.x + b.x, y: region.y + b.y, width: b.width, height: b.height });
    };
    const type = this.getInteractionType(region.name);
    if (type === MATRIX_CHOICE) {
      this.ensureMatrixState(region.name).rows.forEach((row) => Object.values(row.cells).forEach(rel));
    } else if (type === SLOT_FILL) {
      this.ensureSlotFillState(region.name).slots.forEach((slot) => rel(slot.box));
    } else if (type === DRAG_PLACE) {
      const place = this.ensureDragPlaceState(region.name);
      place.targets.forEach((t) => rel(t.box));
      place.cards.forEach((c) => rel(c.box));
    } else if (type === MATCH) {
      const match = this.ensureMatchState(region.name);
      [...match.left, ...match.right].forEach((i) => rel(i.box));
    } else if (type === TEXT_SELECT) {
      this.ensureTextSelectState(region.name).tokens.forEach((t) => t.boxes.forEach(rel));
    }
    return boxes.some((b) => {
      const w = Math.min(b.x + b.width, mask.x + mask.width) - Math.max(b.x, mask.x);
      const h = Math.min(b.y + b.height, mask.y + mask.height) - Math.max(b.y, mask.y);
      return w > 0 && h > 0 && w * h >= 0.5 * b.width * b.height;
    });
  }

  /**
   * Issue #579: Claude'un çıkardığı taslak planı (kutular blok kutusuna göre) bölgenin o tipteki yazarlık durumuna yükler;
   * tuvalde satır / hücre, cevap kutusu, hedef / kart, sol / sağ öğe olarak çizilir. Çözüm yok (doğru değerler boş).
   * Elle yapılan yazarlık ezilmez: o tipin durumu doluysa ve son yüklenen taslaktan farklıysa (öğretmen çizdi / düzeltti)
   * `'kept'` döner ve durum değişmez; `replace` ("Taslağı uygula" onayı) yine de değiştirir. Plan o tipin geçerli planı
   * değilse (bozuk, boş) `'invalid'`.
   */
  public applyInteractionDraft(
    regionName: string,
    type: BlockInteractionType,
    plan: unknown,
    options: { replace?: boolean } = {}
  ): InteractionDraftApply {
    if (!this.regions().some((r) => r.name === regionName) || plan == null) return 'invalid';
    let json: string;
    try {
      json = JSON.stringify(plan);
    } catch {
      return 'invalid';
    }
    const apply = this.draftLoader(regionName, type, json);
    if (!apply) return 'invalid';
    if (!options.replace && this.hasAuthoringEdits(regionName, type)) return 'kept';
    apply();
    this.draftFingerprints.set(`${regionName}\u0000${type}`, this.authoringFingerprint(regionName, type) ?? '');
    this.drawImage();
    return 'applied';
  }

  /**
   * Bölgenin o tipteki yazarlığı elle değişti mi: durum boş değil ve son uygulanan taslağın (yoksa hiçbir taslağın)
   * izinden farklı.
   */
  public hasAuthoringEdits(regionName: string, type: BlockInteractionType): boolean {
    if (this.isAuthoringEmpty(regionName, type)) return false;
    const applied = this.draftFingerprints.get(`${regionName}\u0000${type}`);
    return applied === undefined || applied !== this.authoringFingerprint(regionName, type);
  }

  /** Taslak planı ayrıştırır; geçerliyse durumu yazan işlev (henüz yazmaz), değilse null. */
  private draftLoader(regionName: string, type: BlockInteractionType, json: string): (() => void) | null {
    if (type === MATRIX_CHOICE) {
      const parsed = parseMatrixChoicePlan(json);
      const state = parsed ? this.matrixStateFromPlan(parsed) : null;
      return state ? () => this.matrixState.set(regionName, state) : null;
    }
    if (type === SLOT_FILL) {
      const parsed = parseSlotFillPlan(json);
      return parsed ? () => this.slotFillState.set(regionName, slotFillStateFromPlan(parsed)) : null;
    }
    if (type === DRAG_PLACE) {
      const parsed = parseDragPlacePlan(json);
      return parsed ? () => this.dragPlaceState.set(regionName, dragPlaceStateFromPlan(parsed)) : null;
    }
    if (type === MATCH) {
      const parsed = parseMatchPlan(json);
      return parsed ? () => this.matchState.set(regionName, matchStateFromPlan(parsed)) : null;
    }
    if (type === TEXT_SELECT) {
      const parsed = parseTextSelectPlan(json);
      return parsed ? () => this.textSelectState.set(regionName, textSelectStateFromPlan(parsed)) : null;
    }
    // Issue #583: openText taslağı çıkarılmaz (bu uca gönderilmez); gelirse yok sayılır.
    return null;
  }

  private isAuthoringEmpty(regionName: string, type: BlockInteractionType): boolean {
    switch (type) {
      case MATRIX_CHOICE:
        return (this.matrixState.get(regionName)?.rows.length ?? 0) === 0;
      case SLOT_FILL:
        return (this.slotFillState.get(regionName)?.slots.length ?? 0) === 0;
      case DRAG_PLACE: {
        const state = this.dragPlaceState.get(regionName);
        return !state || (state.targets.length === 0 && state.cards.length === 0);
      }
      case MATCH: {
        const state = this.matchState.get(regionName);
        return !state || (state.left.length === 0 && state.right.length === 0);
      }
      case TEXT_SELECT:
        return (this.textSelectState.get(regionName)?.tokens.length ?? 0) === 0;
      case OPEN_TEXT:
        return true;
    }
  }

  /** Yazarlığın plan + çözüm izi (durum yoksa null); taslaktan sonra elle düzeltme olup olmadığını ayırt eder. */
  private authoringFingerprint(regionName: string, type: BlockInteractionType): string | null {
    switch (type) {
      case MATRIX_CHOICE:
        return this.matrixState.has(regionName)
          ? JSON.stringify([this.buildMatrixPlan(regionName), this.buildMatrixSolutionJson(regionName)])
          : null;
      case SLOT_FILL: {
        const state = this.slotFillState.get(regionName);
        return state ? JSON.stringify([buildSlotFillPlan(state), buildSlotFillSolution(state)]) : null;
      }
      case DRAG_PLACE: {
        const state = this.dragPlaceState.get(regionName);
        return state ? JSON.stringify([buildDragPlacePlan(state), buildDragPlaceSolution(state)]) : null;
      }
      case MATCH: {
        const state = this.matchState.get(regionName);
        return state ? JSON.stringify([buildMatchPlan(state), buildMatchSolution(state)]) : null;
      }
      case TEXT_SELECT: {
        const state = this.textSelectState.get(regionName);
        return state ? JSON.stringify([buildTextSelectPlan(state), buildTextSelectSolution(state)]) : null;
      }
      case OPEN_TEXT: {
        const state = this.openTextState.get(regionName);
        return state ? JSON.stringify([buildOpenTextPlan(state), buildOpenTextSolution(state)]) : null;
      }
    }
  }

  /**
   * Matris planı → yazarlık durumu. Satır kutusu zorunlu (yazarlık satırı kutuyla çizer); kutusuz satırda hücrelerin
   * kapsayan kutusu kullanılır, ikisi de yoksa satır atlanır. Hiç satır kalmazsa null.
   */
  private matrixStateFromPlan(plan: MatrixChoicePlanV1): MatrixChoiceAuthoringState | null {
    const rows = plan.rows.flatMap((row) => {
      const cells = { ...(row.cells ?? {}) } as Record<string, MatrixBox>;
      const box = row.box ?? boundingBox(Object.values(cells));
      return box ? [{ id: row.id, label: row.label ?? '', box: { ...box }, cells }] : [];
    });
    if (rows.length === 0) return null;
    return {
      multiPerRow: plan.multiPerRow,
      columns: plan.columns.map((c) => ({ ...c })),
      rows,
      solution: emptyMatrixSelections(),
      activeColumnId: plan.columns[0]?.id ?? null,
    };
  }

  /** Tespit sonucundaki türü bölgeye yazar; tipli etkinlik, tür seçicide o tipi seçmekle aynıdır. */
  private applyPredictedKind(regionName: string, prediction: Prediction) {
    const kind = blockKindOf(prediction);
    if (kind === 'mcq_question') {
      return;
    }
    const component = kind === 'activity' && isActivityComponent(prediction.component) ? prediction.component : null;
    this.blockKinds.set(regionName, { kind, component });
    // Issue #582 / #583: eski kayıtlarda D / H bloğunun tipi null gelebilir; ikisi de tek tipli (textSelect / openText).
    const type =
      prediction.interaction_type ?? (component === 'D' || component === 'H' ? interactionForComponent(component, null) : null);
    if (kind === 'activity' && isBlockInteractionType(type) && isConsistentInteraction(component, type)) {
      this.setInteractionTypeFor(regionName, type);
    }
  }

  private setInteractionTypeFor(regionName: string, type: QuestionInteractionType) {
    this.interactionTypes.set(regionName, type);

    if (type === 'dragDropLabeling') {
      this.ensureLabelingState(regionName);
    } else if (type === MATRIX_CHOICE) {
      this.ensureMatrixState(regionName);
    } else if (type === SLOT_FILL) {
      this.ensureSlotFillState(regionName);
    } else if (type === DRAG_PLACE) {
      this.ensureDragPlaceState(regionName);
    } else if (type === MATCH) {
      this.ensureMatchState(regionName);
    } else if (type === TEXT_SELECT) {
      this.ensureTextSelectState(regionName);
    } else if (type === OPEN_TEXT) {
      this.ensureOpenTextState(regionName);
    } else if (type === REGION_PAINT) {
      this.ensurePaintRegionsState(regionName);
    }
  }

  public selectDropZoneMode(questionIndex: number) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) {
      return;
    }
    this.selectedQuestionIndex = questionIndex;
    const regionName = this.regions()[questionIndex].name;
    this.interactionTypes.set(regionName, 'dragDropLabeling');
    this.ensureLabelingState(regionName);
    this.selectionMode.set('dropzone');
    this.drawImage();
  }

  public selectAnswerMode(questionIndex: number) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) {
      return;
    }
    this.selectedQuestionIndex = questionIndex;
    const regionName = this.regions()[questionIndex].name;
    this.interactionTypes.set(regionName, 'mcq');
    this.selectionMode.set('answer');
    this.drawImage();
  }

  private ensureLabelingState(regionName: string): DragDropLabelingAuthoringState {
    const existing = this.labelingState.get(regionName);
    if (existing) return existing;
    const created: DragDropLabelingAuthoringState = {
      labelCount: 4,
      dropZones: [],
      draggables: [],
      placements: [],
    };
    this.labelingState.set(regionName, created);
    return created;
  }

  public getLabelCount(regionName: string): number {
    return this.ensureLabelingState(regionName).labelCount;
  }

  public setLabelCount(regionName: string, rawValue: string) {
    const parsed = Number(rawValue);
    const next = Number.isFinite(parsed) ? Math.max(1, Math.min(30, Math.floor(parsed))) : 4;
    const state = this.ensureLabelingState(regionName);
    state.labelCount = next;
    this.labelingState.set(regionName, { ...state });
  }

  public generateDraggables(regionName: string) {
    const state = this.ensureLabelingState(regionName);
    state.draggables = Array.from({ length: state.labelCount }, (_, i) => {
      const idx = i + 1;
      return { id: `d${idx}`, label: `${idx}` };
    });

    // Keep placements only for existing draggables
    const draggableIds = new Set(state.draggables.map((d) => d.id));
    state.placements = state.placements.filter((p) => draggableIds.has(p.draggableId));
    this.labelingState.set(regionName, { ...state });
    this.drawImage();
  }

  public getDropZones(regionName: string) {
    return this.ensureLabelingState(regionName).dropZones;
  }

  public getDraggables(regionName: string) {
    return this.ensureLabelingState(regionName).draggables;
  }

  public setZoneSolution(regionName: string, dropZoneId: string, draggableId: string) {
    const state = this.ensureLabelingState(regionName);

    // remove old assignment for this zone
    state.placements = state.placements.filter((p) => p.dropZoneId !== dropZoneId);

    if (draggableId && draggableId !== '0') {
      // ensure draggable is not used elsewhere (unique)
      state.placements = state.placements.filter((p) => p.draggableId !== draggableId);
      state.placements.push({ dropZoneId, draggableId });
    }

    this.labelingState.set(regionName, { ...state });
  }

  public getZoneAssignedDraggable(regionName: string, dropZoneId: string): string {
    const state = this.ensureLabelingState(regionName);
    return state.placements.find((p) => p.dropZoneId === dropZoneId)?.draggableId ?? '0';
  }

  private addDropZone(regionName: string, x: number, y: number, width: number, height: number) {
    const state = this.ensureLabelingState(regionName);
    const nextId = `z${state.dropZones.length + 1}`;
    state.dropZones.push({ id: nextId, x, y, width, height });
    this.labelingState.set(regionName, { ...state });
  }

  public removeLastDropZone(regionName: string) {
    const state = this.ensureLabelingState(regionName);
    const removed = state.dropZones.pop();
    if (!removed) {
      return;
    }

    state.placements = state.placements.filter((p) => p.dropZoneId !== removed.id);
    this.labelingState.set(regionName, { ...state });
    this.drawImage();
  }

  public clearDropZones(regionName: string) {
    const state = this.ensureLabelingState(regionName);
    state.dropZones = [];
    state.placements = [];
    this.labelingState.set(regionName, { ...state });
    this.drawImage();
  }

  public autoAssignSolutionSequential(regionName: string) {
    const state = this.ensureLabelingState(regionName);
    if (!state.dropZones.length || !state.draggables.length) {
      return;
    }

    const placements: Array<{ draggableId: string; dropZoneId: string }> = [];
    const count = Math.min(state.dropZones.length, state.draggables.length);
    for (let i = 0; i < count; i++) {
      placements.push({ dropZoneId: state.dropZones[i].id, draggableId: state.draggables[i].id });
    }

    state.placements = placements;
    this.labelingState.set(regionName, { ...state });
  }

  private buildInteractionPlan(regionName: string): DragDropLabelingPlanV1 | null {
    if (this.getInteractionType(regionName) !== 'dragDropLabeling') return null;
    const state = this.ensureLabelingState(regionName);
    return {
      version: 1,
      type: 'dragDropLabeling',
      dropZones: state.dropZones,
      draggables: state.draggables,
    };
  }

  private buildInteractionPlanJson(regionName: string): string | null {
    if (this.getInteractionType(regionName) === MATRIX_CHOICE) {
      return JSON.stringify(this.buildMatrixPlan(regionName));
    }
    if (this.getInteractionType(regionName) === SLOT_FILL) {
      return JSON.stringify(buildSlotFillPlan(this.ensureSlotFillState(regionName)));
    }
    if (this.getInteractionType(regionName) === DRAG_PLACE) {
      return JSON.stringify(buildDragPlacePlan(this.ensureDragPlaceState(regionName)));
    }
    if (this.getInteractionType(regionName) === MATCH) {
      return JSON.stringify(buildMatchPlan(this.ensureMatchState(regionName)));
    }
    if (this.getInteractionType(regionName) === TEXT_SELECT) {
      return JSON.stringify(buildTextSelectPlan(this.ensureTextSelectState(regionName)));
    }
    if (this.getInteractionType(regionName) === REGION_PAINT) {
      return JSON.stringify(buildPaintRegionsPlan(this.ensurePaintRegionsState(regionName)));
    }
    if (this.getInteractionType(regionName) === OPEN_TEXT) {
      return JSON.stringify(buildOpenTextPlan(this.ensureOpenTextState(regionName)));
    }
    const plan = this.buildInteractionPlan(regionName);
    if (!plan) return null;
    return JSON.stringify(plan);
  }

  /**
   * issue #570: doğru yerleşimler plandan ayrı gönderilir (`BulkQuestionDto.interactionSolution`); plan çözüm taşımaz,
   * aksi halde öğrenciye giden plan cevabı sızdırırdı. dragDropLabeling dışında null.
   */
  private buildInteractionSolutionJson(regionName: string): string | null {
    if (this.getInteractionType(regionName) === MATRIX_CHOICE) {
      return this.buildMatrixSolutionJson(regionName);
    }
    if (this.getInteractionType(regionName) === SLOT_FILL) {
      const solution = buildSlotFillSolution(this.ensureSlotFillState(regionName));
      return solution ? JSON.stringify(solution) : null;
    }
    if (this.getInteractionType(regionName) === DRAG_PLACE) {
      const solution = buildDragPlaceSolution(this.ensureDragPlaceState(regionName));
      return solution ? JSON.stringify(solution) : null;
    }
    if (this.getInteractionType(regionName) === MATCH) {
      const solution = buildMatchSolution(this.ensureMatchState(regionName));
      return solution ? JSON.stringify(solution) : null;
    }
    if (this.getInteractionType(regionName) === TEXT_SELECT) {
      const solution = buildTextSelectSolution(this.ensureTextSelectState(regionName));
      return solution ? JSON.stringify(solution) : null;
    }
    // Issue #583: örnek cevap + rubrik (boş çözüm geçerli: değerlendirme elle).
    if (this.getInteractionType(regionName) === REGION_PAINT) {
      const solution = buildPaintRegionsSolution(this.ensurePaintRegionsState(regionName));
      return solution ? JSON.stringify(solution) : null;
    }
    if (this.getInteractionType(regionName) === OPEN_TEXT) {
      const solution = buildOpenTextSolution(this.ensureOpenTextState(regionName));
      return solution ? JSON.stringify(solution) : null;
    }
    if (this.getInteractionType(regionName) !== 'dragDropLabeling') return null;
    const state = this.ensureLabelingState(regionName);
    return JSON.stringify({ placements: state.placements });
  }

  // -------- Issue #574: matris seçim yazarlığı --------

  public ensureMatrixState(regionName: string): MatrixChoiceAuthoringState {
    const existing = this.matrixState.get(regionName);
    if (existing) return existing;
    const preset = MATRIX_COLUMN_PRESETS.trueFalse;
    const columns = preset.labels.map((label, i) => ({ id: `c${i + 1}`, label }));
    const created: MatrixChoiceAuthoringState = {
      multiPerRow: preset.multiPerRow,
      columns,
      rows: [],
      solution: {},
      activeColumnId: columns[0].id,
    };
    this.matrixState.set(regionName, created);
    return created;
  }

  private commitMatrix(regionName: string, state: MatrixChoiceAuthoringState) {
    this.matrixState.set(regionName, { ...state });
    this.drawImage();
  }

  public selectMatrixRowMode(questionIndex: number) {
    this.selectMatrixMode(questionIndex, 'matrixRow');
  }

  public selectMatrixCellMode(questionIndex: number, columnId?: string) {
    const regionName = this.regions()[questionIndex]?.name;
    if (regionName && columnId) this.setMatrixActiveColumn(regionName, columnId);
    this.selectMatrixMode(questionIndex, 'matrixCell');
  }

  private selectMatrixMode(questionIndex: number, mode: 'matrixRow' | 'matrixCell') {
    if (questionIndex < 0 || questionIndex >= this.regions().length) {
      return;
    }
    this.selectedQuestionIndex = questionIndex;
    const regionName = this.regions()[questionIndex].name;
    this.interactionTypes.set(regionName, MATRIX_CHOICE);
    this.ensureMatrixState(regionName);
    // Araç çubuğu modu kilitlediyse kilit de güncellenir; yoksa sonraki soru seçimi eski kilide döndürür.
    if (this.selectionModeLocked()) this.selectionModeLocked.set(mode);
    this.selectionMode.set(mode);
    this.drawImage();
  }

  /** Kolon şablonu: kolonları yeniden kurar (aynı sıradaki kolon id'si korunur; artan kolonların hücre/çözümü silinir). */
  public applyMatrixPreset(regionName: string, preset: MatrixColumnPreset) {
    const definition = MATRIX_COLUMN_PRESETS[preset];
    const state = this.ensureMatrixState(regionName);
    this.resizeMatrixColumns(state, definition.labels.length);
    state.columns = state.columns.map((c, i) => ({ ...c, label: definition.labels[i] }));
    state.multiPerRow = definition.multiPerRow;
    this.trimMatrixSolutionToSingle(state);
    this.commitMatrix(regionName, state);
  }

  public setMatrixColumnCount(regionName: string, rawValue: string | number) {
    const parsed = Math.floor(Number(rawValue));
    const limits = MATRIX_CHOICE_LIMITS;
    const state = this.ensureMatrixState(regionName);
    const next = Number.isFinite(parsed)
      ? Math.max(limits.minColumns, Math.min(limits.maxColumns, parsed))
      : state.columns.length;
    this.resizeMatrixColumns(state, next);
    this.commitMatrix(regionName, state);
  }

  private resizeMatrixColumns(state: MatrixChoiceAuthoringState, count: number) {
    const columns = state.columns.slice(0, count);
    for (let i = columns.length; i < count; i++) {
      columns.push({ id: this.nextMatrixId('c', columns.map((c) => c.id)), label: `${i + 1}` });
    }
    const keep = new Set(columns.map((c) => c.id));
    state.columns = columns;
    state.rows = state.rows.map((row) => ({
      ...row,
      cells: Object.fromEntries(Object.entries(row.cells).filter(([id]) => keep.has(id))),
    }));
    state.solution = this.filterMatrixSolution(state.solution, (columnId) => keep.has(columnId));
    if (!state.activeColumnId || !keep.has(state.activeColumnId)) {
      state.activeColumnId = columns[0]?.id ?? null;
    }
  }

  public setMatrixColumnLabel(regionName: string, columnId: string, label: string) {
    const state = this.ensureMatrixState(regionName);
    const value = (label ?? '').slice(0, MATRIX_CHOICE_LIMITS.maxLabelLength);
    state.columns = state.columns.map((c) => (c.id === columnId ? { ...c, label: value } : c));
    this.commitMatrix(regionName, state);
  }

  public setMatrixMultiPerRow(regionName: string, multi: boolean) {
    const state = this.ensureMatrixState(regionName);
    state.multiPerRow = !!multi;
    this.trimMatrixSolutionToSingle(state);
    this.commitMatrix(regionName, state);
  }

  /** Çoklu seçim kapanınca her satırda yalnız ilk (kolon sırasına göre) çözüm kalır. */
  private trimMatrixSolutionToSingle(state: MatrixChoiceAuthoringState) {
    if (state.multiPerRow) return;
    const solution: MatrixSelections = {};
    Object.entries(state.solution).forEach(([rowId, cols]) => {
      if (cols.length > 0) solution[rowId] = [cols[0]];
    });
    state.solution = solution;
  }

  public setMatrixRowLabel(regionName: string, rowId: string, label: string) {
    const state = this.ensureMatrixState(regionName);
    const value = (label ?? '').slice(0, MATRIX_CHOICE_LIMITS.maxLabelLength);
    state.rows = state.rows.map((r) => (r.id === rowId ? { ...r, label: value } : r));
    this.matrixState.set(regionName, { ...state });
  }

  public setMatrixActiveColumn(regionName: string, columnId: string) {
    const state = this.ensureMatrixState(regionName);
    if (!state.columns.some((c) => c.id === columnId)) return;
    state.activeColumnId = columnId;
    this.matrixState.set(regionName, { ...state });
  }

  /** Satırın çözümü: tekli seçimde kolonu seçer (aynısına yeniden basınca kaldırır), çokluda aç/kapa. */
  public toggleMatrixSolution(regionName: string, rowId: string, columnId: string) {
    const state = this.ensureMatrixState(regionName);
    if (!state.rows.some((r) => r.id === rowId) || !state.columns.some((c) => c.id === columnId)) return;
    const current = state.solution[rowId] ?? [];
    let next: string[];
    if (state.multiPerRow) {
      const toggled = current.includes(columnId) ? current.filter((c) => c !== columnId) : [...current, columnId];
      next = state.columns.map((c) => c.id).filter((id) => toggled.includes(id));
    } else {
      next = current.length === 1 && current[0] === columnId ? [] : [columnId];
    }
    const solution = { ...state.solution };
    if (next.length > 0) solution[rowId] = next;
    else delete solution[rowId];
    state.solution = solution;
    this.commitMatrix(regionName, state);
  }

  public removeMatrixRow(regionName: string, rowId: string) {
    const state = this.ensureMatrixState(regionName);
    state.rows = state.rows.filter((r) => r.id !== rowId);
    const solution = { ...state.solution };
    delete solution[rowId];
    state.solution = solution;
    this.commitMatrix(regionName, state);
  }

  public removeLastMatrixRow(regionName: string) {
    const last = this.ensureMatrixState(regionName).rows.at(-1);
    if (last) this.removeMatrixRow(regionName, last.id);
  }

  public clearMatrixRows(regionName: string) {
    const state = this.ensureMatrixState(regionName);
    state.rows = [];
    state.solution = {};
    this.commitMatrix(regionName, state);
  }

  public removeMatrixCell(regionName: string, rowId: string, columnId: string) {
    const state = this.ensureMatrixState(regionName);
    state.rows = state.rows.map((r) => {
      if (r.id !== rowId) return r;
      const cells = { ...r.cells };
      delete cells[columnId];
      return { ...r, cells };
    });
    this.commitMatrix(regionName, state);
  }

  public clearMatrixCells(regionName: string) {
    const state = this.ensureMatrixState(regionName);
    state.rows = state.rows.map((r) => ({ ...r, cells: {} }));
    this.commitMatrix(regionName, state);
  }

  /**
   * İlk satırın hücrelerini diğer satırlara kopyalar: hücrenin ilk satır kutusuna göre yatay/dikey kayması her satırın
   * kutusuna uygulanır ve satır kutusuna sığdırılır. Tablo düzenli ise tek satır çizmek yeter.
   */
  public copyMatrixCellsFromFirstRow(regionName: string) {
    const state = this.ensureMatrixState(regionName);
    const [first, ...others] = state.rows;
    if (!first || Object.keys(first.cells).length === 0) return;
    state.rows = [
      first,
      ...others.map((row) => {
        const cells: Record<string, MatrixBox> = {};
        Object.entries(first.cells).forEach(([columnId, cell]) => {
          // Kayma ilk satırın kutusuna göre hem yatayda hem dikeyde: iki sütunlu düzende sağ sütundaki satırın
          // hücreleri de kendi kutusuna oturur. Sonuç satır kutusuna sığdırılır.
          const span = (start: number, size: number, firstStart: number, rowStart: number, rowSize: number) => {
            const rowEnd = rowStart + rowSize;
            const from = Math.max(rowStart, Math.min(rowStart + (start - firstStart), rowEnd - 1));
            return { from, size: Math.max(1, Math.min(size, rowEnd - from)) };
          };
          const h = span(cell.x, cell.width, first.box.x, row.box.x, row.box.width);
          const v = span(cell.y, cell.height, first.box.y, row.box.y, row.box.height);
          cells[columnId] = { x: h.from, y: v.from, width: h.size, height: v.size };
        });
        return { ...row, cells };
      }),
    ];
    this.commitMatrix(regionName, state);
  }

  /** Sürüklemeyle çizilen kutu: `matrixRow` modunda yeni satır, `matrixCell` modunda merkezinin düştüğü satıra hücre. */
  private addMatrixBoxFromDrag(questionIndex: number, x1: number, y1: number, x2: number, y2: number) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    const absX1 = Math.min(x1, x2);
    const absY1 = Math.min(y1, y2);
    const absX2 = Math.max(x1, x2);
    const absY2 = Math.max(y1, y2);
    if (absX2 - absX1 < 6 || absY2 - absY1 < 6) return;
    const within =
      absX1 >= region.x && absY1 >= region.y && absX2 <= region.x + region.width && absY2 <= region.y + region.height;
    if (!within) {
      this.snackBar.open(this.tr('messages.matrixOutsideQuestion'), this.tr('common.ok'), { duration: 2000 });
      return;
    }
    // Koordinatlar soru bölgesine göre ve tam sayı (backend kutuları sayı olarak saklar; kırpma tam piksel).
    const box: MatrixBox = {
      x: Math.round(absX1 - region.x),
      y: Math.round(absY1 - region.y),
      width: Math.max(1, Math.round(absX2 - absX1)),
      height: Math.max(1, Math.round(absY2 - absY1)),
    };
    // Araç çubuğundaki "Matris" modu MCQ/sürükle-bırak sorusunda çizerse çizim kaybolmasın: soru matrise döner.
    if (this.getInteractionType(region.name) !== MATRIX_CHOICE) {
      this.interactionTypes.set(region.name, MATRIX_CHOICE);
      this.ensureMatrixState(region.name);
    }
    if (this.selectionMode() === 'matrixRow') {
      this.addMatrixRow(region.name, box);
    } else {
      this.addMatrixCell(region.name, box);
    }
  }

  private addMatrixRow(regionName: string, box: MatrixBox) {
    const state = this.ensureMatrixState(regionName);
    if (state.rows.length >= MATRIX_CHOICE_LIMITS.maxRows) {
      this.snackBar.open(this.tr('warnings.matrixTooManyRows', { max: MATRIX_CHOICE_LIMITS.maxRows }), this.tr('common.ok'), {
        duration: 2500,
      });
      return;
    }
    const id = this.nextMatrixId('r', state.rows.map((r) => r.id));
    state.rows = [...state.rows, { id, label: '', box, cells: {} }];
    this.matrixState.set(regionName, { ...state });
  }

  private addMatrixCell(regionName: string, box: MatrixBox) {
    const state = this.ensureMatrixState(regionName);
    const cx = box.x + box.width / 2;
    const cy = box.y + box.height / 2;
    const row = state.rows.find(
      (r) => cx >= r.box.x && cx <= r.box.x + r.box.width && cy >= r.box.y && cy <= r.box.y + r.box.height
    );
    if (!row) {
      this.snackBar.open(this.tr('messages.matrixCellOutsideRow'), this.tr('common.ok'), { duration: 2500 });
      return;
    }
    const columnId = state.activeColumnId ?? state.columns[0]?.id;
    if (!columnId) return;
    state.rows = state.rows.map((r) => (r.id === row.id ? { ...r, cells: { ...r.cells, [columnId]: box } } : r));
    // Sıradaki kolona geç: satır satır hızlı çizim (D, Y, D, Y, ...).
    const index = state.columns.findIndex((c) => c.id === columnId);
    state.activeColumnId = state.columns[(index + 1) % state.columns.length]?.id ?? columnId;
    this.matrixState.set(regionName, { ...state });
  }

  private nextMatrixId(prefix: 'r' | 'c', used: string[]): string {
    const taken = new Set(used);
    let n = used.length + 1;
    for (let i = 1; i <= used.length + 1; i++) {
      if (!taken.has(`${prefix}${i}`)) {
        n = i;
        break;
      }
    }
    return `${prefix}${n}`;
  }

  private filterMatrixSolution(solution: MatrixSelections, keepColumn: (columnId: string) => boolean): MatrixSelections {
    const result: MatrixSelections = {};
    Object.entries(solution).forEach(([rowId, cols]) => {
      const kept = cols.filter(keepColumn);
      if (kept.length > 0) result[rowId] = kept;
    });
    return result;
  }

  /** Öğrenciye giden plan: çözüm içermez (#570). Boş satır metni gönderilmez. */
  private buildMatrixPlan(regionName: string): MatrixChoicePlanV1 {
    const state = this.ensureMatrixState(regionName);
    return {
      version: 1,
      type: MATRIX_CHOICE,
      multiPerRow: state.multiPerRow,
      columns: state.columns.map((c) => ({ id: c.id, label: c.label.trim() })),
      rows: state.rows.map((r) => ({
        id: r.id,
        ...(r.label.trim() ? { label: r.label.trim() } : {}),
        box: { ...r.box },
        ...(Object.keys(r.cells).length > 0 ? { cells: { ...r.cells } } : {}),
      })),
    };
  }

  /** Çözüm `{rowId: [colId]}` (plan satır sırasıyla); hiç işaret yoksa null (backend boş nesneyi kabul etmez). */
  private buildMatrixSolutionJson(regionName: string): string | null {
    const state = this.ensureMatrixState(regionName);
    const solution: MatrixSelections = {};
    state.rows.forEach((r) => {
      const cols = state.solution[r.id];
      if (cols?.length) solution[r.id] = [...cols];
    });
    return Object.keys(solution).length > 0 ? JSON.stringify(solution) : null;
  }

  private buildMatrixInspector(regionName: string): MatrixInspector {
    const state = this.ensureMatrixState(regionName);
    const cellTotal = state.rows.reduce((sum, r) => sum + Object.keys(r.cells).length, 0);
    return {
      multiPerRow: state.multiPerRow,
      columns: state.columns,
      rows: state.rows.map((r, i) => ({
        id: r.id,
        no: i + 1,
        label: r.label,
        solution: state.solution[r.id] ?? [],
        cellCount: Object.keys(r.cells).length,
      })),
      activeColumnId: state.activeColumnId,
      hasCells: cellTotal > 0,
      allCells: state.rows.length > 0 && cellTotal === state.rows.length * state.columns.length,
      missingSolutionCount: state.rows.filter((r) => !(state.solution[r.id]?.length)).length,
      limits: MATRIX_CHOICE_LIMITS,
    };
  }

  // -------- Issue #575: cevap kutusu (slotFill) yazarlığı --------

  public ensureSlotFillState(regionName: string): SlotFillAuthoringState {
    const existing = this.slotFillState.get(regionName);
    if (existing) return existing;
    const created = createSlotFillState();
    this.slotFillState.set(regionName, created);
    return created;
  }

  private commitSlotFill(regionName: string, state: SlotFillAuthoringState) {
    this.slotFillState.set(regionName, { ...state });
    this.drawImage();
  }

  /** "Kutu" çizim modu: soru cevap kutusu türüne geçer; araç çubuğu kilidi varsa kilit de güncellenir. */
  public selectSlotFillMode(questionIndex: number) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) return;
    this.selectedQuestionIndex = questionIndex;
    const regionName = this.regions()[questionIndex].name;
    this.interactionTypes.set(regionName, SLOT_FILL);
    this.ensureSlotFillState(regionName);
    if (this.selectionModeLocked()) this.selectionModeLocked.set('slot');
    this.selectionMode.set('slot');
    this.drawImage();
  }

  public selectSlot(regionName: string, slotId: string | null) {
    const state = this.ensureSlotFillState(regionName);
    if (slotId !== null && !state.slots.some((s) => s.id === slotId)) return;
    state.selectedSlotId = slotId;
    this.commitSlotFill(regionName, state);
  }

  private updateSlot(regionName: string, slotId: string, change: (slot: SlotAuthoring) => SlotAuthoring) {
    const state = this.ensureSlotFillState(regionName);
    if (!state.slots.some((s) => s.id === slotId)) return;
    state.slots = state.slots.map((s) => (s.id === slotId ? change({ ...s }) : s));
    this.commitSlotFill(regionName, state);
  }

  public setSlotKind(regionName: string, slotId: string, kind: SlotKind) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, kind }));
  }

  public setSlotLabel(regionName: string, slotId: string, label: string) {
    const value = (label ?? '').slice(0, SLOT_FILL_LIMITS.maxLabelLength);
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, label: value }));
  }

  public setSlotEntry(regionName: string, slotId: string, entry: SlotAuthoring['entry']) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, entry }));
  }

  public setSlotUnitHint(regionName: string, slotId: string, unit: string) {
    const value = (unit ?? '').trim().slice(0, SLOT_FILL_LIMITS.maxUnitHintLength);
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, unitHint: value }));
  }

  public setSlotValue(regionName: string, slotId: string, value: string) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, value: (value ?? '').slice(0, 64) }));
  }

  public setSlotEquivalence(regionName: string, slotId: string, on: boolean) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, equivalence: !!on }));
  }

  public setSlotRequireSimplest(regionName: string, slotId: string, on: boolean) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, requireSimplest: !!on }));
  }

  /** Birim kipi; birim listesi boşken kutunun gösterdiği birim listeye alınır (en yaygın durum: "cm" yazılır). */
  public setSlotUnitMode(regionName: string, slotId: string, mode: SlotAuthoring['unitMode']) {
    this.updateSlot(regionName, slotId, (slot) => ({
      ...slot,
      unitMode: mode,
      units: mode !== 'none' && slot.units.length === 0 && slot.unitHint.trim() ? [slot.unitHint.trim()] : slot.units,
    }));
  }

  /** Kabul edilen birimler: virgülle ayrılır ("cm, santimetre"). */
  public setSlotUnits(regionName: string, slotId: string, text: string) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, units: parseList(text, COMMA_SEPARATOR) }));
  }

  /** Metin kabul listesi: her satıra bir cevap. */
  public setSlotAccept(regionName: string, slotId: string, text: string) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, accept: parseList(text, LINE_SEPARATOR) }));
  }

  /** İsteğe bağlı ekler ("Bölgesi"): virgül ya da satırla ayrılır. */
  public setSlotSuffixes(regionName: string, slotId: string, text: string) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, optionalSuffixes: parseList(text, COMMA_SEPARATOR) }));
  }

  public addSlotOption(regionName: string, slotId: string) {
    this.updateSlot(regionName, slotId, (slot) => {
      if (slot.options.length >= SLOT_FILL_LIMITS.maxSelectOptions) return slot;
      const id = nextSlotId('o', slot.options.map((o) => o.id));
      return { ...slot, options: [...slot.options, { id, label: '' }] };
    });
  }

  public removeSlotOption(regionName: string, slotId: string, optionId: string) {
    this.updateSlot(regionName, slotId, (slot) => ({
      ...slot,
      options: slot.options.filter((o) => o.id !== optionId),
      correctOptionIds: slot.correctOptionIds.filter((id) => id !== optionId),
    }));
  }

  public setSlotOptionLabel(regionName: string, slotId: string, optionId: string, label: string) {
    const value = (label ?? '').slice(0, SLOT_FILL_LIMITS.maxLabelLength);
    this.updateSlot(regionName, slotId, (slot) => ({
      ...slot,
      options: slot.options.map((o) => (o.id === optionId ? { ...o, label: value } : o)),
    }));
  }

  /** Doğru seçenek aç/kapa (birden fazla doğru seçenek olabilir). */
  public toggleSlotCorrectOption(regionName: string, slotId: string, optionId: string) {
    this.updateSlot(regionName, slotId, (slot) => {
      if (!slot.options.some((o) => o.id === optionId)) return slot;
      const on = slot.correctOptionIds.includes(optionId);
      const next = on ? slot.correctOptionIds.filter((id) => id !== optionId) : [...slot.correctOptionIds, optionId];
      return { ...slot, correctOptionIds: slot.options.map((o) => o.id).filter((id) => next.includes(id)) };
    });
  }

  /** Palet sembolleri: boşlukla ayrılır ("∠ ⊥ // [AB]"). */
  public setSlotSymbols(regionName: string, slotId: string, text: string) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, symbols: parseList(text, SPACE_SEPARATOR) }));
  }

  /** Hazır palet: eksik semboller sona eklenir (geometri + noktalama birlikte kullanılabilir). */
  public applySlotPalettePreset(regionName: string, slotId: string, preset: SlotPalettePreset) {
    const symbols = SLOT_PALETTE_PRESETS[preset] ?? [];
    this.updateSlot(regionName, slotId, (slot) => ({
      ...slot,
      symbols: [...slot.symbols, ...symbols.filter((s) => !slot.symbols.includes(s))].slice(0, SLOT_FILL_LIMITS.maxPaletteSymbols),
    }));
  }

  /** Palet kabul listesi: her satıra bir cevap. */
  public setSlotPaletteAccept(regionName: string, slotId: string, text: string) {
    this.updateSlot(regionName, slotId, (slot) => ({ ...slot, paletteAccept: parseList(text, LINE_SEPARATOR) }));
  }

  /** "Tüm kutulara uygula": tür ve ayarlar (giriş, birim, eşdeğerlik, seçenekler, semboller) kopyalanır; doğru değerler kalır. */
  public applySlotSettingsToAll(regionName: string, slotId: string) {
    const state = this.ensureSlotFillState(regionName);
    const source = state.slots.find((s) => s.id === slotId);
    if (!source) return;
    state.slots = state.slots.map((s) => (s.id === slotId ? s : copySlotSettings(source, s)));
    this.commitSlotFill(regionName, state);
  }

  public removeSlot(regionName: string, slotId: string) {
    const state = this.ensureSlotFillState(regionName);
    state.slots = state.slots.filter((s) => s.id !== slotId);
    if (state.selectedSlotId === slotId) state.selectedSlotId = state.slots.at(-1)?.id ?? null;
    this.commitSlotFill(regionName, state);
  }

  public removeLastSlot(regionName: string) {
    const last = this.ensureSlotFillState(regionName).slots.at(-1);
    if (last) this.removeSlot(regionName, last.id);
  }

  public clearSlots(regionName: string) {
    const state = this.ensureSlotFillState(regionName);
    state.slots = [];
    state.selectedSlotId = null;
    this.commitSlotFill(regionName, state);
  }

  /** Müfettiş önizlemesi: "öğrenci şunu yazarsa doğru mu?" — boş cevapta ya da kutu ayarı eksikken null. */
  public previewSlotAnswer(regionName: string, slotId: string, answer: string): boolean | null {
    const slot = this.ensureSlotFillState(regionName).slots.find((s) => s.id === slotId);
    if (!slot || !(answer ?? '').trim() || slotIssues(slot).length > 0) return null;
    return isSlotAnswerCorrect(slot.kind, slotSolution(slot), answer);
  }

  /** Sürüklemeyle çizilen kutu: yeni kutu, seçili (yoksa son) kutunun ayarlarıyla açılır ve seçilir. */
  private addSlotFromDrag(questionIndex: number, x1: number, y1: number, x2: number, y2: number) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    const absX1 = Math.min(x1, x2);
    const absY1 = Math.min(y1, y2);
    const absX2 = Math.max(x1, x2);
    const absY2 = Math.max(y1, y2);
    if (absX2 - absX1 < 6 || absY2 - absY1 < 6) return;
    const within =
      absX1 >= region.x && absY1 >= region.y && absX2 <= region.x + region.width && absY2 <= region.y + region.height;
    if (!within) {
      this.snackBar.open(this.tr('messages.slotOutsideQuestion'), this.tr('common.ok'), { duration: 2000 });
      return;
    }
    // Araç çubuğundaki "Kutu" modu başka türde bir soruya çizerse çizim kaybolmasın: soru cevap kutusuna döner.
    if (this.getInteractionType(region.name) !== SLOT_FILL) {
      this.interactionTypes.set(region.name, SLOT_FILL);
    }
    const state = this.ensureSlotFillState(region.name);
    if (state.slots.length >= SLOT_FILL_LIMITS.maxSlots) {
      this.snackBar.open(this.tr('warnings.slotTooMany', { max: SLOT_FILL_LIMITS.maxSlots }), this.tr('common.ok'), {
        duration: 2500,
      });
      return;
    }
    const box: SlotBox = {
      x: Math.round(absX1 - region.x),
      y: Math.round(absY1 - region.y),
      width: Math.max(1, Math.round(absX2 - absX1)),
      height: Math.max(1, Math.round(absY2 - absY1)),
    };
    const template = state.slots.find((s) => s.id === state.selectedSlotId) ?? state.slots.at(-1) ?? null;
    const id = nextSlotId('s', state.slots.map((s) => s.id));
    state.slots = [...state.slots, newSlot(id, box, template)];
    state.selectedSlotId = id;
    this.slotFillState.set(region.name, { ...state });
    this.focusQuestionKeepingMode(questionIndex);
  }

  /** Kayıt engelleyen cevap kutusu sorunları (uyarı ikonu + Kaydet). */
  private slotFillMessages(regionName: string): string[] {
    return slotFillIssues(this.ensureSlotFillState(regionName)).map((issue) =>
      issue.code === 'noSlots'
        ? this.tr('warnings.slotNone')
        : issue.code === 'tooManySlots'
          ? this.tr('warnings.slotTooMany', { max: SLOT_FILL_LIMITS.maxSlots })
          : this.tr(`warnings.slot.${issue.code}`, { n: issue.no })
    );
  }

  // -------- Issue #576: sürükle-yerleştir (dragPlace) ve eşleştirme (match) yazarlığı --------

  public ensureDragPlaceState(regionName: string): DragPlaceAuthoringState {
    const existing = this.dragPlaceState.get(regionName);
    if (existing) return existing;
    const created = createDragPlaceState();
    this.dragPlaceState.set(regionName, created);
    return created;
  }

  public ensureMatchState(regionName: string): MatchAuthoringState {
    const existing = this.matchState.get(regionName);
    if (existing) return existing;
    const created = createMatchState();
    this.matchState.set(regionName, created);
    return created;
  }

  private updateDragPlace(regionName: string, change: (state: DragPlaceAuthoringState) => DragPlaceAuthoringState) {
    this.dragPlaceState.set(regionName, change(this.ensureDragPlaceState(regionName)));
    this.drawImage();
  }

  private updateMatch(regionName: string, change: (state: MatchAuthoringState) => MatchAuthoringState) {
    this.matchState.set(regionName, change(this.ensureMatchState(regionName)));
    this.drawImage();
  }

  /** Hedef / kart çizim modu: soru sürükle-yerleştir türüne geçer; araç çubuğu kilidi varsa kilit de güncellenir. */
  public selectDragPlaceMode(questionIndex: number, mode: 'placeTarget' | 'placeCard') {
    this.selectInteractionMode(questionIndex, DRAG_PLACE, mode);
  }

  /** Sol / sağ öğe çizim modu: soru eşleştirme türüne geçer. */
  public selectMatchMode(questionIndex: number, mode: 'matchLeft' | 'matchRight') {
    this.selectInteractionMode(questionIndex, MATCH, mode);
  }

  private selectInteractionMode(
    questionIndex: number,
    type: typeof DRAG_PLACE | typeof MATCH | typeof TEXT_SELECT,
    mode: CanvasSelectionMode
  ) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) return;
    this.selectedQuestionIndex = questionIndex;
    const regionName = this.regions()[questionIndex].name;
    this.interactionTypes.set(regionName, type);
    if (type === DRAG_PLACE) this.ensureDragPlaceState(regionName);
    else if (type === MATCH) this.ensureMatchState(regionName);
    else this.ensureTextSelectState(regionName);
    if (this.selectionModeLocked()) this.selectionModeLocked.set(mode);
    this.selectionMode.set(mode);
    this.drawImage();
  }

  public setDragPlaceMode(regionName: string, mode: DragPlaceMode) {
    this.updateDragPlace(regionName, (s) => setDragPlaceMode(s, mode));
  }

  public setDragPlaceReuse(regionName: string, reuse: boolean) {
    this.updateDragPlace(regionName, (s) => setDragPlaceReuse(s, reuse));
  }

  /** Kutusuz hedef (kova; sıra modunda konum). */
  public addDragPlaceBucket(regionName: string, label = '') {
    this.updateDragPlace(regionName, (s) => addPlaceTarget(s, null, label));
  }

  public addDragPlaceCardsFromText(regionName: string, text: string) {
    this.updateDragPlace(regionName, (s) => addPlaceCardsFromText(s, text));
  }

  public removeDragPlaceTarget(regionName: string, targetId: string) {
    this.updateDragPlace(regionName, (s) => removePlaceTarget(s, targetId));
  }

  public removeDragPlaceCard(regionName: string, cardId: string) {
    this.updateDragPlace(regionName, (s) => removePlaceCard(s, cardId));
  }

  public setDragPlaceTargetLabel(regionName: string, targetId: string, label: string) {
    this.updateDragPlace(regionName, (s) => setPlaceTargetLabel(s, targetId, label));
  }

  public setDragPlaceTargetCapacity(regionName: string, targetId: string, value: string | number) {
    this.updateDragPlace(regionName, (s) => setPlaceTargetCapacity(s, targetId, Number(value)));
  }

  public setDragPlaceCardLabel(regionName: string, cardId: string, label: string) {
    this.updateDragPlace(regionName, (s) => setPlaceCardLabel(s, cardId, label));
  }

  public selectDragPlaceTarget(regionName: string, targetId: string | null) {
    this.updateDragPlace(regionName, (s) => selectPlaceTarget(s, targetId));
  }

  /** Kartı hedefin doğru kartı yap / kaldır; çok kartlık hedef doluysa bildirilir. */
  public toggleDragPlaceAssignment(regionName: string, targetId: string, cardId: string) {
    const before = this.ensureDragPlaceState(regionName);
    const after = togglePlaceAssignment(before, targetId, cardId);
    if (after === before) {
      this.snackBar.open(this.tr('messages.placeTargetFull'), this.tr('common.ok'), { duration: 2500 });
      return;
    }
    this.dragPlaceState.set(regionName, after);
    this.drawImage();
  }

  public fillDragPlaceOrderFromCards(regionName: string) {
    this.updateDragPlace(regionName, (s) => fillOrderFromCards(s));
  }

  public addMatchItemsFromText(regionName: string, side: MatchSide, text: string) {
    this.updateMatch(regionName, (s) => addMatchItemsFromText(s, side, text));
  }

  public removeMatchItemFrom(regionName: string, side: MatchSide, id: string) {
    this.updateMatch(regionName, (s) => removeMatchItem(s, side, id));
  }

  public setMatchItemLabel(regionName: string, side: MatchSide, id: string, label: string) {
    this.updateMatch(regionName, (s) => setMatchItemLabel(s, side, id, label));
  }

  public setMatchPair(regionName: string, leftId: string, rightId: string | null) {
    this.updateMatch(regionName, (s) => setMatchPair(s, leftId, rightId || null));
  }

  public toggleMatchPrelinked(regionName: string, leftId: string) {
    this.updateMatch(regionName, (s) => toggleMatchPrelinked(s, leftId));
  }

  public selectMatchLeft(regionName: string, leftId: string | null) {
    this.updateMatch(regionName, (s) => selectMatchLeft(s, leftId));
  }

  /** Tuvalde hedef / kart / eşleştirme öğesine tık (çizim modu değişmez). */
  private onAuthoringItemClick(
    regionName: string,
    hit: Extract<RegionOrAnswerHit, { type: 'placeTarget' | 'placeCard' | 'matchItem' }>
  ) {
    if (hit.type === 'placeTarget') {
      this.selectDragPlaceTarget(regionName, hit.value.targetId);
    } else if (hit.type === 'placeCard') {
      const targetId = this.ensureDragPlaceState(regionName).selectedTargetId;
      if (targetId) this.toggleDragPlaceAssignment(regionName, targetId, hit.value.cardId);
    } else if (hit.value.side === 'left') {
      this.selectMatchLeft(regionName, hit.value.itemId);
    } else {
      const leftId = this.ensureMatchState(regionName).selectedLeftId;
      if (leftId) this.setMatchPair(regionName, leftId, hit.value.itemId);
    }
  }

  /**
   * Sürüklemeyle çizilen hedef / kart / eşleştirme öğesi (soru bölgesine göre). Araç çubuğu modu başka türde bir soruya
   * çizerse çizim kaybolmasın: soru o türe döner.
   */
  private addInteractionBoxFromDrag(mode: CanvasSelectionMode, questionIndex: number, x1: number, y1: number, x2: number, y2: number) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    const absX1 = Math.min(x1, x2);
    const absY1 = Math.min(y1, y2);
    const absX2 = Math.max(x1, x2);
    const absY2 = Math.max(y1, y2);
    if (absX2 - absX1 < 6 || absY2 - absY1 < 6) return;
    const within =
      absX1 >= region.x && absY1 >= region.y && absX2 <= region.x + region.width && absY2 <= region.y + region.height;
    if (!within) {
      this.snackBar.open(this.tr('messages.boxOutsideQuestion'), this.tr('common.ok'), { duration: 2000 });
      return;
    }
    const box: InteractionBox = {
      x: Math.round(absX1 - region.x),
      y: Math.round(absY1 - region.y),
      width: Math.max(1, Math.round(absX2 - absX1)),
      height: Math.max(1, Math.round(absY2 - absY1)),
    };
    if (mode === 'placeTarget' || mode === 'placeCard') {
      this.interactionTypes.set(region.name, DRAG_PLACE);
      const state = this.ensureDragPlaceState(region.name);
      const next = mode === 'placeTarget' ? addPlaceTarget(state, box) : addPlaceCardBox(state, box);
      if (next === state) {
        this.snackBar.open(this.tr('messages.interactionLimit'), this.tr('common.ok'), { duration: 2500 });
        return;
      }
      this.dragPlaceState.set(region.name, next);
    } else {
      this.interactionTypes.set(region.name, MATCH);
      const state = this.ensureMatchState(region.name);
      const next = addMatchItem(state, mode === 'matchLeft' ? 'left' : 'right', box);
      if (next === state) {
        this.snackBar.open(this.tr('messages.interactionLimit'), this.tr('common.ok'), { duration: 2500 });
        return;
      }
      this.matchState.set(region.name, next);
    }
    this.focusQuestionKeepingMode(questionIndex);
  }

  /** Kayıt engelleyen sürükle-yerleştir sorunları (uyarı ikonu + Kaydet). */
  private dragPlaceMessages(regionName: string): string[] {
    return dragPlaceIssues(this.ensureDragPlaceState(regionName)).map((issue) =>
      this.tr(`warnings.dragPlace.${issue.code}`, { n: issue.no })
    );
  }

  /** Kayıt engelleyen eşleştirme sorunları. */
  private matchMessages(regionName: string): string[] {
    return matchIssues(this.ensureMatchState(regionName)).map((issue) =>
      this.tr(`warnings.match.${issue.code}`, { n: issue.no, count: issue.count })
    );
  }

  // -------- Issue #582: metin üzerinde işaretleme (textSelect) yazarlığı --------

  public ensureTextSelectState(regionName: string): TextSelectAuthoringState {
    const existing = this.textSelectState.get(regionName);
    if (existing) return existing;
    const created = createTextSelectState();
    this.textSelectState.set(regionName, created);
    return created;
  }

  private updateTextSelect(regionName: string, change: (state: TextSelectAuthoringState) => TextSelectAuthoringState) {
    this.textSelectState.set(regionName, change(this.ensureTextSelectState(regionName)));
    this.drawImage();
  }

  /** "Satır kutusu çiz" modu: soru metin işaretleme türüne geçer; çizilen kutu bekleyen satır olur. */
  public selectTextLineMode(questionIndex: number) {
    this.selectInteractionMode(questionIndex, TEXT_SELECT, 'textLine');
  }

  public setTextSelectMode(regionName: string, mode: TextSelectMode) {
    this.updateTextSelect(regionName, (s) => setTextSelectMode(s, mode));
  }

  public setTextSelectStyle(regionName: string, style: TextSelectStyle) {
    this.updateTextSelect(regionName, (s) => setTextSelectStyle(s, style));
  }

  /** Palet metni (boşlukla ayrılmış semboller). */
  public setTextSelectPalette(regionName: string, text: string) {
    this.updateTextSelect(regionName, (s) => setTextSelectPalette(s, parsePaletteText(text)));
  }

  public selectTextSelectToken(regionName: string, tokenId: string | null) {
    this.updateTextSelect(regionName, (s) => selectTextToken(s, tokenId));
  }

  public setTextSelectTokenText(regionName: string, tokenId: string, text: string) {
    this.updateTextSelect(regionName, (s) => setTextTokenText(s, tokenId, text));
  }

  public removeTextSelectToken(regionName: string, tokenId: string) {
    this.updateTextSelect(regionName, (s) => removeTextToken(s, tokenId));
  }

  public toggleTextSelectTarget(regionName: string, tokenId: string) {
    this.updateTextSelect(regionName, (s) => toggleTextTarget(selectTextToken(s, tokenId), tokenId));
  }

  public splitTextSelectTarget(regionName: string, index: number) {
    this.updateTextSelect(regionName, (s) => splitTextTarget(s, index));
  }

  public removeTextSelectTarget(regionName: string, index: number) {
    this.updateTextSelect(regionName, (s) => removeTextTarget(s, index));
  }

  public setTextSelectTargetAccept(regionName: string, index: number, text: string) {
    this.updateTextSelect(regionName, (s) => setTextTargetAccept(s, index, parseAcceptText(text)));
  }

  public setTextSelectTargetSuffixes(regionName: string, index: number, text: string) {
    this.updateTextSelect(regionName, (s) => setTextTargetSuffixes(s, index, parseAcceptText(text)));
  }

  public toggleTextSelectGapSymbol(regionName: string, gapId: string, symbol: string) {
    this.updateTextSelect(regionName, (s) => toggleGapSymbol(s, gapId, symbol));
  }

  public clearPendingTextLine(regionName: string) {
    this.updateTextSelect(regionName, (s) => setPendingTextLine(s, null));
  }

  /**
   * "Satır ekle": bekleyen satır kutusuna yazılan metin token'lara bölünür. Kutu yoksa ya da metin boşsa false; token sınırı
   * aşılırsa bildirilir.
   */
  public addTextSelectLine(regionName: string, text: string): boolean {
    const state = this.ensureTextSelectState(regionName);
    if (!state.pendingLine || !text.trim()) return false;
    const next = addTextLine(state, state.pendingLine, text);
    if (next === state) {
      this.snackBar.open(this.tr('messages.interactionLimit'), this.tr('common.ok'), { duration: 2500 });
      return false;
    }
    this.textSelectState.set(regionName, next);
    this.regions.set([...this.regions()]);
    this.drawImage();
    return true;
  }

  /** Tuvalde token'a tık: select / correct'te hedef işaretle / kaldır, punctuation'da yalnız seç (sembol müfettişte). */
  public clickTextSelectToken(regionName: string, tokenId: string) {
    const state = this.ensureTextSelectState(regionName);
    const token = state.tokens.find((t) => t.id === tokenId);
    if (!token) return;
    if (state.mode !== 'punctuation' && !token.gap) {
      this.toggleTextSelectTarget(regionName, tokenId);
    } else {
      this.selectTextSelectToken(regionName, tokenId);
    }
  }

  /** Sürüklemeyle çizilen satır kutusu (soru bölgesine göre, yuvarlanmış); metni müfettişte yazılır. */
  private setTextLineFromDrag(questionIndex: number, x1: number, y1: number, x2: number, y2: number) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    const absX1 = Math.max(Math.min(x1, x2), region.x);
    const absY1 = Math.max(Math.min(y1, y2), region.y);
    const absX2 = Math.min(Math.max(x1, x2), region.x + region.width);
    const absY2 = Math.min(Math.max(y1, y2), region.y + region.height);
    if (absX2 - absX1 < 6 || absY2 - absY1 < 4) return;
    const box: InteractionBox = {
      x: Math.round(absX1 - region.x),
      y: Math.round(absY1 - region.y),
      width: Math.max(1, Math.round(absX2 - absX1)),
      height: Math.max(1, Math.round(absY2 - absY1)),
    };
    this.interactionTypes.set(region.name, TEXT_SELECT);
    this.textSelectState.set(region.name, setPendingTextLine(this.ensureTextSelectState(region.name), box));
    this.focusQuestionKeepingMode(questionIndex);
  }

  // -------- Issue #583: açık uçlu cevap (openText) yazarlığı --------

  public ensureOpenTextState(regionName: string): OpenTextAuthoringState {
    const existing = this.openTextState.get(regionName);
    if (existing) return existing;
    const created = createOpenTextState();
    this.openTextState.set(regionName, created);
    return created;
  }

  private updateOpenText(regionName: string, change: (state: OpenTextAuthoringState) => OpenTextAuthoringState) {
    this.openTextState.set(regionName, change(this.ensureOpenTextState(regionName)));
    this.regions.set([...this.regions()]);
    const region = this.regions().find((r) => r.name === regionName);
    if (region) this.checkRegionWarning(region);
  }

  /** Sözcük sınırı / satır alanı: boş = sınırsız (satırda varsayılan); tam sayı değilse uyarı, kayıt engellenir. */
  public setOpenTextLimit(regionName: string, field: 'minWords' | 'maxWords' | 'lines', text: string) {
    this.updateOpenText(regionName, (s) => setOpenTextLimit(s, field, parseLimitInput(text)));
  }

  public setOpenTextSolutionText(regionName: string, field: 'sampleAnswer' | 'rubric', text: string) {
    this.updateOpenText(regionName, (s) => setOpenTextSolutionText(s, field, text));
  }

  /** Kayıt engelleyen açık uçlu cevap sorunları. */
  private openTextMessages(regionName: string): string[] {
    return openTextIssues(this.ensureOpenTextState(regionName)).map((issue) =>
      this.tr(`warnings.openText.${issue.code}`, {
        maxWords: OPEN_TEXT_LIMITS.maxWordLimit,
        maxLines: OPEN_TEXT_LIMITS.maxLines,
        maxLength: OPEN_TEXT_LIMITS.maxSolutionTextLength,
      })
    );
  }

  /** Kayıt engelleyen metin işaretleme sorunları. */
  // -------- Issue #587: görsel üzerinde boyama bölgeleri (regionPaint, bölge modu) yazarlığı --------

  public ensurePaintRegionsState(regionName: string): PaintRegionsAuthoringState {
    const existing = this.paintRegionsState.get(regionName);
    if (existing) return existing;
    const created = createPaintRegionsState();
    this.paintRegionsState.set(regionName, created);
    return created;
  }

  private updatePaintRegions(regionName: string, change: (state: PaintRegionsAuthoringState) => PaintRegionsAuthoringState) {
    this.paintRegionsState.set(regionName, change(this.ensurePaintRegionsState(regionName)));
    this.regions.set([...this.regions()]);
    const region = this.regions().find((r) => r.name === regionName);
    if (region) this.checkRegionWarning(region);
    this.drawImage();
  }

  /** Çizim aracı: sürükleyerek dikdörtgen / elips (`paintRegion`) ya da köşe köşe çokgen (`paintPolygon`). */
  public selectPaintRegionMode(questionIndex: number, mode: 'paintRegion' | 'paintPolygon', shape?: PaintDrawShape) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    this.interactionTypes.set(region.name, REGION_PAINT);
    if (shape) this.paintRegionsState.set(region.name, setPaintShape(this.ensurePaintRegionsState(region.name), shape));
    if (mode !== 'paintPolygon') {
      this.paintRegionsState.set(region.name, cancelPendingPolygon(this.ensurePaintRegionsState(region.name)));
    }
    this.selectionMode.set(mode);
    this.focusQuestionKeepingMode(questionIndex);
  }

  public setPaintRegionsStyle(regionName: string, style: RegionPaintStyle) {
    this.updatePaintRegions(regionName, (s) => setPaintStyle(s, style));
  }

  public setPaintRegionsBrush(regionName: string, brush: string | null) {
    this.updatePaintRegions(regionName, (s) => setPaintBrush(s, brush));
  }

  public addPaintRegionsColor(regionName: string, color?: PaintColor) {
    this.updatePaintRegions(regionName, (s) => addPaintColor(s, color));
  }

  public removePaintRegionsColor(regionName: string, id: string) {
    this.updatePaintRegions(regionName, (s) => removePaintColor(s, id));
  }

  public setPaintRegionsColor(regionName: string, id: string, color: PaintColor) {
    this.updatePaintRegions(regionName, (s) => setPaintColor(s, id, color));
  }

  public setPaintRegionsColorLabel(regionName: string, id: string, label: string) {
    this.updatePaintRegions(regionName, (s) => setPaintLabel(s, id, label));
  }

  public setPaintRegionLabel(regionName: string, id: string, label: string) {
    this.updatePaintRegions(regionName, (s) => setPaintRegionLabel(s, id, label));
  }

  public setPaintRegionFill(regionName: string, id: string, paint: string | null) {
    this.updatePaintRegions(regionName, (s) => setPaintRegionFill(s, id, paint));
  }

  public removePaintRegion(regionName: string, id: string) {
    this.updatePaintRegions(regionName, (s) => removePaintRegion(s, id));
  }

  public clearPaintRegions(regionName: string) {
    this.updatePaintRegions(regionName, (s) => clearPaintRegions(s));
  }

  public finishPaintPolygon(regionName: string) {
    this.updatePaintRegions(regionName, (s) => finishPendingPolygon(s));
  }

  public cancelPaintPolygon(regionName: string) {
    this.updatePaintRegions(regionName, (s) => cancelPendingPolygon(s));
  }

  /** Sürüklemeyle çizilen bölge (soru bölgesine kırpılır, göreli); biçim durumdaki `shape`. */
  private addPaintRegionFromDrag(questionIndex: number, x1: number, y1: number, x2: number, y2: number) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    const absX1 = Math.max(Math.min(x1, x2), region.x);
    const absY1 = Math.max(Math.min(y1, y2), region.y);
    const absX2 = Math.min(Math.max(x1, x2), region.x + region.width);
    const absY2 = Math.min(Math.max(y1, y2), region.y + region.height);
    if (absX2 - absX1 < 6 || absY2 - absY1 < 6) return;
    const box: InteractionBox = { x: absX1 - region.x, y: absY1 - region.y, width: absX2 - absX1, height: absY2 - absY1 };
    this.interactionTypes.set(region.name, REGION_PAINT);
    const state = this.ensurePaintRegionsState(region.name);
    if (state.regions.length >= REGION_PAINT_LIMITS.maxRegions) {
      this.snackBar.open(this.tr('messages.paintRegionsFull', { max: REGION_PAINT_LIMITS.maxRegions }), this.tr('common.ok'), {
        duration: 2500,
      });
      return;
    }
    this.paintRegionsState.set(region.name, addPaintRegion(state, state.shape === 'ellipse' ? ellipsePoints(box) : rectPoints(box)));
    this.focusQuestionKeepingMode(questionIndex);
  }

  /** Çokgen köşesi (soru bölgesinin içine kırpılır). */
  private addPaintPolygonPoint(questionIndex: number, x: number, y: number) {
    const region = this.regions()[questionIndex];
    if (!region) return;
    const point: PaintPoint = [
      Math.min(Math.max(x, region.x), region.x + region.width) - region.x,
      Math.min(Math.max(y, region.y), region.y + region.height) - region.y,
    ];
    this.interactionTypes.set(region.name, REGION_PAINT);
    this.paintRegionsState.set(region.name, addPendingPoint(this.ensurePaintRegionsState(region.name), point));
    this.focusQuestionKeepingMode(questionIndex);
  }

  private paintRegionsMessages(regionName: string): string[] {
    return paintRegionsIssues(this.ensurePaintRegionsState(regionName)).map((code) =>
      this.tr(`warnings.paintRegions.${code}`, { max: REGION_PAINT_LIMITS.maxRegions, maxLabel: REGION_PAINT_LIMITS.maxLabel })
    );
  }

  /** Boyama bölgeleri: hedef bölge kendi renginde yarı saydam, boş kalması gereken kesikli; bekleyen çokgen köşeleriyle. */
  private drawPaintRegionOverlays() {
    const ctx = this.ctx;
    if (!ctx) return;
    const theme = this.getThemeColors();
    for (const region of this.regions()) {
      if (this.getInteractionType(region.name) !== REGION_PAINT) continue;
      const state = this.ensurePaintRegionsState(region.name);
      const colors = new Map(state.palette.map((p) => [p.id, this.cssVar(`--paint-${p.color}`) || p.color]));
      state.regions.forEach((area, i) => {
        const paint = Object.hasOwn(state.fills, area.id) ? state.fills[area.id] : null;
        const color = paint ? (colors.get(paint) ?? theme.accent) : theme.label;
        ctx.save();
        ctx.beginPath();
        area.points.forEach(([px, py], k) => (k === 0 ? ctx.moveTo(region.x + px, region.y + py) : ctx.lineTo(region.x + px, region.y + py)));
        ctx.closePath();
        if (paint) {
          ctx.globalAlpha = 0.35;
          ctx.fillStyle = color;
          ctx.fill();
          ctx.globalAlpha = 1;
        } else {
          ctx.setLineDash([5, 4]);
        }
        ctx.lineWidth = state.selectedRegionId === area.id ? 3 : 1.5;
        ctx.strokeStyle = color;
        ctx.stroke();
        const [lx, ly] = area.points[0];
        ctx.setLineDash([]);
        ctx.font = `bold 12px ${theme.fontFamily}`;
        ctx.fillStyle = color;
        ctx.fillText(String(i + 1), region.x + lx + 3, region.y + ly + 13);
        ctx.restore();
      });
      if (state.pending.length > 0) {
        ctx.save();
        ctx.strokeStyle = theme.accent;
        ctx.fillStyle = theme.accent;
        ctx.lineWidth = 2;
        ctx.beginPath();
        state.pending.forEach(([px, py], k) => (k === 0 ? ctx.moveTo(region.x + px, region.y + py) : ctx.lineTo(region.x + px, region.y + py)));
        ctx.stroke();
        state.pending.forEach(([px, py], k) => {
          ctx.beginPath();
          ctx.arc(region.x + px, region.y + py, k === 0 ? 6 : 3, 0, 2 * Math.PI);
          ctx.fill();
        });
        ctx.restore();
      }
    }
  }

  private textSelectMessages(regionName: string): string[] {
    return textSelectIssues(this.ensureTextSelectState(regionName)).map((issue) =>
      this.tr(`warnings.textSelect.${issue.code}`, { n: issue.no })
    );
  }

  /** Soruyu müfettişe odaklar ama çizim modunu değiştirmez (matris hücresine tık). */
  private focusQuestionKeepingMode(index: number) {
    this.selectedQuestionIndex = index;
    this.selectedRegion = index;
    this.selectedQuestionChange.emit({ index, region: this.regions()[index] ?? null });
    this.drawImage();
  }

  selectQuestion(index: number) {
    this.selectedQuestionIndex = index;
    this.selectedRegion = index;
    const regionName = this.regions()[index]?.name;
    const interactionType = regionName ? this.getInteractionType(regionName) : 'mcq';
    const current = this.selectionMode();
    this.toggleSelectionMode(
      interactionType === 'dragDropLabeling'
        ? 'dropzone'
        : interactionType === MATRIX_CHOICE
          ? current === 'matrixCell' || current === 'matrixRow'
            ? current
            : 'matrixRow'
          : interactionType === SLOT_FILL
            ? 'slot'
            : interactionType === DRAG_PLACE
              ? current === 'placeTarget' || current === 'placeCard'
                ? current
                : 'placeTarget'
              : interactionType === MATCH
                ? current === 'matchLeft' || current === 'matchRight'
                  ? current
                  : 'matchLeft'
                : interactionType === REGION_PAINT
                  ? current === 'paintPolygon'
                    ? current
                    : 'paintRegion'
                  : 'answer'
    );
    this.selectedQuestionChange.emit({ index, region: this.regions()[index] ?? null });
    this.drawImage();
    requestAnimationFrame(() => this.scrollActiveRegionIntoView());
  }

  selectQuestionByName(name: string) {
    const index = this.regions().findIndex((region) => region.name === name);
    if (index !== -1) {
      this.selectQuestion(index);
    }
  }

  /** Müfettiş panelinin odaklandığı bölge (sağ tık seçimi ya da son seçilen soru). */
  get activeRegionIndex(): number | null {
    const regions = this.regions();
    if (!regions.length) {
      return null;
    }
    if (this.selectedRegion != null && this.selectedRegion >= 0 && this.selectedRegion < regions.length) {
      return this.selectedRegion;
    }
    if (this.selectedQuestionIndex >= 0 && this.selectedQuestionIndex < regions.length) {
      return this.selectedQuestionIndex;
    }
    return 0;
  }

  get currentRegion(): QuestionRegion | null {
    const i = this.activeRegionIndex;
    return i == null ? null : this.regions()[i];
  }

  selectNextQuestion() {
    const n = this.regions().length;
    if (!n) {
      return;
    }
    const cur = this.activeRegionIndex ?? -1;
    this.selectQuestion((cur + 1) % n);
  }

  selectPreviousQuestion() {
    const n = this.regions().length;
    if (!n) {
      return;
    }
    const cur = this.activeRegionIndex ?? 0;
    this.selectQuestion((cur - 1 + n) % n);
  }

  /**
   * Soru Müfettişi'nin ihtiyacı olan tüm veriyi tek çağrıda toplar; parent şablonun
   * onlarca ayrı getter yerine tek nesne okumasını sağlar (CD maliyetini düşürür).
   */
  public getActiveInspector(): ActiveInspector | null {
    const index = this.activeRegionIndex;
    if (index == null) {
      return null;
    }
    const region = this.regions()[index];
    if (!region) {
      return null;
    }
    const name = region.name;
    const interactionType = this.getInteractionType(name);
    const isLabeling = interactionType === 'dragDropLabeling';
    const dropZones = isLabeling ? this.getDropZones(name) : [];
    const draggables = isLabeling ? this.getDraggables(name) : [];
    const passageId = this.selectedPassageMap.get(name) || '0';

    const zoneAssignments: Record<string, string> = {};
    for (const z of dropZones) {
      zoneAssignments[z.id] = this.getZoneAssignedDraggable(name, z.id);
    }

    return {
      index,
      no: index + 1,
      region,
      name,
      interactionType,
      blockKind: this.getBlockKind(name),
      activityComponent: this.getActivityComponent(name),
      answers: region.answers ?? [],
      warnings: this.warningMarkers.filter((m) => m.id === region.id).flatMap((m) => m.messages),
      dropZones,
      draggables,
      zoneAssignments,
      labelCount: isLabeling ? this.getLabelCount(name) : 0,
      matrix: interactionType === MATRIX_CHOICE ? this.buildMatrixInspector(name) : null,
      slotFill: interactionType === SLOT_FILL ? buildSlotFillInspector(this.ensureSlotFillState(name)) : null,
      dragPlace: interactionType === DRAG_PLACE ? buildDragPlaceInspector(this.ensureDragPlaceState(name)) : null,
      match: interactionType === MATCH ? buildMatchInspector(this.ensureMatchState(name)) : null,
      textSelect: interactionType === TEXT_SELECT ? buildTextSelectInspector(this.ensureTextSelectState(name)) : null,
      openText: interactionType === OPEN_TEXT ? buildOpenTextInspector(this.ensureOpenTextState(name)) : null,
      paintRegions: interactionType === REGION_PAINT ? buildPaintRegionsInspector(this.ensurePaintRegionsState(name)) : null,
      masks: this.getMasks(name),
      isExample: this.isExample(name),
      exampleAnswer: this.getExampleAnswer(name),
      difficulty: this.previewDifficultyLevel(),
      passages: this.passages(),
      selectedPassageId: passageId,
      passageFirst: this.isPassageFirstEnabledForPassage(passageId === '0' ? null : passageId),
    };
  }

  // getJsonData() {
  //   return {
  //     //imageData: this.imageData(), // 🖼️ Resmin base64 verisini JSON'a ekle
  //     passages: this.passages(), // 📜 Passage'ları ekle
  //     questions: this.regions()
  //   };
  // }

  public toggleAutoMode() {
    this.autoMode.set(!this.autoMode());
  }

  public sendToStudyPage() {
    console.log('Sending to study page...');
    this.questionService.attachStudyPage(this.imageData()).subscribe((response) => {
      if (response.success) {
        this.snackBar.open(`Successfully attached study page with ${response.imageFile}`, 'Tamam', {
          duration: 3000,
        });
        this.nextImage();
      } else {
        this.snackBar.open(`${response.message}`, 'Tamam', {
          duration: 3000,
        });
      }
    });
  }

  /**
   * Sayfayı eğitim verisi olarak question-detector'a gönderir.
   * @param pageComplete false ise sayfadaki soruların hepsi işaretlenmemiştir: sayfa, soru tespit
   *   modelinin eğitiminde kullanılmaz; yalnızca soru kırpmaları (şık modeli için) saklanır.
   * @param advance false ise başarıda sonraki sayfaya geçilmez (çağıran kendi geçişini yapıyorsa;
   *   aksi halde iki sayfa ilerleyip biri atlanır).
   */
  public sendToFix(pageComplete: boolean = true, advance: boolean = true) {
    // Önceki gönderim sürüyor (çift tıklama): aynı sayfa iki kez eğitime gitmesin.
    if (this.sendingToFix()) {
      return;
    }
    // Sayfa yüklenmemişse gönderilecek görsel yok (soru listesi boş diye "boş sayfa" sayılmamalı).
    if (!this.imageData()) {
      if (advance) {
        this.snackBar.open(this.tr('training.noImage'), this.tr('common.ok'), { duration: 3000 });
      }
      return;
    }
    const regions = this.regions();
    // dragDropLabeling / etkileşimli sorular ve etkinlik blokları (#578) MCQ değildir; şık + doğru cevap bekleyen eğitime
    // gönderilmez. Anlatım / cevap anahtarı / gürültü blokları hiç kırpılmaz (sayfada arka plan olarak kalır).
    const mcqQuestions = regions.filter((q) => this.isMcqRegion(q.name));
    const nonTrainable = regions.filter((q) => !this.isMcqRegion(q.name) && !isExcludedBlockKind(this.getBlockKind(q.name)));
    // Hiç soru yok: kullanıcı modelin bulduklarını silmiştir (Eğit = işaretleme doğru), sayfa tamamı
    // arka plan olarak eğitime gider. Soru eğitimine alınmayacaksa gönderilecek bir şey kalmaz.
    const emptyPage = mcqQuestions.length === 0 && nonTrainable.length === 0;
    // advance=false: "Kaydet ve Düzelt"in kayıt sonrası çağrısı — "kaydedildi" mesajı ezilmesin, sessiz dön.
    if (emptyPage && !pageComplete) {
      if (advance) {
        this.snackBar.open(this.tr('training.emptyPageExcluded'), this.tr('common.ok'), { duration: 4000 });
      }
      return;
    }

    // Kasıtlı kural (#578): yalnız tipli/tipsiz etkinlik (ya da etkileşimli soru) varsa sayfa gönderilmez (dragDropOnly);
    // MCQ + etkinlik karışık sayfada MCQ'lar eğitilir, etkinlikler arka plan olur; yalnız anlatım / cevap anahtarı /
    // gürültü varsa sayfa (pageComplete iken) boş sayfa olarak gider.
    if (!emptyPage && mcqQuestions.length === 0) {
      // Gönderilseydi sayfa boş gibi gider, model dragDrop sorularını arka plan öğrenirdi.
      if (advance) {
        this.snackBar.open(this.tr('training.dragDropOnly'), this.tr('common.ok'), { duration: 5000 });
      }
      return;
    }

    // Şık sayısı uyumsuz soruyu question-detector 400 ile reddeder; istek gönderilmez, sayfa ilerlemez.
    // Doğru cevap eğitim verisinde kullanılmadığı için burada aranmaz.
    if (!this.ensureQuestionsValid({ requireCorrectAnswer: false })) {
      return;
    }

    const data = {
      answerCount: this.answerCount(),
      pageComplete: pageComplete,
      imageData: { image_base64: this.imageData() },
      questions: mcqQuestions.map((q) => ({
        x: q.x,
        y: q.y,
        width: q.width,
        height: q.height,
        answers: q.answers.map((a) => ({
          x: a.x,
          y: a.y,
          width: a.width,
          height: a.height,
        })),
      })),
    };
    // Yanıt gelene kadar sayfa değiştiyse (ör. başka yoldan gezinme) bir sayfa daha ilerlenmesin.
    const sentIndex = this.currentImageIndex;
    const sentImage = this.imageData();
    this.sendingToFix.set(true);
    // takeUntilDestroyed yok: komponent yok edilse de eğitim isteği iptal edilmesin (veri kaybolmasın).
    this.questionDetectorService.sendtoFix(data).subscribe({
      next: (data) => {
        this.sendingToFix.set(false);
        if (this.destroyed) {
          return;
        }
        if (data.success) {
          const message = emptyPage
            ? this.tr('training.emptyPageAdded')
            : this.tr('training.questionsAdded', { count: data.added, file: data.imageFile });
          this.snackBar.open(message, this.tr('common.ok'), {
            duration: 3000,
          });
          this.trained.emit({ pageId: String(data.imageFile ?? ''), emptyPage, added: Number(data.added ?? 0) });
          // Manuel kayıt = eğitime giden hal (sürükle-bırak sorular eğitime gitmediği için kayda da girmez) + türü
          // işaretlenmiş soru dışı bloklar (#578; Manuel kaynağı açılınca renkleriyle geri gelsin).
          const manualRegions = regions.filter(
            (q) => mcqQuestions.includes(q) || this.getBlockKind(q.name) !== 'mcq_question'
          );
          this.saveManualRecord(sentImage!, manualRegions);
          if (
            advance &&
            this.advanceOnTrain &&
            this.currentImageIndex === sentIndex &&
            this.imageData() === sentImage
          ) {
            this.nextImage();
          }
        } else {
          // Servis HTTP hatasını { success: false, message: detail } yapar; detail boş olabilir.
          this.snackBar.open(data.message || this.tr('training.sendError'), this.tr('common.ok'), {
            duration: 4000,
          });
        }
      },
      error: (err) => {
        console.error('send-to-fix failed', err);
        this.sendingToFix.set(false);
        if (this.destroyed) {
          return;
        }
        this.snackBar.open(this.tr('training.sendError'), this.tr('common.ok'), { duration: 4000 });
      },
    });
  }

  public predict() {
    // Issue #454: JSON modunda detector JSON bölgelerini ve sınıflandırmayı ezerdi; çağıranlar zaten
    // devre dışı, bu da son emniyet.
    if (this.coordinateJsonMode()) {
      return;
    }
    // Sayfa yüklenmemişse analiz edilecek görsel yok; meşgul durumu açılmaz.
    if (!this.imageData()) {
      return;
    }
    const imageData = { image_base64: this.imageData() };
    // "Analiz başladı" bildirimi yerine tuvalin üstündeki örtü (inProgress) gösterilir.
    const seq = ++this.predictSeq;
    this.inProgress.set(true);
    this.predictFailed.set(false);

    this.questionDetectorService
      .readQrData(imageData)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          console.log(data);
        },
        error: (err) => {
          // QR sonucu yalnız loglanır; kullanıcıya bildirim yok, analizi (inProgress) etkilemez.
          // Sayfada QR yoksa question-detector 404 döner — olağan durum, sessiz geçilir.
          if (err?.status !== 404) {
            console.error('read-qr failed', err);
          }
        },
      });

    this.questionDetectorService
      .predict(imageData)
      // Büyük sayfalar uzun sürebilir; yanıt hiç gelmezse örtü sonsuza dek açık kalmasın.
      .pipe(timeout(PREDICT_TIMEOUT_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (questions) => {
          if (seq === this.predictSeq && Array.isArray(questions?.predictions)) {
            this.lastModelPrediction = { imageData: imageData.image_base64!, list: questions };
            this.detectionSource.set({ kind: 'Model' });
          }
          this.applyPrediction(seq, questions);
        },
        error: (err) => {
          console.error('predict failed', err);
          if (seq !== this.predictSeq) {
            return;
          }
          this.inProgress.set(false);
          this.predictFailed.set(true);
          this.snackBar.open(this.tr('messages.analysisError'), this.tr('common.ok'), { duration: 4000 });
        },
      });
  }

  /** Dış kaynaklı tespit (Claude / kayıt) başlar: örtü açılır; dönen sıra numarası `apply`/`fail`'e verilir. */
  public beginExternalDetection(): number {
    const seq = ++this.predictSeq;
    this.inProgress.set(true);
    this.predictFailed.set(false);
    return seq;
  }

  /**
   * Dış kaynaklı kutuları tuvale uygular (`/predict` ile aynı şekil). Bu arada sayfa değiştiyse ya da yeni analiz
   * başladıysa yok sayılır.
   */
  public applyExternalPrediction(
    seq: number,
    predictions: PredictionList,
    source: DetectionSource,
    carriedBlocks: Prediction[] = []
  ) {
    const current = seq === this.predictSeq;
    if (current) {
      this.detectionSource.set(source);
    }
    this.applyPrediction(seq, predictions);
    // applyPrediction önceki saklananları siler; bu tespitin düşürülen blokları ondan sonra yazılır.
    if (current && carriedBlocks.length > 0 && Array.isArray(predictions?.predictions)) {
      this.carriedBlocks = { imageData: this.imageData(), blocks: carriedBlocks };
    }
  }

  public failExternalDetection(seq: number) {
    if (seq !== this.predictSeq) {
      return;
    }
    this.inProgress.set(false);
    this.predictFailed.set(true);
  }

  /** Son uygulanan tespitten sonra kutular elle değiştirildi mi (kaynak değiştirmeden önce sorulur). */
  public hasUnsavedEdits(): boolean {
    return this.appliedSnapshot !== null && this.serializeBoxes() !== this.appliedSnapshot;
  }

  private serializeBoxes(): string {
    return JSON.stringify(
      this.regions().map((q) => [
        q.x,
        q.y,
        q.width,
        q.height,
        (q.answers ?? []).map((a) => [a.x, a.y, a.width, a.height]),
        // Issue #578: tür / bileşen değişikliği de kaydedilmemiş düzeltmedir.
        this.getBlockKind(q.name),
        this.getActivityComponent(q.name),
        // Issue #579: C bileşeninde dragPlace ↔ match seçimi de düzeltmedir (manuel kayıt tipi taşır).
        this.getBlockKind(q.name) === 'activity' ? this.getInteractionType(q.name) : null,
        // Issue #581: maske de kaydedilmemiş düzeltmedir.
        (this.masks.get(q.name) ?? []).map((m) => [m.x, m.y, m.width, m.height]),
      ])
    );
  }

  /**
   * Eğitime giden son hali exam API'ye manuel kayıt olarak yazar (+ bu sayfanın YOLO tahmini). Eğitim zaten başarılı;
   * kayıt başarısızsa yalnız uyarılır.
   */
  private saveManualRecord(imageData: string, questions: QuestionRegion[]) {
    const source = this.detectionSource();
    let base: { source: 'Model' | 'Ai'; modelName: string | null; promptVersion?: number | null } | null = null;
    if (source?.kind === 'Manual') {
      base = source.base ?? null;
    } else if (source) {
      base = { source: source.kind, modelName: source.modelName ?? null, promptVersion: source.promptVersion ?? null };
    }
    const toPrediction = (box: { x: number; y: number; width: number; height: number }) => ({
      class_id: 0,
      x: Math.round(box.x),
      y: Math.round(box.y),
      width: Math.round(box.width),
      height: Math.round(box.height),
    });
    const predictions: PredictionList = {
      success: true,
      predictions: questions.map((q): Prediction => {
        const kind = this.getBlockKind(q.name);
        // Issue #581: maskeler kayıtta saklanır (bölgenin yuvarlanmış köşesine göre); kayıt uygulanınca geri gelir.
        const masks = this.getMasks(q.name);
        const maskPart = masks.length > 0 ? { masks: masks.map((m) => ({ ...m })) } : {};
        if (kind === 'mcq_question') {
          return {
            ...toPrediction(q),
            subpredictions: (q.answers ?? []).map((a) => ({ ...toPrediction(a), subpredictions: [] })),
            ...maskPart,
          };
        }
        // Issue #578: soru dışı blok şıksız gider; bileşen ve etkileşim tipi yalnız etkinlikte, tip bileşenle tutarlıysa
        // (sunucu aynı kuralla doğrular).
        const component = kind === 'activity' ? this.getActivityComponent(q.name) : null;
        const type = kind === 'activity' ? this.getInteractionType(q.name) : null;
        return {
          ...toPrediction(q),
          subpredictions: [],
          kind,
          ...(component ? { component } : {}),
          ...(type && isBlockInteractionType(type) && isConsistentInteraction(component, type)
            ? { interaction_type: type }
            : {}),
          ...maskPart,
        };
      }),
    };
    // Issue #578: tuvale uygulanmamış soru dışı bloklar (aynı sayfa) kayıtta değiştirilmeden korunur.
    if (this.carriedBlocks?.imageData === imageData) {
      predictions.predictions.push(...this.carriedBlocks.blocks);
    }
    const snapshot = this.lastModelPrediction?.imageData === imageData ? this.lastModelPrediction.list : null;
    this.labelingService
      .saveManual({
        imageBase64: imageData,
        predictions,
        baseSource: base?.source ?? null,
        baseModelName: base?.source === 'Ai' ? base.modelName : null,
        basePromptVersion: base?.source === 'Ai' ? (base.promptVersion ?? null) : null,
        modelSnapshot: snapshot,
      })
      .subscribe({
        next: () => {
          if (!this.destroyed && this.imageData() === imageData) {
            this.detectionSource.set({ kind: 'Manual', base });
            this.appliedSnapshot = this.serializeBoxes();
          }
          this.manualSaved.emit(imageData);
        },
        error: (err) => {
          console.error('labeling manual save failed', err);
          if (!this.destroyed) {
            this.snackBar.open(this.tr('training.manualSaveFailed'), this.tr('common.ok'), { duration: 5000 });
          }
        },
      });
  }

  /** Sayfa değişince süren analizi geçersiz kılar: dönen yanıt yok sayılır, örtü kalkar. */
  private cancelPendingPredict() {
    this.predictSeq++;
    this.inProgress.set(false);
  }

  private applyPrediction(seq: number, questions: PredictionList) {
    // Bu arada sayfa değişti ya da yeni analiz başladı: eski yanıt yeni sayfanın bölgelerini ezmesin.
    if (seq !== this.predictSeq) {
      return;
    }
    // Yanıt işlenirken hata olursa da örtü açık kalmasın.
    this.inProgress.set(false);
    // Servis hatayı 200 + { success: false } olarak da döndürebilir (predictions yok).
    if (!questions || !Array.isArray(questions.predictions)) {
      this.predictFailed.set(true);
      this.snackBar.open(this.tr('messages.analysisError'), this.tr('common.ok'), { duration: 4000 });
      return;
    }

    console.log(questions);
    const imageWidth = Math.max(...questions.predictions.map((q) => q.x + q.width));
    // soruları sırala
    const sorted = questions.predictions
      .filter((q: any) => q.class_id === 0)
      .sort((a, b) => {
        // Sayfanın ortasını bulmak için tahmini bir sınır (örneğin width / 2)
        const middleX = imageWidth / 2;

        // Sol sütunları önce al (a ve b'nin sol sütun mu sağ sütun mu olduğunu kontrol et)
        const aIsLeft = a.x < middleX;
        const bIsLeft = b.x < middleX;

        if (aIsLeft !== bIsLeft) {
          return aIsLeft ? -1 : 1; // Sol sütun önde gelir
        }

        // Aynı sütundalarsa yukarıdan aşağıya (y koordinatına göre) sıralama yap
        return a.y - b.y;
      });
    // Yeni tespit bölgeleri baştan kurar: önceki tespitin tür / etkileşim tipleri (aynı adlı) yeni kutulara taşınmasın.
    this.blockKinds.clear();
    this.carriedBlocks = null;
    this.interactionTypes.clear();
    this.labelingState.clear();
    this.matrixState.clear();
    this.slotFillState.clear();
    this.dragPlaceState.clear();
    this.matchState.clear();
    this.textSelectState.clear();
    this.openTextState.clear();
    this.paintRegionsState.clear();
    this.draftFingerprints.clear();
    this.masks.clear();
    this.regions.set(
      sorted
        // Issue #578: tür alanları bölgeye kopyalanmaz (soru kaydına gitmesin), `blockKinds`'te tutulur.
        .map(({ kind: _kind, component: _component, interaction_type: _type, masks: _masks, ...q }, index) => ({
          ...q,
          name: `Soru ${index + 1}`,
          answers: q.subpredictions
            .filter((a: any) => a.class_id === 0)
            .map((a: any, index: number) => ({
              label: this.tr('labels.answerNumbered', { number: index + 1 }),
              ...a,
              isCorrect: false,
              id: index,
            })),
          // this.getAnswers(questions.predictions, q).map((a, index) => ({
          //   label: `Şık ${index + 1}`,
          //   ...a,
          //   isCorrect: false,
          //   id: index
          // })),
          isExample: false,
          exampleAnswer: null,
          id: index,
          passageId: '',
          imageId: '',
          imageUrl: '',
        }))
    );
    sorted.forEach((prediction, index) => {
      this.applyPredictedKind(`Soru ${index + 1}`, prediction);
      // Issue #581: manuel kayıttaki maskeler geri yüklenir (geçersizler atlanır).
      const masks = validMasks(prediction.masks);
      if (masks.length > 0) this.masks.set(`Soru ${index + 1}`, masks);
    });
    this.detectionRevision.update((n) => n + 1);

    const questionCount = this.regions().length;

    for (let i = 0; i < questionCount; i++) {
      this.alignAnswers(i);
    }
    this.appliedSnapshot = this.serializeBoxes();

    console.log(`${this.regions().length} Adet Soru bulundu`);
    console.log(`Auto Mode: ${this.autoMode()}`);
    console.log(`Q Mode:  ${this.onlyQuestionMode()}`);

    if (
      this.autoMode() &&
      this.regions().length > 2 &&
      (this.onlyQuestionMode() || this.regions().every((region) => region.answers && region.answers.length >= 3))
    ) {
      console.log(`Condition: true`);
    } else {
      console.log(`Condition: false`);
    }

    this.snackBar.open(this.tr('messages.analysisCompleted'), this.tr('common.ok'), { duration: 2000 });
    if (!this.autoMode()) {
      // resmi çiz ve dur
      this.drawImage();
    } else {
      // auto mode ise question sayısını kontrol et
      if (this.regions().length > 1) {
        // ikiden çok fazla soru varsa durabiliriz.
        if (this.onlyQuestionMode()) {
          // ikiden fazla soru var ve sadece soru modundayız dur!
          this.drawImage();
        } else {
          // ikiden fazla soru var ve soru cevap modundayız.
          if (this.regions().every((region) => region.answers && region.answers.length >= 2)) {
            // ikiden fazla soru var soru cevap modundayız ve her soruda en az 2 şık var.
            this.drawImage();
          } else {
            this.nextImage();
          }
        }
      } else {
        this.nextImage();
      }
    }
  }

  public getAnswers(predictions: Prediction[], question: Prediction): Prediction[] {
    const answers = predictions.filter((a: any) => a.class_id === 1);
    const result: Prediction[] = [];
    // Şimdi her cevabı ilgili soruya ekle
    for (const answer of answers) {
      const centerX = answer.x + answer.width / 2;
      const centerY = answer.y + answer.height / 2;

      const withinX = centerX >= question.x && centerX <= question.x + question.width;
      const withinY = centerY >= question.y && centerY <= question.y + question.height;

      if (withinX && withinY) {
        result.push(answer);
        break; // Bir cevap yalnızca bir soruya ait olabilir
      }
    }
    return result;
  }

  public downloadRegionsLite() {
    // const jsonData = {
    //   questions: this.regions().map(region => ({
    //     ...region,
    //     imageUrl: '../data/' + this.imagName()
    //     // name: '../data/' + this.imagName(),
    //   })),
    // }

    const jsonData = this.regions().map((region) => ({
      questions: {
        ...region,
        imageUrl: '../data/' + this.imagName(),
      },
    }));

    const dataStr = 'data:text/json;charset=utf-8,' + encodeURIComponent(JSON.stringify(jsonData, null, 2));
    const downloadAnchor = document.createElement('a');
    downloadAnchor.setAttribute('href', dataStr);
    downloadAnchor.setAttribute('download', 'soru_koordinatlar.json');
    document.body.appendChild(downloadAnchor);
    downloadAnchor.click();
    document.body.removeChild(downloadAnchor);
  }

  public downloadRegions(header: any) {
    const jsonData = {
      imageData: this.imageData(),
      passages: this.passages(),
      questions: this.regions().map((region) => ({
        ...region,
        bookName: this.bookName(),
        isExample: this.isExample(region.name),
        exampleAnswer: this.isExample(region.name) ? this.getExampleAnswer(region.name) : null,
        interactionType: this.getInteractionType(region.name),
        interactionPlan: this.buildInteractionPlanJson(region.name),
        interactionSolution: this.buildInteractionSolutionJson(region.name),
      })),
      header: header,
    };
    const dataStr = 'data:text/json;charset=utf-8,' + encodeURIComponent(JSON.stringify(jsonData, null, 2));
    const downloadAnchor = document.createElement('a');
    downloadAnchor.setAttribute('href', dataStr);
    downloadAnchor.setAttribute('download', 'soru_koordinatlar.json');
    document.body.appendChild(downloadAnchor);
    downloadAnchor.click();
    document.body.removeChild(downloadAnchor);
  }

  // Backend Width/Height int bekler; kesirli koordinat (ölçekli çizim) kaydı 400'e düşürür.
  private roundBox<T extends { x: number; y: number; width: number; height: number }>(box: T): T {
    const isNum = (v: unknown): v is number => typeof v === 'number' && !Number.isNaN(v);
    return {
      ...box,
      x: isNum(box.x) ? Math.round(box.x) : box.x,
      y: isNum(box.y) ? Math.round(box.y) : box.y,
      width: isNum(box.width) ? Math.max(1, Math.round(box.width)) : box.width,
      height: isNum(box.height) ? Math.max(1, Math.round(box.height)) : box.height,
    };
  }

  public getRegions(header: any) {
    return {
      imageData: this.imageData(),
      passages: this.passages().map((p) => this.toBulkPassage(p)),
      // Issue #578: anlatım / cevap anahtarı / gürültü ve tipsiz etkinlik blokları soru olarak kaydedilmez.
      questions: this.regions().filter((region) => this.isSavableRegion(region.name)).map((region) => ({
        ...this.roundBox(region),
        answers: region.answers?.map((a) => this.roundBox(a)),
        ...(region.passage ? { passage: this.roundBox(region.passage) } : {}),
        bookName: this.bookName(),
        isExample: this.isExample(region.name),
        exampleAnswer: this.isExample(region.name) ? this.getExampleAnswer(region.name) : null,
        interactionType: this.getInteractionType(region.name),
        interactionPlan: this.buildInteractionPlanJson(region.name),
        interactionSolution: this.buildInteractionSolutionJson(region.name),
        // Issue #581: basılı cevap maskeleri (bölgeye göreli); sunucu kırpılan görselin üstüne opak boyar.
        ...(this.getMasks(region.name).length > 0 ? { masks: this.getMasks(region.name) } : {}),
      })),
      header: header,
    };
  }

  public resetRegions() {
    this.passages.set([]);
    this.regions.set([]);
    this.imageData.set(null);
    this.imageSrc.set(null);
    this.bookName.set(null);
    this.exampleAnswers.clear();
    this.exampleFlags.clear();
    this.selectedPassageMap.clear();
    this.interactionTypes.clear();
    this.blockKinds.clear();
    this.carriedBlocks = null;
    this.labelingState.clear();
    this.matrixState.clear();
    this.slotFillState.clear();
    this.dragPlaceState.clear();
    this.matchState.clear();
    this.textSelectState.clear();
    this.openTextState.clear();
    this.paintRegionsState.clear();
    this.draftFingerprints.clear();
    this.masks.clear();
    this.warningMarkers = [];
    this.selectedQuestionIndex = -1;
    this.selectedAnswer = -1;
    this.selectedRegion = -1;
    this.selectedWarning = null;
  }

  setCorrectAnswer(questionIndex: number, answerIndex: number, scale: number) {
    const region = this.regions()[questionIndex];

    // Tüm cevapları yanlış yap
    region.answers.forEach((a) => (a.isCorrect = false));

    // Seçilen cevabı doğru yap
    region.answers[answerIndex].isCorrect = true;

    this.regions.set([...this.regions()]); // UI güncelleme

    // Eğer preview mode'daysa backend'e güncelleme gönder
    if (this.previewMode()) {
      const questionId = region.id;
      const correctAnswerId = region.answers[answerIndex].id;

      if (questionId && correctAnswerId) {
        this.questionService.updateCorrectAnswer(questionId, correctAnswerId, scale).subscribe({
          next: (response) => {
            if (response.success) {
              this.snackBar.open(this.tr('messages.correctAnswerUpdated'), this.tr('common.ok'), { duration: 3000 });
            } else {
              this.snackBar.open('Hata: ' + response.message, 'Tamam', { duration: 3000 });
            }
          },
          error: (error) => {
            console.error('Error updating correct answer:', error);
            this.snackBar.open(this.tr('messages.correctAnswerUpdateError'), this.tr('common.ok'), { duration: 3000 });
          },
        });
      }
    }

    this.drawImage();
  }

  onPreviewChoiceSelected(selectedChoice: AnswerChoice) {
    if (!this.previewMode()) return;

    const currentRegion = this.regions()[this.previewCurrentIndex()];
    const answerIndex = currentRegion.answers.findIndex((answer) => answer.id === selectedChoice.id);

    if (answerIndex !== -1) {
      this.setCorrectAnswer(this.previewCurrentIndex(), answerIndex, selectedChoice.scale || 1);
    }
  }

  onQuestionRemove(questionId: number) {
    if (!this.previewMode()) return;

    // Confirmation dialog kullanarak kullanıcıdan onay al
    const confirmRemoval = confirm(this.tr('messages.removeQuestionConfirm'));

    if (confirmRemoval && this.currentTestId()) {
      this.questionService.removeQuestionFromTest(this.currentTestId()!, questionId).subscribe({
        next: (response) => {
          if (response.success) {
            this.snackBar.open(this.tr('messages.questionRemoved'), this.tr('common.ok'), { duration: 3000 });

            // Local regions array'den de soruyu çıkar
            const currentRegions = this.regions();
            const updatedRegions = currentRegions.filter((region) => region.id !== questionId);
            this.regions.set(updatedRegions);

            // Eğer son soruyu sildik ve index sınır dışı kaldıysa, önceki soruya git
            if (this.previewCurrentIndex() >= updatedRegions.length && updatedRegions.length > 0) {
              this.previewCurrentIndex.set(updatedRegions.length - 1);
            }

            // Eğer hiç soru kalmadıysa preview mode'dan çık
            if (updatedRegions.length === 0) {
              this.togglePreviewMode(0); // Dummy testId, zaten false duruma geçiyor
              this.snackBar.open(this.tr('messages.previewClosedNoQuestions'), this.tr('common.ok'), { duration: 3000 });
            }
          } else {
            this.snackBar.open('Hata: ' + response.message, 'Tamam', { duration: 3000 });
          }
        },
        error: (error) => {
          console.error('Error removing question from test:', error);
          this.snackBar.open(this.tr('messages.questionRemoveError'), this.tr('common.ok'), { duration: 3000 });
        },
      });
    }
  }

  sortRegionsByName() {
    const prefix = 'Soru ';
    const regions = this.regions();
    const sortedRegions = regions.slice().sort((a, b) => {
      const aNum = parseInt(a.name.replace(prefix, ''), 10);
      const bNum = parseInt(b.name.replace(prefix, ''), 10);

      return aNum - bNum;
    });

    this.regions.set(sortedRegions);
  }

  removeQuestion(questionIndex: number) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) {
      console.error('Invalid question index');
      return;
    }
    const regionName = this.regions()[questionIndex].name;
    this.exampleAnswers.delete(regionName);
    this.exampleFlags.delete(regionName);
    this.selectedPassageMap.delete(regionName);
    this.interactionTypes.delete(regionName);
    this.blockKinds.delete(regionName);
    this.labelingState.delete(regionName);
    this.matrixState.delete(regionName);
    this.slotFillState.delete(regionName);
    this.dragPlaceState.delete(regionName);
    this.matchState.delete(regionName);
    this.textSelectState.delete(regionName);
    this.openTextState.delete(regionName);
    this.paintRegionsState.delete(regionName);
    this.masks.delete(regionName);
    for (const key of [...this.draftFingerprints.keys()]) {
      if (key.startsWith(`${regionName}\u0000`)) this.draftFingerprints.delete(key);
    }
    this.warningMarkers = this.warningMarkers.filter((w) => w.id !== this.regions()[questionIndex].id);
    this.regions.set(this.regions().filter((_, index) => index !== questionIndex));

    this.sortRegionsByName();
    this.removeContextMenu();
    this.drawImage();
  }

  removeAllQuestions() {
    for (let i = this.regions().length - 1; i >= 0; i--) {
      this.removeQuestion(i);
    }
  }

  removeContextMenu() {
    this.contextMenuVisible = false;
    this.selectedRegion = -1;
    this.selectedAnswer = -1;
  }

  removeAnswer(questionIndex: number, answerIndex: number) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) {
      console.error('Invalid question index');
      return;
    }
    if (answerIndex < 0 || answerIndex >= this.regions()[questionIndex].answers.length) {
      console.error('Invalid answer index');
      return;
    }
    const region = this.regions()[questionIndex];

    // Seçilen şıkkı listeden kaldır
    region.answers.splice(answerIndex, 1);

    // UI'yi güncelle
    this.regions.set([...this.regions()]);

    this.alignAnswers(questionIndex); // 🔄 Şıkları hizala
    this.removeContextMenu();
    this.drawImage();
  }

  private removeDuplicateAnswers(answers: AnswerChoice[]): AnswerChoice[] {
    const thresholdIntersection = 0.5;

    const isOverlappingEnough = (a: AnswerChoice, b: AnswerChoice): boolean => {
      const x1 = Math.max(a.x, b.x);
      const y1 = Math.max(a.y, b.y);
      const x2 = Math.min(a.x + a.width, b.x + b.width);
      const y2 = Math.min(a.y + a.height, b.y + b.height);

      const intersectionArea = Math.max(0, x2 - x1) * Math.max(0, y2 - y1);
      const aArea = a.width * a.height;
      const bArea = b.width * b.height;

      const overlapRatio = intersectionArea / Math.min(aArea, bArea);
      return overlapRatio >= thresholdIntersection;
    };

    const keep: AnswerChoice[] = [];

    for (const current of answers) {
      let shouldKeep = true;

      for (const existing of keep) {
        if (isOverlappingEnough(existing, current)) {
          const existingArea = existing.width * existing.height;
          const currentArea = current.width * current.height;

          if (currentArea > existingArea) {
            // Remove existing, keep current
            const index = keep.indexOf(existing);
            if (index !== -1) {
              keep.splice(index, 1);
            }
            // Devam et, mevcut current eklenecek
          } else {
            shouldKeep = false;
            break; // current küçüktü, ekleme
          }
        }
      }

      if (shouldKeep) {
        keep.push(current);
      }
    }

    return keep;
  }

  private filterOutFarAnswersByRow(answers: AnswerChoice[], rowThresholdY = 15, rowDistanceFactor = 2): AnswerChoice[] {
    if (answers.length <= 2) return answers;

    // 1. Satırları y koordinatına göre gruplandır
    const sortedAnswers = [...answers].sort((a, b) => a.y - b.y);
    const rows: AnswerChoice[][] = [];

    for (const answer of sortedAnswers) {
      let added = false;
      for (const row of rows) {
        if (Math.abs(row[0].y - answer.y) <= rowThresholdY) {
          row.push(answer);
          added = true;
          break;
        }
      }
      if (!added) {
        rows.push([answer]);
      }
    }

    // 2. Ortalama şık yüksekliğini hesapla
    const averageHeight = answers.reduce((sum, a) => sum + a.height, 0) / answers.length;

    // 3. Satırların y ortalamalarını al
    const rowYValues = rows.map((row) => row.reduce((sum, a) => sum + a.y, 0) / row.length);

    // 4. En yakın komşusuyla farkı çok büyük olanları filtrele
    const validRowIndexes = rowYValues.map((y, i) => {
      if (rowYValues.length === 1) return true; // Tek satır varsa koru ✅
      // bir önceki veya sonraki row ile karşılaştır
      const prevDiff = i > 0 ? Math.abs(y - rowYValues[i - 1]) : Infinity;
      const nextDiff = i < rowYValues.length - 1 ? Math.abs(y - rowYValues[i + 1]) : Infinity;
      const closestDiff = Math.min(prevDiff, nextDiff);
      return closestDiff <= averageHeight * rowDistanceFactor;
    });

    // 5. Valid olan satırları geri döndür
    const validRows = rows.filter((_, i) => validRowIndexes[i]);
    return validRows.flat();
  }

  alignAnswers(questionIndex: number, force: boolean = false) {
    if (!this.autoAlign() && !force) return;
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;

    // 0.a Temizle: Üst üste binen benzer kutuları ayıkla
    region.answers = this.removeDuplicateAnswers(region.answers);
    console.log(JSON.stringify(region.answers));
    // 0.b En yoğun y'ye sahip grubu al (dağılmışları at)
    region.answers = this.filterOutFarAnswersByRow(region.answers);

    const thresholdY = 10;

    // 1. Satırları belirle (y farkına göre grupla)
    const sortedAnswers = [...region.answers].sort((a, b) => a.y - b.y);
    const rows: AnswerChoice[][] = [];

    for (const answer of sortedAnswers) {
      let addedToRow = false;
      for (const row of rows) {
        const avgY = row.reduce((sum, a) => sum + (a.y + a.height) / 2, 0) / row.length;
        if (Math.abs(avgY - (answer.y + answer.height) / 2) <= thresholdY) {
          row.push(answer);
          addedToRow = true;
          break;
        }
      }
      if (!addedToRow) rows.push([answer]);
    }

    // Row bilgisi çıktı: kaç row ve her birinde kaç şık var?
    const rowLengths = rows.map((row) => row.length);
    const allSameLength = rowLengths.every((len) => len === rowLengths[0]);

    // Durum 1: Tek satır varsa, tüm şıkların y ve height değerini eşitle
    if (rows.length === 1) {
      const row = rows[0];
      const minY = Math.min(...row.map((a) => a.y));
      const maxY = Math.max(...row.map((a) => a.y + a.height));
      const h = maxY - minY;
      row.forEach((a) => {
        a.y = minY;
        a.height = h;
      });
    }

    // Durum 2: Tüm satırlarda yalnızca bir answer varsa → x ve width eşitle
    else if (rowLengths.every((len) => len === 1)) {
      const allAnswers = rows.map((r) => r[0]);
      const minX = Math.min(...allAnswers.map((a) => a.x));
      const maxX = Math.max(...allAnswers.map((a) => a.x + a.width));
      const w = maxX - minX;
      allAnswers.forEach((a) => {
        a.x = minX;
        a.width = w;
      });
    }

    // Durum 3: Birden fazla satır ve her satırda eşit sayıda şık varsa
    else if (allSameLength && rowLengths[0] > 1) {
      const columnCount = rowLengths[0];

      // Kolon hizalama (x ve width)
      for (let col = 0; col < columnCount; col++) {
        // Aynı kolondaki şıkları bulmak için: her row'daki şıklar arasında x koordinatları yakın olanları grupla
        // Her row'daki şıklar arasında, bu kolona en yakın x'e sahip olanı seç
        const baseRow = rows[0];
        const baseX = baseRow[col].x;
        const thresholdX = 15; // X yakınlık toleransı (piksel)

        const columnAnswers = rows.map((row) => {
          // O satırda baseX'e en yakın x'e sahip olan şıkkı bul
          let minDiff = Infinity;
          let closest = row[0];
          for (const answer of row) {
            const diff = Math.abs(answer.x - baseX);
            if (diff < minDiff && diff <= thresholdX) {
              minDiff = diff;
              closest = answer;
            }
          }
          return closest;
        });
        const minX = Math.min(...columnAnswers.map((a) => a.x));
        const maxX = Math.max(...columnAnswers.map((a) => a.x + a.width));
        const w = maxX - minX;
        columnAnswers.forEach((a) => {
          a.x = minX;
          a.width = w;
        });
      }

      // Satır hizalama (y ve height)
      for (const row of rows) {
        const minY = Math.min(...row.map((a) => a.y));
        const maxY = Math.max(...row.map((a) => a.y + a.height));
        const h = maxY - minY;
        row.forEach((a) => {
          a.y = minY;
          a.height = h;
        });
      }
    }

    // Durum 4 ve 5: Satırlardaki şık sayıları farklıysa → sadece y ve height hizalama
    else {
      for (const row of rows) {
        const minY = Math.min(...row.map((a) => a.y));
        const maxY = Math.max(...row.map((a) => a.y + a.height));
        const h = maxY - minY;
        row.forEach((a) => {
          a.y = minY;
          a.height = h;
        });
      }
    }

    region.answers = rows.flat();
    this.renameAnswers(questionIndex);
    this.regions.set([...this.regions()]);
    this.drawImage();
  }

  alignAnswers_old(questionIndex: number) {
    if (!this.autoAlign()) return;
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;

    const minX = Math.min(...region.answers.map((a) => a.x));
    const minY = Math.min(...region.answers.map((a) => a.y));
    const maxWidth = Math.max(...region.answers.map((a) => a.width));
    const maxHeight = Math.max(...region.answers.map((a) => a.height));

    const isVertical = region.answers.every((a) => Math.abs(a.x - minX) < 10);

    if (isVertical) {
      region.answers.forEach((a) => {
        a.x = Math.max(region.x, minX); // 🔹 X koordinatını soru alanı içinde tut
        a.width = Math.min(region.width, maxWidth); // 🔹 Genişlik soru alanını aşamaz
      });
    } else {
      region.answers.forEach((a) => {
        a.y = Math.max(region.y, minY); // 🔹 Y koordinatını soru alanı içinde tut
        a.height = Math.min(region.height, maxHeight); // 🔹 Yükseklik soru alanını aşamaz
      });
    }

    // 🔄 Şıkları yeniden adlandır (A, B, C, D)
    this.renameAnswers(questionIndex);

    // 🔥 Güncellenmiş şık listesini kaydet ve UI'yi güncelle
    this.regions.set([...this.regions()]);
    this.drawImage();
  }

  renameAnswers(questionIndex: number) {
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;

    // Tüm şıkların X ve Y değerlerini kontrol et
    const minX = Math.min(...region.answers.map((a) => a.x));
    const minY = Math.min(...region.answers.map((a) => a.y));

    // **Dikey mi, yatay mı olduğunu belirle**
    const isVertical = region.answers.every((a) => Math.abs(a.x - minX) < 10); // X'ler yakınsa dikey hizalanmış demektir.

    // **Şıkları sıralayıp A, B, C, D olarak yeniden isimlendir**
    const sortedAnswers = [...region.answers].sort((a, b) => (isVertical ? a.y - b.y : a.x - b.x));

    // A, B, C, D, E ... — şık sayısı kaç olursa olsun son şık dahil harflenir.
    sortedAnswers.forEach((answer, index) => {
      answer.label = String.fromCharCode(65 + index);
    });
    // 🔁 region.answers'ı yeniden sırala
    region.answers = sortedAnswers;
  }

  toggleExampleMode(questionId: string) {
    const isExample = this.exampleFlags.get(questionId) ?? false;
    this.exampleFlags.set(questionId, !isExample);

    if (!isExample) {
      // 📝 Eğer örnek soru seçildiyse, default boş bir metin ekle
      this.exampleAnswers.set(questionId, '');
    } else {
      // ❌ Eğer kapatıldıysa, metni temizle
      this.exampleAnswers.delete(questionId);
    }
  }

  onTextChanged(questionId: string, event: any) {
    const content = event;
    this.exampleAnswers.set(questionId, content);
  }

  isExample(questionId: string): boolean {
    return this.exampleFlags.get(questionId) ?? false;
  }

  getExampleAnswer(questionId: string): string {
    return this.exampleAnswers.get(questionId) ?? '';
  }
}

/** Issue #581: kayıttan gelen maskeler — sonlu, negatif olmayan, kenarı en az 1; en çok `MAX_MASKS_PER_REGION`. */
function validMasks(masks: unknown): MaskBox[] {
  if (!Array.isArray(masks)) return [];
  const ok = (v: unknown, min: number): v is number => typeof v === 'number' && Number.isFinite(v) && v >= min;
  return masks
    .filter((m): m is MaskBox => !!m && ok(m.x, 0) && ok(m.y, 0) && ok(m.width, 1) && ok(m.height, 1))
    .slice(0, MAX_MASKS_PER_REGION)
    .map((m) => ({ x: m.x, y: m.y, width: m.width, height: m.height }));
}

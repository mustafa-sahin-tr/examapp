import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { PASSAGE_INSTRUCTION_MAX_LENGTH, PassageKind, saveValidationMessage } from '../../models/activity';
import {
  AUTHORING_KINDS,
  AUTHORING_LABEL_MAX,
  AuthoringError,
  AuthoringForm,
  AuthoringKind,
  MazeDefaults,
  ClassifyForm,
  MatchForm,
  MatrixForm,
  OrderForm,
  buildDefinition,
  emptyForm,
  parseDefinition,
  validateForm,
} from '../../models/activity-authoring';
import {
  BLANK_TABLE_LIMITS,
  FillTableForm,
  FillTextForm,
  ShortAnswerForm,
  WordPoolForm,
  authorBlanks,
  cellFromBlank,
  cellHasBlank,
  cellToBlank,
} from '../../models/activity-authoring-blanks';
import {
  CROSSWORD_FORM_LIMITS,
  CrosswordEntryForm,
  CrosswordForm,
  answerLetters,
  autoLayout,
  crosswordFormFromDraft,
  entryLength,
} from '../../models/activity-authoring-crossword';
import { CROSSWORD } from '../../models/crossword';
import { LabelingAiModel } from '../../models/labeling';
import {
  WORD_SEARCH_FORM_LIMITS,
  WordSearchEntryForm,
  WordSearchForm,
  wordLetters,
} from '../../models/activity-authoring-word-search';
import {
  REGION_PAINT_FORM_LIMITS,
  RegionPaintEntryForm,
  RegionPaintForm,
  RegionPaintShape,
  nextPaintColor,
  regionPaintTotal,
  targetCells,
} from '../../models/activity-authoring-region-paint';
import { PAINT_COLORS, PaintColor, paintCss } from '../../models/region-paint';
import {
  NUMBER_LINE_FORM_LIMITS,
  NumberLineCardForm,
  NumberLineForm,
  targetTick,
} from '../../models/activity-authoring-number-line';
import { BAR_CHART_FORM_LIMITS, BarChartCategoryForm, BarChartForm } from '../../models/activity-authoring-bar-chart';
import { GRID_DRAW_FORM_LIMITS, GridDrawForm, gridDrawFormTargets } from '../../models/activity-authoring-grid-draw';
import {
  BALANCE_SCALE_FORM_LIMITS,
  BalanceScaleForm,
  BalanceWeightForm,
  balanceFormWeights,
} from '../../models/activity-authoring-balance-scale';
import { findBalance, weightText } from '../../models/balance-scale';
import {
  LOGIC_GRID_FORM_LIMITS,
  LogicGridForm,
  addLogicCategory,
  removeLogicCategory,
  resizeLogicSubjects,
} from '../../models/activity-authoring-logic-grid';
import {
  MAZE_FORM_LIMITS,
  MazeBoardForm,
  MazeRoomsForm,
  MazeStonesForm,
  MazeTreeForm,
  otherRoom,
  resizeRooms,
  resizeTree,
  treeCorrectExit,
  roomDoors,
} from '../../models/activity-authoring-maze';
import { QuestionRegion } from '../../models/draws';
import { Question } from '../../models/question';
import { AuthService } from '../../services/auth.service';
import { LabelingService } from '../../services/labeling.service';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';
import {
  ClassificationSelection,
  ClassificationSelectorComponent,
} from '../../shared/components/classification-selector/classification-selector.component';
import { QuestionCanvasCrosswordComponent } from '../../shared/components/question-canvas-crossword/question-canvas-crossword.component';
import { QuestionCanvasLogicGridComponent } from '../../shared/components/question-canvas-logic-grid/question-canvas-logic-grid.component';
import { QuestionCanvasRegionPaintComponent } from '../../shared/components/question-canvas-region-paint/question-canvas-region-paint.component';
import { QuestionCanvasNumberLineComponent } from '../../shared/components/question-canvas-number-line/question-canvas-number-line.component';
import { QuestionCanvasBarChartComponent } from '../../shared/components/question-canvas-bar-chart/question-canvas-bar-chart.component';
import { QuestionCanvasGridDrawComponent } from '../../shared/components/question-canvas-grid-draw/question-canvas-grid-draw.component';
import { QuestionCanvasBalanceScaleComponent } from '../../shared/components/question-canvas-balance-scale/question-canvas-balance-scale.component';
import { CLOCK_SET_FORM_LIMITS, ClockForm, ClockSetForm } from '../../models/activity-authoring-clock-set';
import { QuestionCanvasClockSetComponent } from '../../shared/components/question-canvas-clock-set/question-canvas-clock-set.component';
import { QuestionCanvasWordSearchComponent } from '../../shared/components/question-canvas-word-search/question-canvas-word-search.component';
import { QuestionCanvasDragPlaceComponent } from '../../shared/components/question-canvas-drag-place/question-canvas-drag-place.component';
import { QuestionCanvasMatchComponent } from '../../shared/components/question-canvas-match/question-canvas-match.component';
import { QuestionCanvasMazeTreeComponent } from '../../shared/components/question-canvas-maze-tree/question-canvas-maze-tree.component';
import { QuestionCanvasMatrixChoiceComponent } from '../../shared/components/question-canvas-matrix-choice/question-canvas-matrix-choice.component';
import { QuestionCanvasSlotFillComponent } from '../../shared/components/question-canvas-slot-fill/question-canvas-slot-fill.component';

export const ACTIVITY_EDITOR_SCOPE = 'activity-editor';

/** İsteğe bağlı görselin üst sınırı (bayt); toplu kayıt gövdesi base64 taşır. */
export const ACTIVITY_EDITOR_MAX_IMAGE_BYTES = 5 * 1024 * 1024;

interface EditorImage {
  /** Yeni seçilen görsel (data URL); mevcut görselde null. */
  dataUrl: string | null;
  /** Önizlemede gösterilen adres (yeni görselde data URL, mevcut görselde imzalı adres). */
  src: string;
  width: number;
  height: number;
}

interface EditableItem {
  id: number;
  order: number;
  kind: AuthoringKind;
  title: string;
}

/**
 * Issue #590: görselsiz etkinlik maddesi editörü — öğretmen kitap görseli olmadan yönerge + içerik yazarak matris seçim
 * (D/Y şablonu), kovaya sınıflandırma, sıralama ve eşleştirme maddesi oluşturur ya da düzenler. Dilim b: boşluk doldurma
 * (`[[cevap]]`), soru + kısa cevap, tablo (`slotFill` düzenli plan) ve kelime havuzu (`dragPlace` boşluklu metin). Aynı tipler ve kontrolcüler
 * (#571), aynı oynatıcılar (önizleme) kullanılır. Yeni madde toplu kayıtla (`questions/save`, görselsiz) etkinlik sayfasına
 * eklenir; düzenleme tekli kayıtla (`questions`, Id > 0). Öğretmenin seçtiği konu kilitlenir (sınıflandırıcı ezmez); seçmezse
 * sınıflandırıcı maddeyi metinle sınıflandırır.
 *
 * Rota: `/activity-editor?testId=&gradeId=` (yeni) ve `&questionId=` (düzenleme). Yönerge düz metindir (#573).
 */
const INLINE_PREVIEW_KINDS: ReadonlySet<AuthoringKind> = new Set<AuthoringKind>(['matrix', 'order', 'mazeTree', 'mazeStones']);

@Component({
  selector: 'app-activity-editor',
  standalone: true,
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatSlideToggleModule,
    TranslocoDirective,
    ClassificationSelectorComponent,
    QuestionCanvasMatrixChoiceComponent,
    QuestionCanvasDragPlaceComponent,
    QuestionCanvasMatchComponent,
    QuestionCanvasSlotFillComponent,
    QuestionCanvasMazeTreeComponent,
    QuestionCanvasCrosswordComponent,
    QuestionCanvasWordSearchComponent,
    QuestionCanvasLogicGridComponent,
    QuestionCanvasRegionPaintComponent,
    QuestionCanvasNumberLineComponent,
    QuestionCanvasBarChartComponent,
    QuestionCanvasGridDrawComponent,
    QuestionCanvasBalanceScaleComponent,
    QuestionCanvasClockSetComponent,
  ],
  providers: [provideTranslocoScope(ACTIVITY_EDITOR_SCOPE)],
  templateUrl: './activity-editor.component.html',
  styleUrl: './activity-editor.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityEditorComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly questionService = inject(QuestionService);
  private readonly testService = inject(TestService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly auth = inject(AuthService);
  private readonly labelingApi = inject(LabelingService);

  readonly kinds = AUTHORING_KINDS;
  readonly instructionMax = PASSAGE_INSTRUCTION_MAX_LENGTH;
  readonly labelMax = AUTHORING_LABEL_MAX;
  readonly tableLimits = BLANK_TABLE_LIMITS;
  readonly cellHasBlank = cellHasBlank;
  readonly mazeLimits = MAZE_FORM_LIMITS;
  readonly mazeLabelMax = MAZE_FORM_LIMITS.maxLabel;
  readonly treeMaxDepth = MAZE_FORM_LIMITS.maxDepth;
  readonly gridSizes = [2, 3, 4];
  readonly crosswordLimits = CROSSWORD_FORM_LIMITS;
  readonly wordSearchLimits = WORD_SEARCH_FORM_LIMITS;
  readonly logicLimits = LOGIC_GRID_FORM_LIMITS;
  readonly paintLimits = REGION_PAINT_FORM_LIMITS;
  readonly paintColors = PAINT_COLORS;
  readonly lineLimits = NUMBER_LINE_FORM_LIMITS;
  readonly chartLimits = BAR_CHART_FORM_LIMITS;
  readonly drawLimits = GRID_DRAW_FORM_LIMITS;
  readonly balanceLimits = BALANCE_SCALE_FORM_LIMITS;
  readonly clockLimits = CLOCK_SET_FORM_LIMITS;
  readonly clockSet = computed(() => (this.form().kind === 'clockSet' ? (this.form() as ClockSetForm) : null));
  /** Otomatik yerleştirmenin son sonucu: kesiştirilemeyip ayrı satıra konan kelimeler (1 tabanlı). */
  readonly crosswordUnplaced = signal<number[] | null>(null);
  /**
   * Issue #586 (dilim e): görselden Claude ile bulmaca çıkarma (`labeling/activity-structure`, yalnız Admin + QuestionTrainer;
   * izinli model yoksa düğme görünmez). Kayıtlı sonuç (aynı görsel + model) ücretsiz döner.
   */
  readonly aiModel = signal<LabelingAiModel | null>(null);
  readonly extracting = signal(false);
  readonly extractError = signal<string | null>(null);

  readonly testId = Number(this.route.snapshot.queryParamMap.get('testId')) || null;
  private readonly gradeId = Number(this.route.snapshot.queryParamMap.get('gradeId')) || null;

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadError = signal(false);
  readonly saveError = signal<string | null>(null);
  /** Kaydet'e basıldıktan sonra doğrulama hataları gösterilir. */
  readonly submitted = signal(false);

  /** Düzenlenen madde (yeni maddede null). */
  readonly editingId = signal<number | null>(null);
  private editingPassageId: number | null = null;
  /** Düzenlenen maddenin kayıtlı görseli: tekli kayıt görseli silemez, yeni seçim kaldırılınca buna dönülür. */
  private originalImage: EditorImage | null = null;
  private initialClassificationKey = '';

  readonly items = signal<EditableItem[]>([]);
  readonly form = signal<AuthoringForm>(emptyForm('matrix', this.trueFalseLabels(), this.mazeDefaults()));
  readonly instruction = signal('');
  readonly image = signal<EditorImage | null>(null);
  readonly imageError = signal<string | null>(null);
  readonly classification = signal<ClassificationSelection | null>(null);
  readonly initialClassification = signal<ClassificationSelection | null>(
    this.gradeId ? { gradeId: this.gradeId, subjectId: null, topicId: null, subtopicIds: [] } : null
  );
  readonly showSolution = signal(false);

  readonly kind = computed(() => this.form().kind);
  readonly matrix = computed(() => (this.form().kind === 'matrix' ? (this.form() as MatrixForm) : null));
  readonly classify = computed(() => (this.form().kind === 'classify' ? (this.form() as ClassifyForm) : null));
  readonly order = computed(() => (this.form().kind === 'order' ? (this.form() as OrderForm) : null));
  readonly match = computed(() => (this.form().kind === 'match' ? (this.form() as MatchForm) : null));
  readonly fillText = computed(() => (this.form().kind === 'fillText' ? (this.form() as FillTextForm) : null));
  readonly shortAnswer = computed(() => (this.form().kind === 'shortAnswer' ? (this.form() as ShortAnswerForm) : null));
  readonly fillTable = computed(() => (this.form().kind === 'fillTable' ? (this.form() as FillTableForm) : null));
  /** Tablo: hücre seçme kipi (açıkken hücreye tık / sürükleme seçer, yazılmaz). */
  readonly tableSelectMode = signal(false);
  /** Seçili hücreler (`satır:kolon`); başlık satırı seçilmez. */
  readonly tableSelection = signal<ReadonlySet<string>>(new Set());
  /** Son odaklanan hücre: seçim yokken "Boşluk yap" ona uygulanır (hücrede seçili metin varsa yalnız o kısım). */
  readonly tableFocus = signal<{ row: number; column: number } | null>(null);
  private tableDrag: { row: number; column: number; adding: boolean; base: ReadonlySet<string> } | null = null;
  readonly wordPool = computed(() => (this.form().kind === 'wordPool' ? (this.form() as WordPoolForm) : null));
  readonly mazeTree = computed(() => (this.form().kind === 'mazeTree' ? (this.form() as MazeTreeForm) : null));
  readonly mazeBoard = computed(() => (this.form().kind === 'mazeBoard' ? (this.form() as MazeBoardForm) : null));
  readonly mazeRooms = computed(() => (this.form().kind === 'mazeRooms' ? (this.form() as MazeRoomsForm) : null));
  readonly mazeStones = computed(() => (this.form().kind === 'mazeStones' ? (this.form() as MazeStonesForm) : null));
  readonly crossword = computed(() => (this.form().kind === 'crossword' ? (this.form() as CrosswordForm) : null));
  readonly regionPaint = computed(() => (this.form().kind === 'regionPaint' ? (this.form() as RegionPaintForm) : null));
  readonly logicGrid = computed(() => (this.form().kind === 'logicGrid' ? (this.form() as LogicGridForm) : null));
  readonly numberLine = computed(() => (this.form().kind === 'numberLine' ? (this.form() as NumberLineForm) : null));
  readonly barChart = computed(() => (this.form().kind === 'barChart' ? (this.form() as BarChartForm) : null));
  readonly gridDraw = computed(() => (this.form().kind === 'gridDraw' ? (this.form() as GridDrawForm) : null));
  /** Kareli zeminde çizim: kurala uyan boyutlar ("1 × 6, 2 × 5, 3 × 4"). */
  readonly gridDrawTargets = computed(() => {
    const f = this.gridDraw();
    return f ? gridDrawFormTargets(f).map((t) => `${t.short} × ${t.long}`) : [];
  });
  readonly balanceScale = computed(() => (this.form().kind === 'balanceScale' ? (this.form() as BalanceScaleForm) : null));
  /** Terazi: kurala uyan bir yerleşim ("5 kg + 14 kg" / "9 kg + 10 kg"); form geçersiz ya da dengelenemiyorsa null. */
  readonly balanceExample = computed(() => {
    const f = this.balanceScale();
    if (!f) return null;
    const weights = balanceFormWeights(f);
    const all = [...weights.cards, ...weights.left, ...weights.right];
    if (!weights.cards.length || all.some((w) => w.value === 0)) return null;
    const pans = findBalance(weights, f.useAll);
    if (!pans) return null;
    const unit = f.unit.trim() || null;
    const side = (fixed: typeof weights.cards, ids: string[]) =>
      [...fixed, ...weights.cards.filter((c) => ids.includes(c.id))].map((w) => weightText(w, unit)).join(' + ');
    return { left: side(weights.left, pans.left), right: side(weights.right, pans.right) };
  });
  readonly wordSearch = computed(() => (this.form().kind === 'wordSearch' ? (this.form() as WordSearchForm) : null));
  /** D/Y ağacının düzeyleri: genişlik öncelikli sırada her düzeyin ifade dizinleri. */
  readonly treeLevels = computed(() => {
    const f = this.mazeTree();
    if (!f) return [];
    return Array.from({ length: f.depth }, (_, depth) => ({
      depth,
      indices: Array.from({ length: 2 ** depth }, (_, i) => 2 ** depth - 1 + i),
    }));
  });
  /** Form öğrencinin göreceği ekranla aynıysa (ağaç, pano, odalar, matris, sıralama, taşlar) ayrı önizleme kolonu yok. */
  readonly inlinePreview = computed(() => INLINE_PREVIEW_KINDS.has(this.form().kind));
  /** Doğru dalları izleyen rota: üzerindeki ifadeler ve vardığı çıkış (ağaçta vurgulanır). */
  readonly treeExit = computed(() => {
    const f = this.mazeTree();
    return f ? treeCorrectExit(f) : -1;
  });
  readonly treeRoute = computed(() => {
    const f = this.mazeTree();
    const route = new Set<number>();
    if (!f) return route;
    for (let i = 0, d = 0; d < f.depth; d++) {
      route.add(i);
      i = 2 * i + 1 + (f.nodes[i]?.correct ?? 0);
    }
    return route;
  });
  /** Metindeki boşluk sayısı (boşluk doldurma / kelime havuzu ipucu). */
  readonly blankCount = computed(() => {
    const f = this.form();
    if (f.kind === 'fillTable') return f.cells.flat().reduce((n, cell) => n + authorBlanks(cell).length, 0);
    return f.kind === 'fillText' || f.kind === 'wordPool' ? authorBlanks(f.text).length : 0;
  });

  readonly errors = computed<AuthoringError[]>(() => validateForm(this.form()));
  readonly definition = computed(() => buildDefinition(this.form()));
  readonly instructionTooLong = computed(() => this.instruction().length > PASSAGE_INSTRUCTION_MAX_LENGTH);
  /** Konu seçiliyse taksonomi kilitlenir; ders seçilip konu seçilmediyse kaydedilmez (ders tek başına kilitlenemez). */
  readonly classificationIncomplete = computed(() => {
    const c = this.classification();
    return !!c?.subjectId && !c.topicId;
  });
  readonly canSave = computed(
    () => !this.saving() && this.errors().length === 0 && !this.instructionTooLong() && !this.classificationIncomplete()
  );

  readonly previewRegion = computed<QuestionRegion>(() => {
    const image = this.image();
    return {
      id: this.editingId() ?? 0,
      name: 'preview',
      x: 0,
      y: 0,
      width: image?.width ?? 0,
      height: image?.height ?? 0,
      passageId: '',
      answers: [],
      imageId: '',
      imageUrl: image?.src ?? '',
      exampleAnswer: null,
      isExample: false,
    };
  });

  /**
   * Etkinliğe başlamış öğrenci sayısı: oturum madde listesini başlarken kopyalar, yeni madde yalnız bundan sonra başlayanlara
   * görünür (bilinçli karar); öğretmen yeni maddeyi yazarken uyarılır.
   */
  readonly startedCount = signal(0);

  constructor() {
    this.load();
    this.loadAiModel();
  }

  load(): void {
    if (!this.testId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }
    this.loading.set(true);
    this.loadError.set(false);
    this.testService.get(this.testId).subscribe({
      next: (exam) => this.startedCount.set(exam?.instanceCount ?? 0),
      error: () => this.startedCount.set(0),
    });
    this.questionService.getAll(this.testId).subscribe({
      next: (questions) => {
        this.items.set(this.editableItems(questions));
        const questionId = Number(this.route.snapshot.queryParamMap.get('questionId')) || null;
        const target = questionId ? questions.find((q) => q.id === questionId) : null;
        if (target) this.startEdit(target);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  // ---- Tip ve ortak alanlar ----

  selectKind(kind: AuthoringKind): void {
    if (kind === this.kind() || this.editingId()) return;
    this.form.set(emptyForm(kind, this.trueFalseLabels(), this.mazeDefaults()));
    this.crosswordUnplaced.set(null);
    this.resetTableSelection();
    this.submitted.set(false);
  }

  setInstruction(value: string): void {
    this.instruction.set(value);
  }

  onClassificationChange(selection: ClassificationSelection): void {
    this.classification.set(selection);
  }

  onImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    this.imageError.set(null);
    if (!file) return;
    if (!file.type.startsWith('image/')) {
      this.imageError.set(this.t('image.invalidType'));
      return;
    }
    if (file.size > ACTIVITY_EDITOR_MAX_IMAGE_BYTES) {
      this.imageError.set(this.t('image.tooLarge', { mb: ACTIVITY_EDITOR_MAX_IMAGE_BYTES / 1024 / 1024 }));
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      const dataUrl = typeof reader.result === 'string' ? reader.result : null;
      if (!dataUrl) return;
      const probe = new Image();
      probe.onload = () =>
        this.image.set({ dataUrl, src: dataUrl, width: probe.naturalWidth, height: probe.naturalHeight });
      probe.onerror = () => this.imageError.set(this.t('image.invalidType'));
      probe.src = dataUrl;
    };
    reader.readAsDataURL(file);
  }

  removeImage(): void {
    this.image.set(this.editingId() ? this.originalImage : null);
  }

  // ---- Matris ----

  applyTrueFalse(): void {
    this.updateMatrix((f) => ({
      ...f,
      columns: [...this.trueFalseLabels()],
      multiPerRow: false,
      rows: f.rows.map((r) => ({ ...r, correct: r.correct.filter((c) => c < 2).slice(0, 1) })),
    }));
  }

  setColumn(index: number, value: string): void {
    this.updateMatrix((f) => ({ ...f, columns: f.columns.map((c, i) => (i === index ? value : c)) }));
  }

  addColumn(): void {
    this.updateMatrix((f) => ({ ...f, columns: [...f.columns, ''] }));
  }

  removeColumn(index: number): void {
    this.updateMatrix((f) => ({
      ...f,
      columns: f.columns.filter((_, i) => i !== index),
      rows: f.rows.map((r) => ({
        ...r,
        correct: r.correct.filter((c) => c !== index).map((c) => (c > index ? c - 1 : c)),
      })),
    }));
  }

  setMultiPerRow(value: boolean): void {
    this.updateMatrix((f) => ({
      ...f,
      multiPerRow: value,
      rows: value ? f.rows : f.rows.map((r) => ({ ...r, correct: r.correct.slice(0, 1) })),
    }));
  }

  setShuffleRows(value: boolean): void {
    this.updateMatrix((f) => ({ ...f, shuffleRows: value }));
  }

  setRowText(index: number, value: string): void {
    this.updateMatrix((f) => ({ ...f, rows: f.rows.map((r, i) => (i === index ? { ...r, text: value } : r)) }));
  }

  isRowCorrect(row: { correct: number[] }, column: number): boolean {
    return row.correct.includes(column);
  }

  toggleRowCorrect(index: number, column: number): void {
    this.updateMatrix((f) => ({
      ...f,
      rows: f.rows.map((r, i) => {
        if (i !== index) return r;
        if (!f.multiPerRow) return { ...r, correct: r.correct.includes(column) ? [] : [column] };
        return {
          ...r,
          correct: r.correct.includes(column) ? r.correct.filter((c) => c !== column) : [...r.correct, column],
        };
      }),
    }));
  }

  addRow(): void {
    this.updateMatrix((f) => ({ ...f, rows: [...f.rows, { text: '', correct: [] }] }));
  }

  removeRow(index: number): void {
    this.updateMatrix((f) => ({ ...f, rows: f.rows.filter((_, i) => i !== index) }));
  }

  // ---- Sınıflandırma (kova) ----

  setBucket(index: number, value: string): void {
    this.updateClassify((f) => ({ ...f, buckets: f.buckets.map((b, i) => (i === index ? value : b)) }));
  }

  addBucket(): void {
    this.updateClassify((f) => ({ ...f, buckets: [...f.buckets, ''] }));
  }

  removeBucket(index: number): void {
    this.updateClassify((f) => ({
      ...f,
      buckets: f.buckets.filter((_, i) => i !== index),
      cards: f.cards.map((c) => ({
        ...c,
        bucket: c.bucket === index ? null : c.bucket !== null && c.bucket > index ? c.bucket - 1 : c.bucket,
      })),
    }));
  }

  setCardText(index: number, value: string): void {
    this.updateClassify((f) => ({ ...f, cards: f.cards.map((c, i) => (i === index ? { ...c, text: value } : c)) }));
  }

  setCardBucket(index: number, bucket: number | null): void {
    this.updateClassify((f) => ({ ...f, cards: f.cards.map((c, i) => (i === index ? { ...c, bucket } : c)) }));
  }

  addCard(): void {
    this.updateClassify((f) => ({ ...f, cards: [...f.cards, { text: '', bucket: f.buckets.length ? 0 : null }] }));
  }

  removeCard(index: number): void {
    this.updateClassify((f) => ({ ...f, cards: f.cards.filter((_, i) => i !== index) }));
  }

  // ---- Sıralama ----

  setOrderItem(index: number, value: string): void {
    this.updateOrder((f) => ({ ...f, items: f.items.map((v, i) => (i === index ? value : v)) }));
  }

  moveOrderItem(index: number, delta: -1 | 1): void {
    this.updateOrder((f) => {
      const target = index + delta;
      if (target < 0 || target >= f.items.length) return f;
      const items = [...f.items];
      [items[index], items[target]] = [items[target], items[index]];
      return { ...f, items };
    });
  }

  addOrderItem(): void {
    this.updateOrder((f) => ({ ...f, items: [...f.items, ''] }));
  }

  removeOrderItem(index: number): void {
    this.updateOrder((f) => ({ ...f, items: f.items.filter((_, i) => i !== index) }));
  }

  // ---- Eşleştirme ----

  setPair(index: number, side: 'left' | 'right', value: string): void {
    this.updateMatch((f) => ({ ...f, pairs: f.pairs.map((p, i) => (i === index ? { ...p, [side]: value } : p)) }));
  }

  addPair(): void {
    this.updateMatch((f) => ({ ...f, pairs: [...f.pairs, { left: '', right: '' }] }));
  }

  removePair(index: number): void {
    this.updateMatch((f) => ({ ...f, pairs: f.pairs.filter((_, i) => i !== index) }));
  }

  setDistractor(index: number, value: string): void {
    this.updateMatch((f) => ({ ...f, distractors: f.distractors.map((d, i) => (i === index ? value : d)) }));
  }

  addDistractor(): void {
    this.updateMatch((f) => ({ ...f, distractors: [...f.distractors, ''] }));
  }

  removeDistractor(index: number): void {
    this.updateMatch((f) => ({ ...f, distractors: f.distractors.filter((_, i) => i !== index) }));
  }

  // ---- D/Y ağacı ----

  setTreeDepth(depth: number): void {
    this.updateMazeTree((f) => resizeTree(f, depth, this.mazeDefaults().exitName));
  }

  setTreeBranch(index: number, value: string): void {
    this.updateMazeTree((f) => ({ ...f, branches: f.branches.map((b, i) => (i === index ? value : b)) as [string, string] }));
  }

  setTreeNode(index: number, value: string): void {
    this.updateMazeTree((f) => ({ ...f, nodes: f.nodes.map((n, i) => (i === index ? { ...n, text: value } : n)) }));
  }

  setTreeCorrect(index: number, side: 0 | 1): void {
    this.updateMazeTree((f) => ({ ...f, nodes: f.nodes.map((n, i) => (i === index ? { ...n, correct: side } : n)) }));
  }

  setTreeExit(index: number, value: string): void {
    this.updateMazeTree((f) => ({ ...f, exits: f.exits.map((x, i) => (i === index ? value : x)) }));
  }

  /** İfadenin kökten yolu ("D → Y"); kök için "Başlangıç". */
  treePath(index: number): string {
    const f = this.mazeTree();
    if (!f || index === 0) return this.t('maze.root');
    const steps: string[] = [];
    let bfs = index + 1;
    while (bfs > 1) {
      steps.unshift(f.branches[bfs % 2] || '?');
      bfs = Math.floor(bfs / 2);
    }
    return steps.join(' → ');
  }

  // ---- Işıklı pano ----

  setBoardFinish(value: string): void {
    this.updateMazeBoard((f) => ({ ...f, finish: value }));
  }

  setBoardRowLabel(index: number, value: string): void {
    this.updateMazeBoard((f) => ({ ...f, rows: f.rows.map((r, i) => (i === index ? { ...r, label: value } : r)) }));
  }

  setBoardBox(row: number, box: number, value: string): void {
    this.updateMazeBoard((f) => ({
      ...f,
      rows: f.rows.map((r, i) => (i === row ? { ...r, boxes: r.boxes.map((b, j) => (j === box ? value : b)) } : r)),
    }));
  }

  setBoardCorrect(row: number, box: number): void {
    this.updateMazeBoard((f) => ({ ...f, rows: f.rows.map((r, i) => (i === row ? { ...r, correct: box } : r)) }));
  }

  addBoardBox(row: number): void {
    this.updateMazeBoard((f) => ({ ...f, rows: f.rows.map((r, i) => (i === row ? { ...r, boxes: [...r.boxes, ''] } : r)) }));
  }

  removeBoardBox(row: number, box: number): void {
    this.updateMazeBoard((f) => ({
      ...f,
      rows: f.rows.map((r, i) => {
        if (i !== row) return r;
        const correct = r.correct === box ? 0 : r.correct > box ? r.correct - 1 : r.correct;
        return { ...r, boxes: r.boxes.filter((_, j) => j !== box), correct };
      }),
    }));
  }

  addBoardRow(): void {
    this.updateMazeBoard((f) => ({ ...f, rows: [...f.rows, { label: '', boxes: ['', '', ''], correct: 0 }] }));
  }

  removeBoardRow(index: number): void {
    this.updateMazeBoard((f) => ({ ...f, rows: f.rows.filter((_, i) => i !== index) }));
  }

  // ---- Odalı labirent ----

  setRoomsSize(rows: number, cols: number): void {
    this.updateMazeRooms((f) => resizeRooms(f, rows, cols));
  }

  setRoomsStart(index: number): void {
    this.updateMazeRooms((f) => ({ ...f, start: index }));
  }

  setRoomsFinish(index: number): void {
    this.updateMazeRooms((f) => ({ ...f, finish: index }));
  }

  setRoomText(index: number, value: string): void {
    this.updateMazeRooms((f) => ({ ...f, rooms: f.rooms.map((r, i) => (i === index ? { ...r, text: value } : r)) }));
  }

  setRoomAnswer(index: number, door: number | null): void {
    this.updateMazeRooms((f) => ({ ...f, rooms: f.rooms.map((r, i) => (i === index ? { ...r, answer: door } : r)) }));
  }

  setDoorLabel(index: number, value: string): void {
    this.updateMazeRooms((f) => ({ ...f, doors: f.doors.map((d, i) => (i === index ? { ...d, label: value } : d)) }));
  }

  /** Oda adı: satır harfi + kolon numarası (A1, B3). */
  roomName(index: number): string {
    const cols = this.mazeRooms()?.cols ?? 1;
    return `${String.fromCharCode(65 + Math.floor(index / cols))}${(index % cols) + 1}`;
  }

  doorName(index: number): string {
    const door = this.mazeRooms()?.doors[index];
    return door ? `${this.roomName(door.a)} ↔ ${this.roomName(door.b)}` : '';
  }

  /** Odanın doğru kapı seçenekleri: "<etiket> → <öbür oda>". */
  roomDoorOptions(room: number): { index: number; label: string }[] {
    const f = this.mazeRooms();
    if (!f) return [];
    return roomDoors(f, room).map((i) => ({
      index: i,
      label: `${f.doors[i].label.trim()} → ${this.roomName(otherRoom(f.doors[i], room))}`,
    }));
  }

  // ---- Irmak taşları ----

  setStoneText(index: number, value: string): void {
    this.updateMazeStones((f) => ({ ...f, stones: f.stones.map((s, i) => (i === index ? { ...s, text: value } : s)) }));
  }

  toggleStoneMarked(index: number): void {
    this.updateMazeStones((f) => ({ ...f, stones: f.stones.map((s, i) => (i === index ? { ...s, marked: !s.marked } : s)) }));
  }

  addStone(): void {
    this.updateMazeStones((f) => ({ ...f, stones: [...f.stones, { text: '', marked: false }] }));
  }

  removeStone(index: number): void {
    this.updateMazeStones((f) => ({ ...f, stones: f.stones.filter((_, i) => i !== index) }));
  }

  // ---- Çengel bulmaca ----

  setCrosswordSize(rows: number, cols: number): void {
    this.updateCrossword((f) => ({ ...f, rows, cols }));
  }

  setCrosswordEntry(index: number, patch: Partial<CrosswordEntryForm>): void {
    this.crosswordUnplaced.set(null);
    this.updateCrossword((f) => ({ ...f, entries: f.entries.map((e, i) => (i === index ? { ...e, ...patch } : e)) }));
  }

  /** Satır / sütun alanı 1 tabanlı gösterilir. */
  setCrosswordPosition(index: number, field: 'row' | 'col', value: string): void {
    const n = Number.parseInt(value, 10);
    if (Number.isFinite(n)) this.setCrosswordEntry(index, { [field]: n - 1 });
  }

  addCrosswordEntry(): void {
    this.updateCrossword((f) => ({ ...f, entries: [...f.entries, { answer: '', clue: '', dir: 'across', row: 0, col: 0 }] }));
  }

  removeCrosswordEntry(index: number): void {
    this.updateCrossword((f) => ({ ...f, entries: f.entries.filter((_, i) => i !== index) }));
  }

  autoLayoutCrossword(): void {
    const f = this.crossword();
    if (!f) return;
    const result = autoLayout(f);
    this.form.set(result.form);
    this.crosswordUnplaced.set(result.unplaced);
  }

  crosswordLength(entry: CrosswordEntryForm): number {
    return entryLength(entry);
  }

  /** Cevap görselden çıkarılan kelime yerinden farklı uzunlukta (ızgara cevaba göre değişir). */
  crosswordLengthMismatch(entry: CrosswordEntryForm): boolean {
    const letters = answerLetters(entry.answer).length;
    return !!entry.length && letters > 0 && letters !== entry.length;
  }

  /** Yeni seçilen görsel var (kayıtlı görselin verisi istemcide yok; Claude'a yalnız yeni seçim gönderilir). */
  readonly canExtractCrossword = computed(
    () => !!this.aiModel() && !!this.image()?.dataUrl && !this.extracting() && this.kind() === 'crossword'
  );

  /**
   * Görseldeki bulmacanın ızgarası, kelime yerleri ve ipuçları Claude'dan istenir ve forma yüklenir; cevapları öğretmen yazar.
   * Formda yazılmış içerik varsa önce onay istenir.
   */
  extractCrossword(): void {
    const f = this.crossword();
    if (!f || !this.canExtractCrossword()) return;
    const filled = f.entries.some((e) => e.answer.trim() || e.clue.trim());
    if (!filled) {
      this.runCrosswordExtraction();
      return;
    }
    this.snackBar
      .open(this.t('crossword.extractConfirm'), this.t('crossword.extractConfirmAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => this.runCrosswordExtraction());
  }

  private runCrosswordExtraction(): void {
    const image = this.image();
    const model = this.aiModel();
    if (!image?.dataUrl || !model || this.extracting() || this.kind() !== 'crossword') return;
    this.extracting.set(true);
    this.extractError.set(null);
    this.labelingApi
      .activityStructure({
        imageBase64: image.dataUrl.slice(image.dataUrl.indexOf(',') + 1),
        model: model.id,
        force: false,
        blocks: [{ key: 'crossword', x: 0, y: 0, width: image.width, height: image.height, interactionType: CROSSWORD }],
      })
      .subscribe({
        next: (response) => {
          this.extracting.set(false);
          const result = response.results[0];
          if (!result || result.status !== 'ok') {
            this.extractError.set(result?.error || this.t('crossword.extractFailed'));
            return;
          }
          const form = this.kind() === 'crossword' ? crosswordFormFromDraft(JSON.stringify(result.plan)) : null;
          if (!form) {
            this.extractError.set(this.t('crossword.extractEmpty'));
            return;
          }
          this.form.set(form);
          this.crosswordUnplaced.set(null);
          if (result.instruction && !this.instruction().trim()) this.instruction.set(result.instruction);
          this.snackBar.open(this.t('crossword.extracted', { n: form.entries.length }), undefined, { duration: 4000 });
        },
        error: (error: HttpErrorResponse) => {
          this.extracting.set(false);
          const serverMessage = (error.error as { message?: string } | null)?.message;
          this.extractError.set(serverMessage || this.t(error.status === 503 ? 'crossword.extractUnavailable' : 'crossword.extractFailed'));
        },
      });
  }

  private loadAiModel(): void {
    if (!this.auth.hasRealmRole('Admin') && !this.auth.hasRealmRole('QuestionTrainer')) return;
    this.labelingApi.getAiModels().subscribe({
      next: (models) => this.aiModel.set(models?.find((m) => m.isDefault) ?? models?.[0] ?? null),
      error: () => this.aiModel.set(null),
    });
  }

  // ---- Sözel mantık tablosu ----

  setLogicSubjectLabel(value: string): void {
    this.updateLogicGrid((f) => ({ ...f, subjectLabel: value }));
  }

  setLogicSubject(index: number, value: string): void {
    this.updateLogicGrid((f) => ({ ...f, subjects: f.subjects.map((s, i) => (i === index ? value : s)) }));
  }

  /** Konu öğesi ekler / son öğeyi kaldırır; seçenek listeleri aynı uzunluğa gelir. */
  resizeLogicSubjects(delta: 1 | -1): void {
    this.updateLogicGrid((f) => resizeLogicSubjects(f, f.subjects.length + delta));
  }

  setLogicCategoryLabel(index: number, value: string): void {
    this.updateLogicGrid((f) => ({ ...f, categories: f.categories.map((c, i) => (i === index ? { ...c, label: value } : c)) }));
  }

  setLogicOption(category: number, option: number, value: string): void {
    this.updateLogicGrid((f) => ({
      ...f,
      categories: f.categories.map((c, i) =>
        i === category ? { ...c, options: c.options.map((o, j) => (j === option ? value : o)) } : c
      ),
    }));
  }

  addLogicCategory(): void {
    this.updateLogicGrid((f) => addLogicCategory(f));
  }

  removeLogicCategory(index: number): void {
    this.updateLogicGrid((f) => removeLogicCategory(f, index));
  }

  setLogicAnswer(subject: number, category: number, option: number | null): void {
    this.updateLogicGrid((f) => ({
      ...f,
      answer: f.answer.map((row, i) => (i === subject ? row.map((c, j) => (j === category ? option : c)) : row)),
    }));
  }

  setLogicClue(index: number, value: string): void {
    this.updateLogicGrid((f) => ({ ...f, clues: f.clues.map((c, i) => (i === index ? value : c)) }));
  }

  addLogicClue(): void {
    this.updateLogicGrid((f) => ({ ...f, clues: [...f.clues, ''] }));
  }

  removeLogicClue(index: number): void {
    this.updateLogicGrid((f) => ({ ...f, clues: f.clues.length > 1 ? f.clues.filter((_, i) => i !== index) : [''] }));
  }

  // ---- Boyama ----

  paintCss(color: PaintColor): string {
    return paintCss(color);
  }

  paintColorUsed(color: PaintColor): boolean {
    return !!this.regionPaint()?.entries.some((e) => e.color === color);
  }

  canAddPaintColor(): boolean {
    const f = this.regionPaint();
    return !!f && nextPaintColor(f) !== null;
  }

  /** Hedefin kare karşılığı (`{value}`; boş hedef 0); çözülemezse null. */
  paintCells(target: string, form: RegionPaintForm): { value: number } | null {
    const value = targetCells(target, regionPaintTotal(form));
    return value === null ? null : { value };
  }

  setPaintShape(shape: RegionPaintShape): void {
    this.updateRegionPaint((f) => ({ ...f, shape }));
  }

  setPaintSize(axis: 'rows' | 'cols' | 'slices', value: number): void {
    if (!Number.isFinite(value)) return;
    const n = Math.round(value);
    this.updateRegionPaint((f) => ({ ...f, [axis]: n }));
  }

  setPaintEntry(index: number, patch: Partial<RegionPaintEntryForm>): void {
    this.updateRegionPaint((f) => ({ ...f, entries: f.entries.map((e, i) => (i === index ? { ...e, ...patch } : e)) }));
  }

  addPaintEntry(): void {
    this.updateRegionPaint((f) => {
      const color = nextPaintColor(f);
      return color && f.entries.length < REGION_PAINT_FORM_LIMITS.maxPalette
        ? { ...f, entries: [...f.entries, { color, label: '', target: '' }] }
        : f;
    });
  }

  removePaintEntry(index: number): void {
    this.updateRegionPaint((f) => (f.entries.length > 1 ? { ...f, entries: f.entries.filter((_, i) => i !== index) } : f));
  }

  // ---- Sayı doğrusu ----

  /** Kartın hedef çentiğinin adı (etiketi, yoksa "3/8"); boş hedef çeldirici, geçersiz null. */
  lineTargetName(target: string, form: NumberLineForm): { value: string | null } | null {
    const at = targetTick(target, form.divisions);
    if (at === undefined) return null;
    if (at === null) return { value: null };
    return { value: form.labels.find((l) => l.at === at)?.text.trim() || `${at}/${form.divisions}` };
  }

  setLineOption(patch: Partial<Pick<NumberLineForm, 'divisions' | 'tolerance' | 'hideTicks'>>): void {
    for (const key of ['divisions', 'tolerance'] as const) {
      const value = patch[key];
      if (value !== undefined && !Number.isFinite(value)) return;
      if (value !== undefined) patch[key] = Math.round(value);
    }
    this.updateNumberLine((f) => ({ ...f, ...patch }));
  }

  setLineLabel(index: number, patch: Partial<NumberLineForm['labels'][number]>): void {
    if (patch.at !== undefined && !Number.isFinite(patch.at)) return;
    this.updateNumberLine((f) => ({ ...f, labels: f.labels.map((l, i) => (i === index ? { ...l, ...patch } : l)) }));
  }

  /** Yeni etiket: etiketsiz ilk çentik. */
  addLineLabel(): void {
    this.updateNumberLine((f) => {
      const used = new Set(f.labels.map((l) => l.at));
      const at = Array.from({ length: f.divisions + 1 }, (_, i) => i).find((i) => !used.has(i));
      return at === undefined ? f : { ...f, labels: [...f.labels, { at, text: '' }].sort((a, b) => a.at - b.at) };
    });
  }

  removeLineLabel(index: number): void {
    this.updateNumberLine((f) => ({ ...f, labels: f.labels.filter((_, i) => i !== index) }));
  }

  setLineCard(index: number, patch: Partial<NumberLineCardForm>): void {
    this.updateNumberLine((f) => ({ ...f, cards: f.cards.map((c, i) => (i === index ? { ...c, ...patch } : c)) }));
  }

  addLineCard(): void {
    this.updateNumberLine((f) =>
      f.cards.length < NUMBER_LINE_FORM_LIMITS.maxCards ? { ...f, cards: [...f.cards, { label: '', target: '' }] } : f
    );
  }

  removeLineCard(index: number): void {
    this.updateNumberLine((f) => (f.cards.length > 1 ? { ...f, cards: f.cards.filter((_, i) => i !== index) } : f));
  }

  // ---- Grafik kurma ----

  setChartOption(patch: Partial<Pick<BarChartForm, 'style' | 'title' | 'xLabel' | 'yLabel'>>): void {
    this.updateBarChart((f) => ({ ...f, ...patch }));
  }

  setChartAxis(patch: Partial<Pick<BarChartForm, 'max' | 'step' | 'unit'>>): void {
    for (const key of ['max', 'step', 'unit'] as const) {
      const value = patch[key];
      if (value !== undefined && !Number.isFinite(value)) return;
      if (value !== undefined) patch[key] = Math.round(value);
    }
    this.updateBarChart((f) => ({ ...f, ...patch }));
  }

  setChartCategory(index: number, patch: Partial<BarChartCategoryForm>): void {
    this.updateBarChart((f) => ({ ...f, categories: f.categories.map((c, i) => (i === index ? { ...c, ...patch } : c)) }));
  }

  addChartCategory(): void {
    this.updateBarChart((f) =>
      f.categories.length < BAR_CHART_FORM_LIMITS.maxCategories
        ? { ...f, categories: [...f.categories, { label: '', value: '', fixed: false }] }
        : f
    );
  }

  removeChartCategory(index: number): void {
    this.updateBarChart((f) =>
      f.categories.length > BAR_CHART_FORM_LIMITS.minCategories ? { ...f, categories: f.categories.filter((_, i) => i !== index) } : f
    );
  }

  // ---- Kareli zeminde çizim ----

  setGridDraw(patch: Partial<Omit<GridDrawForm, 'kind'>>): void {
    for (const key of ['rows', 'cols'] as const) {
      const value = patch[key];
      if (value !== undefined && !Number.isFinite(value)) return;
      if (value !== undefined) patch[key] = Math.round(value);
    }
    const f = this.gridDraw();
    if (f) this.form.set({ ...f, ...patch });
  }

  // ---- Terazi ----

  setBalanceOption(patch: Partial<Pick<BalanceScaleForm, 'unit' | 'useAll' | 'hideTilt'>>): void {
    this.updateBalanceScale((f) => ({ ...f, ...patch }));
  }

  setBalanceWeight(list: 'cards' | 'left' | 'right', index: number, patch: Partial<BalanceWeightForm>): void {
    this.updateBalanceScale((f) => ({ ...f, [list]: f[list].map((w, i) => (i === index ? { ...w, ...patch } : w)) }));
  }

  addBalanceWeight(list: 'cards' | 'left' | 'right'): void {
    const max = list === 'cards' ? BALANCE_SCALE_FORM_LIMITS.maxCards : BALANCE_SCALE_FORM_LIMITS.maxFixedPerPan;
    this.updateBalanceScale((f) => (f[list].length < max ? { ...f, [list]: [...f[list], { value: '', label: '' }] } : f));
  }

  removeBalanceWeight(list: 'cards' | 'left' | 'right', index: number): void {
    this.updateBalanceScale((f) =>
      list === 'cards' && f.cards.length <= 1 ? f : { ...f, [list]: f[list].filter((_, i) => i !== index) }
    );
  }

  // ---- Kelime avı ----

  setWordSearchOption(patch: Partial<Pick<WordSearchForm, 'rows' | 'cols' | 'diagonal' | 'reverse'>>): void {
    this.updateWordSearch((f) => ({ ...f, ...patch }));
  }

  setWordSearchEntry(index: number, patch: Partial<WordSearchEntryForm>): void {
    this.updateWordSearch((f) => ({ ...f, words: f.words.map((w, i) => (i === index ? { ...w, ...patch } : w)) }));
  }

  addWordSearchEntry(): void {
    this.updateWordSearch((f) => ({ ...f, words: [...f.words, { word: '', label: '' }] }));
  }

  removeWordSearchEntry(index: number): void {
    this.updateWordSearch((f) => ({ ...f, words: f.words.filter((_, i) => i !== index) }));
  }

  /** Yeni tohumla yeniden yerleştirir (kayıtlı ızgara da bırakılır). */
  shuffleWordSearch(): void {
    this.updateWordSearch((f) => ({ ...f, seed: f.seed + 1, fixed: null }));
  }

  wordSearchLength(entry: WordSearchEntryForm): number {
    return wordLetters(entry.word).length;
  }

  // ---- Boşluk doldurma / kelime havuzu (metin) ----

  setBlankText(value: string): void {
    const f = this.form();
    if (f.kind === 'fillText' || f.kind === 'wordPool') this.form.set({ ...f, text: value });
  }

  /** Seçili metni `[[...]]` boşluğuna çevirir (seçim yoksa imlece boş `[[]]` koyar); odak ve imleç boşluğun sonuna. */
  makeBlank(textarea: HTMLTextAreaElement): void {
    const value = textarea.value;
    const start = textarea.selectionStart ?? value.length;
    const end = textarea.selectionEnd ?? value.length;
    const selected = value.slice(start, end);
    // Seçimin kenarındaki boşluklar boşluğa girmesin ("kelime " → "[[kelime]] ").
    const lead = selected.length - selected.trimStart().length;
    const trail = selected.length - selected.trimEnd().length;
    const inner = selected.trim();
    const next = `${value.slice(0, start + lead)}[[${inner}]]${value.slice(end - trail)}`;
    this.setBlankText(next);
    const caret = start + lead + inner.length + (inner ? 4 : 2);
    textarea.value = next;
    textarea.focus();
    textarea.setSelectionRange(caret, caret);
  }

  setPoolDistractor(index: number, value: string): void {
    this.updateWordPool((f) => ({ ...f, distractors: f.distractors.map((d, i) => (i === index ? value : d)) }));
  }

  addPoolDistractor(): void {
    this.updateWordPool((f) => ({ ...f, distractors: [...f.distractors, ''] }));
  }

  removePoolDistractor(index: number): void {
    this.updateWordPool((f) => ({ ...f, distractors: f.distractors.filter((_, i) => i !== index) }));
  }

  // ---- Soru + kısa cevap ----

  setShortRow(index: number, field: 'question' | 'answers' | 'letter', value: string): void {
    this.updateShortAnswer((f) => ({ ...f, rows: f.rows.map((r, i) => (i === index ? { ...r, [field]: value } : r)) }));
  }

  addShortRow(): void {
    this.updateShortAnswer((f) => ({ ...f, rows: [...f.rows, { question: '', answers: '' }] }));
  }

  /** Issue #586: gizli kelime (boş = şifre yok). */
  setShortCipher(value: string): void {
    this.updateShortAnswer((f) => ({ ...f, cipher: value }));
  }

  removeShortRow(index: number): void {
    this.updateShortAnswer((f) => ({ ...f, rows: f.rows.filter((_, i) => i !== index) }));
  }

  // ---- Tablo ----

  setTableHeader(value: boolean): void {
    this.updateFillTable((f) => ({ ...f, hasHeader: value }));
    if (value) {
      this.tableSelection.update((sel) => new Set([...sel].filter((key) => !key.startsWith('0:'))));
      if (this.tableFocus()?.row === 0) this.tableFocus.set(null);
    }
  }

  isHeaderCell(row: number): boolean {
    return !!this.fillTable()?.hasHeader && row === 0;
  }

  isCellSelected(row: number, column: number): boolean {
    return this.tableSelection().has(`${row}:${column}`);
  }

  toggleTableSelectMode(): void {
    this.tableSelectMode.update((on) => !on);
  }

  clearTableSelection(): void {
    this.tableSelection.set(new Set());
  }

  onCellFocus(row: number, column: number): void {
    this.tableFocus.set(this.isHeaderCell(row) ? null : { row, column });
    // Yazma kipinde hücreye girmek çoklu seçimi bırakır: araçlar artık bu hücreye (ve içindeki seçili metne) uygulanır.
    if (!this.tableSelectMode()) this.tableSelection.set(new Set());
  }

  /** Seçme kipinde hücreye basış: hücreyi seçer / seçimden çıkarır; basılı sürükleme aynı işlemi dikdörtgene yayar. */
  onCellPointerDown(row: number, column: number, event: PointerEvent): void {
    if (!this.tableSelectMode() || this.isHeaderCell(row)) return;
    event.preventDefault();
    // Dokunmatikte işaretçi ilk öğeye bağlanır; bırakınca sürükleme diğer hücrelerin pointerenter'ını alır.
    const target = event.target as Element;
    if (target.hasPointerCapture?.(event.pointerId)) target.releasePointerCapture(event.pointerId);
    const base = this.tableSelection();
    this.tableDrag = { row, column, adding: !base.has(`${row}:${column}`), base };
    this.applyTableDrag(row, column);
  }

  onCellPointerEnter(row: number, column: number): void {
    if (this.tableDrag) this.applyTableDrag(row, column);
  }

  endTableDrag(): void {
    this.tableDrag = null;
  }

  /** Seçme kipinde klavye: Boşluk / Enter odaktaki hücreyi seçer / seçimden çıkarır. */
  onCellKeydown(row: number, column: number, event: KeyboardEvent): void {
    if (!this.tableSelectMode() || this.isHeaderCell(row) || (event.key !== ' ' && event.key !== 'Enter')) return;
    event.preventDefault();
    const key = `${row}:${column}`;
    this.tableSelection.update((sel) => {
      const next = new Set(sel);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  }

  /**
   * Seçili hücreleri boşluk yapar (hücrenin tamamı cevap, zaten boşluklu hücreye dokunmaz). Seçim yoksa odaktaki hücreye:
   * hücrede seçili metin varsa yalnız o kısım boşluk olur ("5 cm" içinde "5" → `[[5]] cm`).
   */
  makeTableBlanks(): void {
    const targets = this.tableTargets();
    if (targets.length === 0) return;
    const focus = this.tableFocus();
    const input = focus && this.tableSelection().size === 0 ? this.cellInput(focus.row, focus.column) : null;
    if (input && focus && (input.selectionStart ?? 0) < (input.selectionEnd ?? 0) && !cellHasBlank(input.value)) {
      const value = input.value;
      const start = input.selectionStart!;
      const end = input.selectionEnd!;
      const selected = value.slice(start, end);
      const lead = selected.length - selected.trimStart().length;
      const trail = selected.length - selected.trimEnd().length;
      this.setCell(focus.row, focus.column, `${value.slice(0, start + lead)}[[${selected.trim()}]]${value.slice(end - trail)}`);
      return;
    }
    this.mapTableCells(targets, cellToBlank);
  }

  /** Seçili (ya da odaktaki) hücrelerin boşluklarını düz metne çevirir; alternatifli boşlukta ilk cevap kalır. */
  removeTableBlanks(): void {
    this.mapTableCells(this.tableTargets(), cellFromBlank);
  }

  /** Seçili hücrelerden en az biri boşluklu mu (seçim yoksa odaktaki hücre). */
  readonly tableTargetsHaveBlank = computed(() => {
    const f = this.fillTable();
    return !!f && this.tableTargets().some(({ row, column }) => cellHasBlank(f.cells[row]?.[column] ?? ''));
  });

  readonly tableHasTarget = computed(() => this.tableTargets().length > 0);

  private tableTargets(): { row: number; column: number }[] {
    const f = this.fillTable();
    if (!f) return [];
    const selection = this.tableSelection();
    const cells =
      selection.size > 0
        ? [...selection].map((key) => {
            const [row, column] = key.split(':').map(Number);
            return { row, column };
          })
        : this.tableFocus()
          ? [this.tableFocus()!]
          : [];
    return cells.filter(({ row, column }) => row < f.cells.length && column < f.cells[0].length && !this.isHeaderCell(row));
  }

  private mapTableCells(targets: { row: number; column: number }[], map: (cell: string) => string): void {
    const keys = new Set(targets.map(({ row, column }) => `${row}:${column}`));
    this.updateFillTable((f) => ({
      ...f,
      cells: f.cells.map((r, ri) => r.map((c, ci) => (keys.has(`${ri}:${ci}`) ? map(c) : c))),
    }));
  }

  private applyTableDrag(row: number, column: number): void {
    const drag = this.tableDrag;
    if (!drag) return;
    const next = new Set(drag.base);
    for (let r = Math.min(drag.row, row); r <= Math.max(drag.row, row); r++) {
      if (this.isHeaderCell(r)) continue;
      for (let c = Math.min(drag.column, column); c <= Math.max(drag.column, column); c++) {
        if (drag.adding) next.add(`${r}:${c}`);
        else next.delete(`${r}:${c}`);
      }
    }
    this.tableSelection.set(next);
  }

  private cellInput(row: number, column: number): HTMLInputElement | null {
    return document.querySelector<HTMLInputElement>(`[data-testid="fill-table-form"] [data-cell="${row}:${column}"]`);
  }

  /** Satır / kolon değişince indeksler kayar: seçim ve odak sıfırlanır. */
  private resetTableSelection(): void {
    this.tableSelection.set(new Set());
    this.tableFocus.set(null);
  }

  setCell(row: number, column: number, value: string): void {
    this.updateFillTable((f) => ({
      ...f,
      cells: f.cells.map((r, ri) => (ri === row ? r.map((c, ci) => (ci === column ? value : c)) : r)),
    }));
  }

  addTableRow(): void {
    this.updateFillTable((f) => ({ ...f, cells: [...f.cells, f.cells[0].map(() => '')] }));
    this.resetTableSelection();
  }

  removeTableRow(row: number): void {
    this.updateFillTable((f) => ({ ...f, cells: f.cells.filter((_, i) => i !== row) }));
    this.resetTableSelection();
  }

  addTableColumn(): void {
    this.updateFillTable((f) => ({ ...f, cells: f.cells.map((r) => [...r, '']) }));
    this.resetTableSelection();
  }

  removeTableColumn(column: number): void {
    this.updateFillTable((f) => ({ ...f, cells: f.cells.map((r) => r.filter((_, i) => i !== column)) }));
    this.resetTableSelection();
  }

  // ---- Madde listesi, kayıt ----

  edit(item: EditableItem): void {
    void this.router.navigate([], {
      queryParams: { questionId: item.id },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.load();
  }

  newItem(): void {
    void this.router.navigate([], {
      queryParams: { questionId: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.resetForm(this.kind());
  }

  backToActivity(): void {
    void this.router.navigate(this.testId ? ['/test', this.testId] : ['/tests']);
  }

  errorText(error: AuthoringError): string {
    return this.t(`errors.${error.key}`, error.params);
  }

  save(addAnother: boolean): void {
    this.submitted.set(true);
    this.saveError.set(null);
    if (!this.canSave() || !this.testId) return;
    this.saving.set(true);
    const editingId = this.editingId();
    const request$ = editingId ? this.questionService.saveQuestion(this.updateBody(editingId)) : this.questionService.saveBulk(this.createBody());
    request$.subscribe({
      next: () => {
        if (editingId) this.saveClassificationIfChanged(editingId);
        this.saving.set(false);
        this.snackBar.open(this.t(editingId ? 'snackbar.updated' : 'snackbar.created'), undefined, { duration: 3000 });
        if (addAnother) {
          this.newItem();
          this.load();
        } else {
          this.backToActivity();
        }
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.saveError.set(
          saveValidationMessage(err) ?? (err?.error?.message as string | undefined) ?? this.t('snackbar.saveError')
        );
      },
    });
  }

  private createBody(): unknown {
    const def = this.definition();
    const image = this.image();
    const selection = this.classification();
    const locked = !!selection?.topicId;
    return {
      imageData: image?.dataUrl ?? null,
      header: { testId: this.testId, classificationSource: 'Human' },
      passages: [
        { id: 'p1', x: 0, y: 0, width: 0, height: 0, kind: PassageKind.Activity, instruction: this.instructionValue() },
      ],
      questions: [
        {
          name: 'S1',
          x: 0,
          y: 0,
          width: image?.dataUrl ? image.width : 0,
          height: image?.dataUrl ? image.height : 0,
          interactionType: def.interactionType,
          interactionPlan: def.plan,
          interactionSolution: def.solution,
          passageId: 'p1',
          answers: [],
          classification: locked
            ? {
                subjectId: selection!.subjectId,
                topicId: selection!.topicId,
                subTopicIds: selection!.subtopicIds,
                source: 'Human',
                locked: true,
              }
            : null,
        },
      ],
    };
  }

  private updateBody(questionId: number): unknown {
    const def = this.definition();
    return {
      id: questionId,
      testId: this.testId,
      text: null,
      interactionType: def.interactionType,
      interactionPlan: def.plan,
      interactionSolution: def.solution,
      image: this.image()?.dataUrl ?? null,
      answers: [],
      passage: {
        id: this.editingPassageId,
        kind: PassageKind.Activity,
        instruction: this.instructionValue() ?? '',
      },
    };
  }

  /** Düzenlemede taksonomi tekli kayıtla değişmez; seçim değiştiyse ayrı uçla (kaynak Human) yazılır. */
  private saveClassificationIfChanged(questionId: number): void {
    const selection = this.classification();
    if (!selection?.topicId || classificationKey(selection) === this.initialClassificationKey) return;
    this.questionService
      .updateClassification(questionId, selection.subjectId, selection.topicId, null, selection.subtopicIds)
      .subscribe({ error: () => this.snackBar.open(this.t('snackbar.classificationError'), undefined, { duration: 4000 }) });
  }

  private startEdit(question: Question): void {
    const form = parseDefinition(question.interactionType, question.interactionPlan, question.interactionSolution);
    if (!form) return;
    this.editingId.set(question.id);
    this.editingPassageId = question.passage?.id ?? null;
    this.form.set(form);
    this.resetTableSelection();
    this.instruction.set(question.passage?.instruction ?? '');
    this.originalImage = question.imageUrl ? { dataUrl: null, src: question.imageUrl, width: 0, height: 0 } : null;
    this.image.set(this.originalImage);
    const selection: ClassificationSelection | null = question.topicId
      ? {
          gradeId: this.gradeId,
          subjectId: question.subjectId ?? null,
          topicId: question.topicId,
          subtopicIds: (question.subTopics ?? []).map((s) => s.id),
        }
      : this.initialClassification();
    this.initialClassification.set(selection);
    this.classification.set(selection);
    this.initialClassificationKey = selection ? classificationKey(selection) : '';
    this.submitted.set(false);
    this.showSolution.set(false);
  }

  private resetForm(kind: AuthoringKind): void {
    this.editingId.set(null);
    this.editingPassageId = null;
    this.originalImage = null;
    this.form.set(emptyForm(kind, this.trueFalseLabels(), this.mazeDefaults()));
    this.crosswordUnplaced.set(null);
    this.resetTableSelection();
    this.instruction.set('');
    this.image.set(null);
    this.imageError.set(null);
    this.saveError.set(null);
    this.submitted.set(false);
    this.showSolution.set(false);
    this.initialClassificationKey = '';
  }

  private editableItems(questions: Question[]): EditableItem[] {
    return questions.flatMap((q, index) => {
      const form = parseDefinition(q.interactionType, q.interactionPlan, q.interactionSolution);
      if (!form) return [];
      return [{ id: q.id, order: index + 1, kind: form.kind, title: itemTitle(form, q.passage?.instruction) }];
    });
  }

  private instructionValue(): string | null {
    return this.instruction().trim() || null;
  }

  private updateMatrix(update: (f: MatrixForm) => MatrixForm): void {
    const f = this.matrix();
    if (f) this.form.set(update(f));
  }

  private updateClassify(update: (f: ClassifyForm) => ClassifyForm): void {
    const f = this.classify();
    if (f) this.form.set(update(f));
  }

  private updateOrder(update: (f: OrderForm) => OrderForm): void {
    const f = this.order();
    if (f) this.form.set(update(f));
  }

  private updateMatch(update: (f: MatchForm) => MatchForm): void {
    const f = this.match();
    if (f) this.form.set(update(f));
  }

  private updateShortAnswer(update: (f: ShortAnswerForm) => ShortAnswerForm): void {
    const f = this.shortAnswer();
    if (f) this.form.set(update(f));
  }

  private updateFillTable(update: (f: FillTableForm) => FillTableForm): void {
    const f = this.fillTable();
    if (f) this.form.set(update(f));
  }

  private updateWordPool(update: (f: WordPoolForm) => WordPoolForm): void {
    const f = this.wordPool();
    if (f) this.form.set(update(f));
  }

  private updateMazeTree(update: (f: MazeTreeForm) => MazeTreeForm): void {
    const f = this.mazeTree();
    if (f) this.form.set(update(f));
  }

  private updateMazeBoard(update: (f: MazeBoardForm) => MazeBoardForm): void {
    const f = this.mazeBoard();
    if (f) this.form.set(update(f));
  }

  private updateMazeRooms(update: (f: MazeRoomsForm) => MazeRoomsForm): void {
    const f = this.mazeRooms();
    if (f) this.form.set(update(f));
  }

  private updateRegionPaint(update: (f: RegionPaintForm) => RegionPaintForm): void {
    const f = this.regionPaint();
    if (f) this.form.set(update(f));
  }

  private updateBalanceScale(update: (f: BalanceScaleForm) => BalanceScaleForm): void {
    const f = this.balanceScale();
    if (f) this.form.set(update(f));
  }

  // ---- Saat kurma ----

  setClockOption(patch: Partial<Pick<ClockSetForm, 'step' | 'numbers' | 'minuteTicks'>>): void {
    this.updateClockSet((f) => ({ ...f, ...patch }));
  }

  setClock(index: number, patch: Partial<ClockForm>): void {
    this.updateClockSet((f) => ({ ...f, clocks: f.clocks.map((c, i) => (i === index ? { ...c, ...patch } : c)) }));
  }

  addClock(): void {
    this.updateClockSet((f) =>
      f.clocks.length < CLOCK_SET_FORM_LIMITS.maxClocks ? { ...f, clocks: [...f.clocks, { label: '', time: '', fixed: false }] } : f
    );
  }

  removeClock(index: number): void {
    this.updateClockSet((f) => (f.clocks.length <= 1 ? f : { ...f, clocks: f.clocks.filter((_, i) => i !== index) }));
  }

  private updateClockSet(update: (f: ClockSetForm) => ClockSetForm): void {
    const f = this.clockSet();
    if (f) this.form.set(update(f));
  }

  private updateNumberLine(update: (f: NumberLineForm) => NumberLineForm): void {
    const f = this.numberLine();
    if (f) this.form.set(update(f));
  }

  private updateBarChart(update: (f: BarChartForm) => BarChartForm): void {
    const f = this.barChart();
    if (f) this.form.set(update(f));
  }

  private updateLogicGrid(update: (f: LogicGridForm) => LogicGridForm): void {
    const f = this.logicGrid();
    if (f) this.form.set(update(f));
  }

  private updateWordSearch(update: (f: WordSearchForm) => WordSearchForm): void {
    const f = this.wordSearch();
    if (f) this.form.set(update(f));
  }

  private updateCrossword(update: (f: CrosswordForm) => CrosswordForm): void {
    const f = this.crossword();
    if (f) this.form.set(update(f));
  }

  private updateMazeStones(update: (f: MazeStonesForm) => MazeStonesForm): void {
    const f = this.mazeStones();
    if (f) this.form.set(update(f));
  }

  /** Yol oyunu şablonlarının varsayılanları (kök çeviriden): dal etiketleri D / Y, çıkış adı "n. çıkış". */
  private mazeDefaults(): MazeDefaults {
    return {
      branches: [
        this.transloco.translate<string>('shared.questionCanvas.maze.branchTrue'),
        this.transloco.translate<string>('shared.questionCanvas.maze.branchFalse'),
      ],
      exitName: (n) => this.transloco.translate<string>('shared.questionCanvas.maze.exitName', { n }),
    };
  }

  /** Kök çeviriden (kapsam yüklenmeden de hazır): yeni form alan başlatılırken çağrılır. */
  private trueFalseLabels(): [string, string] {
    return [
      this.transloco.translate<string>('shared.questionCanvas.matrix.presetTrue'),
      this.transloco.translate<string>('shared.questionCanvas.matrix.presetFalse'),
    ];
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ACTIVITY_EDITOR_SCOPE}.${key}`, params) ?? key;
  }
}

function classificationKey(selection: ClassificationSelection): string {
  return [selection.subjectId ?? '', selection.topicId ?? '', [...selection.subtopicIds].sort((a, b) => a - b).join(',')].join('|');
}

/** Listede maddenin adı: yönerge, yoksa ilk öğe metni. */
function itemTitle(form: AuthoringForm, instruction: string | null | undefined): string {
  const own = instruction?.trim();
  if (own) return own;
  switch (form.kind) {
    case 'matrix':
      return form.rows[0]?.text ?? '';
    case 'classify':
      return form.buckets.join(' / ');
    case 'order':
      return form.items[0] ?? '';
    case 'match':
      return form.pairs[0]?.left ?? '';
    case 'fillText':
    case 'wordPool':
      return form.text.replace(/\[\[[^[\]]*\]\]/g, '___').slice(0, 80);
    case 'shortAnswer':
      return form.rows[0]?.question ?? '';
    case 'fillTable':
      return form.cells[0]?.join(' / ') ?? '';
    case 'mazeTree':
      return form.nodes[0]?.text ?? '';
    case 'mazeBoard':
      return form.rows.map((r) => r.label).filter(Boolean).join(' / ');
    case 'mazeRooms':
      return form.rooms[form.start]?.text ?? '';
    case 'mazeStones':
      return form.stones[0]?.text ?? '';
    case 'crossword':
      return form.entries[0]?.clue ?? '';
    case 'regionPaint':
      return form.entries.map((e) => e.label || e.target).filter(Boolean).join(', ');
    case 'numberLine':
      return form.cards.map((c) => c.label).filter(Boolean).join(', ').slice(0, 80);
    case 'barChart':
      return form.title || form.categories.map((c) => c.label).filter(Boolean).join(', ').slice(0, 80);
    case 'balanceScale':
      return form.cards.map((c) => c.label.trim() || c.value.trim()).filter(Boolean).join(', ').slice(0, 80);
    case 'clockSet':
      return form.clocks.map((c) => c.label.trim() || c.time.trim()).filter(Boolean).join(', ').slice(0, 80);
    case 'gridDraw':
      return [form.area && `A=${form.area}`, form.perimeter && `Ç=${form.perimeter}`].filter(Boolean).join(', ');
    case 'logicGrid':
      return form.clues.find((c) => c.trim()) ?? form.subjects.join(', ');
    case 'wordSearch':
      return form.words.map((w) => w.label || w.word).filter(Boolean).join(', ').slice(0, 80);
  }
}

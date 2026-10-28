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
} from '../../models/activity-authoring-blanks';
import { QuestionRegion } from '../../models/draws';
import { Question } from '../../models/question';
import { QuestionService } from '../../services/question.service';
import {
  ClassificationSelection,
  ClassificationSelectorComponent,
} from '../../shared/components/classification-selector/classification-selector.component';
import { QuestionCanvasDragPlaceComponent } from '../../shared/components/question-canvas-drag-place/question-canvas-drag-place.component';
import { QuestionCanvasMatchComponent } from '../../shared/components/question-canvas-match/question-canvas-match.component';
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
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly kinds = AUTHORING_KINDS;
  readonly instructionMax = PASSAGE_INSTRUCTION_MAX_LENGTH;
  readonly labelMax = AUTHORING_LABEL_MAX;
  readonly tableLimits = BLANK_TABLE_LIMITS;

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
  readonly form = signal<AuthoringForm>(emptyForm('matrix', this.trueFalseLabels()));
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
  readonly wordPool = computed(() => (this.form().kind === 'wordPool' ? (this.form() as WordPoolForm) : null));
  /** Metindeki boşluk sayısı (boşluk doldurma / kelime havuzu ipucu). */
  readonly blankCount = computed(() => {
    const f = this.form();
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

  constructor() {
    this.load();
  }

  load(): void {
    if (!this.testId) {
      this.loading.set(false);
      this.loadError.set(true);
      return;
    }
    this.loading.set(true);
    this.loadError.set(false);
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
    this.form.set(emptyForm(kind, this.trueFalseLabels()));
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

  setShortRow(index: number, field: 'question' | 'answers', value: string): void {
    this.updateShortAnswer((f) => ({ ...f, rows: f.rows.map((r, i) => (i === index ? { ...r, [field]: value } : r)) }));
  }

  addShortRow(): void {
    this.updateShortAnswer((f) => ({ ...f, rows: [...f.rows, { question: '', answers: '' }] }));
  }

  removeShortRow(index: number): void {
    this.updateShortAnswer((f) => ({ ...f, rows: f.rows.filter((_, i) => i !== index) }));
  }

  // ---- Tablo ----

  setTableHeader(value: boolean): void {
    this.updateFillTable((f) => ({ ...f, hasHeader: value }));
  }

  setCell(row: number, column: number, value: string): void {
    this.updateFillTable((f) => ({
      ...f,
      cells: f.cells.map((r, ri) => (ri === row ? r.map((c, ci) => (ci === column ? value : c)) : r)),
    }));
  }

  addTableRow(): void {
    this.updateFillTable((f) => ({ ...f, cells: [...f.cells, f.cells[0].map(() => '')] }));
  }

  removeTableRow(row: number): void {
    this.updateFillTable((f) => ({ ...f, cells: f.cells.filter((_, i) => i !== row) }));
  }

  addTableColumn(): void {
    this.updateFillTable((f) => ({ ...f, cells: f.cells.map((r) => [...r, '']) }));
  }

  removeTableColumn(column: number): void {
    this.updateFillTable((f) => ({ ...f, cells: f.cells.map((r) => r.filter((_, i) => i !== column)) }));
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
    this.form.set(emptyForm(kind, this.trueFalseLabels()));
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
  }
}

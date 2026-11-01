import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  LogicGridFeedback,
  LogicGridPlanV1,
  LogicMark,
  LogicMarks,
  buildLogicGridPayload,
  cloneMarks,
  conflicts,
  countMarks,
  markOf,
  pairKey,
  parseLogicGridAnswer,
  parseLogicGridPlan,
  parseLogicGridSolution,
  solutionMarks,
  staircase,
  summaryRows,
  ticks,
  withAutoCrosses,
} from '../../../models/logic-grid';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/**
 * Issue #586: sözel mantık tablosu oynatıcısı (bileşen G, `logicGrid`). Kitaptaki merdiven tablo: üstte konu öğeleri (kişiler) ×
 * bütün kategoriler, altında kategori × kategori karalama blokları ({@link staircase}; tek kategoride yalnız üst blok), en altta
 * üstteki ✓'lerden dolan salt okunur cevap tablosu. `authoring` kipinde (görselsiz editör) yalnız merdiven tablo çizilir, çözümle
 * dolu ve salt okunurdur; seçenek başlıklarında ‹ › sırayı, kategori başlığındaki düğme karıştırmayı ister (editör uygular). Hücreye dokunmak boş → ✓ → ✗ → boş döngüsü; "Otomatik ✗" açıkken ✓ konunca
 * aynı bloğun satırına ve sütununa ✗ yazılır. Satır / sütun tekliği anında denetlenir (iki ✓ ya da hepsi ✗ → kırmızı çerçeve). Kontrol sonrası (konu,
 * kategori) çiftleri yeşil / kırmızı; çözüm açıkken doğru hücrelerde ✓. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-logic-grid',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatSlideToggleModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-logic-grid.component.html',
  styleUrl: './question-canvas-logic-grid.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasLogicGridComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseLogicGridPlan(value));
    this.restore();
  }

  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solutionJson.set(value ?? null);
  }

  @Input() imageRefresh: StorageImageResolver | null = null;

  @Input() hidePassage = false;

  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restore();
  }

  @Input() set feedback(value: LogicGridFeedback | null | undefined) {
    this._feedback.set(value ?? null);
  }

  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
  }

  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  @Input() set disabled(value: boolean) {
    this._disabled.set(!!value);
  }

  /** Editörde öğretmenin yüzeyi: çözüm gösterilir, işaretlenmez; ipucu / görsel / cevap tablosu editörde. */
  @Input() set authoring(value: boolean) {
    this._authoring.set(!!value);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  /** Yazarlık: `category` kategorisinde `position`'daki seçeneği bir adım taşı. */
  @Output() optionMove = new EventEmitter<{ category: number; position: number; delta: -1 | 1 }>();
  /** Yazarlık: kategorinin seçeneklerini karıştır. */
  @Output() categoryShuffle = new EventEmitter<number>();
  @Output() answered = new EventEmitter<void>();

  private lastEmitted: string | null = null;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<LogicGridPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<LogicGridFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);
  private readonly _authoring = signal(false);

  readonly marks = signal<LogicMarks>(Object.create(null) as LogicMarks);
  readonly autoCross = signal(true);

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly authoringMode = this._authoring.asReadonly();
  readonly readOnly = computed(() => this._showSolution() || this._disabled() || this._authoring());
  readonly feedbackState = this._feedback.asReadonly();

  readonly solution = computed(() => parseLogicGridSolution(this._solutionJson(), this._plan()));
  readonly groups = computed(() => {
    const plan = this._plan();
    return plan ? staircase(plan) : [];
  });
  /** #679: çözüm açık ve öğrenci işaret koymuş — öğrencinin tablosu kalır, üst blokta doğru / yanlış / kaçırılan hücre işaretli. */
  readonly compare = computed(() => this._showSolution() && !this._authoring() && countMarks(this.marks()) > 0);

  /** Gösterilen işaretler: çözüm açıkken (cevap yoksa) çözümünkiler (karalama blokları dahil), değilse öğrencinin. */
  readonly shownMarks = computed<LogicMarks>(() => {
    const plan = this._plan();
    if (!plan || this.compare() || !(this._showSolution() || this._authoring())) return this.marks();
    const marks = solutionMarks(plan, this.solution());
    for (const group of staircase(plan).slice(1)) {
      for (const category of group.cols) {
        for (const subject of plan.subject.items) {
          const row = this.solution()[subject.id]?.[group.category!.id];
          const col = this.solution()[subject.id]?.[category.id];
          if (row && col) (marks[row] ??= Object.create(null) as Record<string, LogicMark>)[col] = 'yes';
        }
      }
    }
    return marks;
  });
  readonly summary = computed(() => {
    const plan = this._plan();
    return plan ? summaryRows(plan, this.shownMarks()) : [];
  });
  readonly conflictCells = computed(() => {
    const plan = this._plan();
    return plan && !this._showSolution() ? conflicts(plan, this.marks()) : new Set<string>();
  });
  readonly pairMarks = computed(() => {
    const plan = this._plan();
    if (!plan || !this.compare()) return new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r.correct]));
    // Karşılaştırma: kontrolcü gibi — kategoride tek ✓ ve doğru seçenek.
    const out = new Map<string, boolean>();
    for (const subject of plan.subject.items) {
      for (const category of plan.categories) {
        const picks = ticks(plan, this.marks(), subject.id, category.id);
        out.set(pairKey(subject.id, category.id), picks.length === 1 && picks[0] === this.solution()[subject.id]?.[category.id]);
      }
    }
    return out;
  });
  readonly correctCount = computed(() => [...this.pairMarks().values()].filter(Boolean).length);
  readonly canReset = computed(() => !this.readOnly() && countMarks(this.marks()) > 0);

  /** Hücrede gösterilen işaret: çözüm açıkken doğru eşleşme ✓, değilse öğrencinin işareti. */
  mark(row: string, col: string): LogicMark | null {
    return markOf(this.shownMarks(), row, col);
  }

  /** Karşılaştırmada üst bloğun hücresi: doğru ✓ (hit), yanlış ✓ (wrong), ✓ konmamış doğru seçenek (missed). */
  compareCell(row: string, categoryId: string, option: string): 'hit' | 'wrong' | 'missed' | null {
    if (!this.compare()) return null;
    const yes = markOf(this.marks(), row, option) === 'yes';
    if (this.solution()[row]?.[categoryId] === option) return yes ? 'hit' : 'missed';
    return yes ? 'wrong' : null;
  }

  /** Karşılaştırmada yanlış özet hücresinin doğrusu (etiket); değilse null. */
  summaryTarget(row: string, categoryId: string): string | null {
    const plan = this._plan();
    if (!plan || !this.compare() || this.pairMarks().get(pairKey(row, categoryId)) !== false) return null;
    const option = this.solution()[row]?.[categoryId];
    return plan.categories.find((c) => c.id === categoryId)?.items.find((o) => o.id === option)?.label ?? null;
  }

  pairState(row: string, categoryId: string): 'ok' | 'bad' | null {
    const mark = this.pairMarks().get(pairKey(row, categoryId));
    return mark === undefined ? null : mark ? 'ok' : 'bad';
  }

  cycle(row: string, col: string): void {
    const plan = this._plan();
    if (!plan || this.readOnly()) return;
    const current = markOf(this.marks(), row, col);
    const next: LogicMark | null = current === null ? 'yes' : current === 'yes' ? 'no' : null;
    let marks = cloneMarks(this.marks());
    if (next === null) {
      delete marks[row]?.[col];
    } else {
      (marks[row] ??= Object.create(null) as Record<string, LogicMark>)[col] = next;
      if (next === 'yes' && this.autoCross()) marks = withAutoCrosses(plan, marks, row, col);
    }
    this.marks.set(marks);
    this.emit();
  }

  reset(): void {
    if (!this.canReset()) return;
    this.marks.set(Object.create(null) as LogicMarks);
    this.emit();
  }

  cellAria(subject: string, option: string, mark: LogicMark | null): string {
    return `${subject} – ${option}: ${mark === 'yes' ? '✓' : mark === 'no' ? '✗' : '—'}`;
  }

  private emit(): void {
    const plan = this._plan();
    if (!plan) return;
    const payload = buildLogicGridPayload(plan, this.marks());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    this.marks.set(parseLogicGridAnswer(json, this._plan()));
  }
}

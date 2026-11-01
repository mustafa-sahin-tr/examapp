import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  Cell,
  WordSearchFeedback,
  WordSearchLine,
  WordSearchPlanV1,
  buildWordSearchPayload,
  isStraight,
  lineCells,
  maxWordSearchLines,
  parseWordSearchAnswer,
  parseWordSearchPlan,
  parseWordSearchSolution,
  sameLine,
  spell,
} from '../../../models/word-search';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

interface DrawnLine {
  key: string;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  state: 'pending' | 'ok' | 'bad' | 'solution' | 'missed';
}

/**
 * Issue #586: kelime avı oynatıcısı (bileşen G, `wordSearch`). Harf ızgarasında düz çizgi seçilir: hücreden hücreye sürükleyerek
 * ya da iki dokunuşla (başlangıç, sonra bitiş; başlangıca yeniden dokunmak vazgeçer). Klavyede hücre düğmeleri Enter/Boşluk ile
 * aynı iki adımlı seçimi yapar. Yalnız yatay, dikey ve 45° çapraz çizgi kabul edilir; aynı çizgi ikinci kez eklenmez. Seçilen
 * çizgiler SVG ile çizilir ve altta listelenir (tek tek çıkarılabilir). Kontrol sonrası kelime çizgileri yeşil, kelime olmayanlar
 * kırmızı; bulunan kelimeler listede işaretlenir. Çözüm açıkken kelimelerin yerleri gösterilir. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-word-search',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-word-search.component.html',
  styleUrl: './question-canvas-word-search.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasWordSearchComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.anchor.set(null);
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseWordSearchPlan(value));
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

  /** Kontrol sonucu (sunucunun `feedback`'i); cevap değişince ebeveyn null verir. */
  @Input() set feedback(value: WordSearchFeedback | null | undefined) {
    this._feedback.set(value ?? null);
  }

  /** true iken seçim kapalı, kelimelerin yerleri gösterilir. */
  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
  }

  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  @Input() set disabled(value: boolean) {
    this._disabled.set(!!value);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  /** Son yayınlanan payload: ebeveynin geri verdiği yankı ekrandaki seçimi ezmez. */
  private lastEmitted: string | null = null;
  /** Sürükleme bir çizgi bitirdiyse ardından gelen tıklama yok sayılır. */
  private suppressClick = false;
  private dragFrom: Cell | null = null;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<WordSearchPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<WordSearchFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly lines = signal<WordSearchLine[]>([]);
  /** İki adımlı seçimde başlangıç hücresi. */
  readonly anchor = signal<Cell | null>(null);
  /** Sürüklerken / başlangıç seçiliyken imlecin üstündeki hücre (önizleme). */
  readonly hover = signal<Cell | null>(null);

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());
  readonly feedbackState = this._feedback.asReadonly();

  readonly rowIndexes = computed(() => Array.from({ length: this._plan()?.rows ?? 0 }, (_, i) => i));
  readonly colIndexes = computed(() => Array.from({ length: this._plan()?.cols ?? 0 }, (_, i) => i));

  readonly solution = computed(() => parseWordSearchSolution(this._solutionJson(), this._plan()));
  /** #679: çözüm açık ve öğrenci çizgi çekmiş — öğrencinin çizgileri yeşil / kırmızı, bulunamayan kelimeler kesikli yeşil. */
  readonly compare = computed(() => this._showSolution() && this.lines().length > 0);
  /** Karşılaştırma: kelime başına bulundu mu, çizgi başına bir kelime mi (kontrolcü gibi harflere bakar, iki yön). */
  private readonly compareMarks = computed(() => {
    const plan = this._plan();
    const words = new Map<string, boolean>();
    const lines: boolean[] = [];
    if (!plan || !this.compare()) return { words, lines };
    const spelled = this.lines().map((line) => spell(plan, line));
    const reversed = spelled.map((text) => [...text].reverse().join(''));
    for (const w of plan.words) {
      const target = this.solution()[w.id];
      const text = target ? spell(plan, target) : null;
      words.set(w.id, text !== null && spelled.some((s, i) => s === text || reversed[i] === text));
    }
    const texts = new Set(plan.words.flatMap((w) => (this.solution()[w.id] ? [spell(plan, this.solution()[w.id])] : [])));
    spelled.forEach((s, i) => lines.push(texts.has(s) || texts.has(reversed[i])));
    return { words, lines };
  });
  readonly foundWords = computed(() =>
    this.compare() ? this.compareMarks().words : new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r.correct]))
  );
  readonly foundCount = computed(() => [...this.foundWords().values()].filter(Boolean).length);
  readonly limitReached = computed(() => this.lines().length >= maxWordSearchLines(this._plan()?.words.length ?? 0));

  /** Önizleme çizgisi: başlangıçtan imlece düzse. */
  readonly preview = computed<WordSearchLine | null>(() => {
    const from = this.anchor();
    const to = this.hover();
    return from && to && isStraight(from, to) ? { from, to } : null;
  });

  readonly selectedCells = computed(() => {
    const out = new Set<string>();
    for (const line of this.lines()) for (const [r, c] of lineCells(line)) out.add(`${r},${c}`);
    return out;
  });

  readonly previewCells = computed(() => {
    const line = this.preview();
    const anchor = this.anchor();
    const out = new Set<string>(anchor ? [`${anchor[0]},${anchor[1]}`] : []);
    if (line) for (const [r, c] of lineCells(line)) out.add(`${r},${c}`);
    return out;
  });

  /** Çizilen çizgiler: öğrencinin seçimleri (kontrol sonrası doğru/yanlış), çözüm açıkken kelimelerin yerleri. */
  readonly drawn = computed<DrawnLine[]>(() => {
    const plan = this._plan();
    if (!plan) return [];
    const toView = (line: WordSearchLine, key: string, state: DrawnLine['state']): DrawnLine => ({
      key,
      x1: line.from[1] + 0.5,
      y1: line.from[0] + 0.5,
      x2: line.to[1] + 0.5,
      y2: line.to[0] + 0.5,
      state,
    });
    if (this.compare()) {
      const { words, lines } = this.compareMarks();
      const own = this.lines().map((line, i) => toView(line, `l-${i}`, lines[i] ? 'ok' : 'bad'));
      const missed = plan.words.flatMap((w) =>
        this.solution()[w.id] && !words.get(w.id) ? [toView(this.solution()[w.id], `s-${w.id}`, 'missed')] : []
      );
      return [...own, ...missed];
    }
    if (this._showSolution()) {
      return plan.words.flatMap((w) => (this.solution()[w.id] ? [toView(this.solution()[w.id], `s-${w.id}`, 'solution')] : []));
    }
    const hits = this._feedback()?.lines ?? null;
    return this.lines().map((line, i) => toView(line, `l-${i}`, hits && i < hits.length ? (hits[i] ? 'ok' : 'bad') : 'pending'));
  });

  readonly previewLine = computed(() => {
    const line = this.preview();
    return line ? { x1: line.from[1] + 0.5, y1: line.from[0] + 0.5, x2: line.to[1] + 0.5, y2: line.to[0] + 0.5 } : null;
  });

  readonly lineTexts = computed(() => {
    const plan = this._plan();
    return plan ? this.lines().map((line) => spell(plan, line)) : [];
  });

  readonly canUndo = computed(() => !this.readOnly() && this.lines().length > 0);

  letter(r: number, c: number): string {
    return this._plan()?.letters[r]?.[c] ?? '';
  }

  // ---- Seçim ----

  onPointerDown(event: PointerEvent, r: number, c: number): void {
    if (this.readOnly()) return;
    this.dragFrom = [r, c];
    this.suppressClick = false;
    (event.currentTarget as HTMLElement | null)?.releasePointerCapture?.(event.pointerId);
  }

  onPointerMove(event: PointerEvent): void {
    if (!this.dragFrom || this.readOnly()) return;
    const cell = this.cellAt(event.clientX, event.clientY);
    if (!cell || (cell[0] === this.dragFrom[0] && cell[1] === this.dragFrom[1])) return;
    if (!this.anchor()) this.anchor.set(this.dragFrom);
    this.hover.set(cell);
  }

  onPointerUp(event: PointerEvent): void {
    const from = this.dragFrom;
    this.dragFrom = null;
    if (!from || this.readOnly()) return;
    const cell = this.cellAt(event.clientX, event.clientY);
    // Aynı hücrede bırakıldı: dokunma — iki adımlı seçimi tıklama yapar.
    if (!cell || (cell[0] === from[0] && cell[1] === from[1])) return;
    this.suppressClick = true;
    this.anchor.set(null);
    this.hover.set(null);
    this.add({ from, to: cell });
  }

  onCellClick(r: number, c: number): void {
    if (this.suppressClick) {
      this.suppressClick = false;
      return;
    }
    if (this.readOnly()) return;
    const anchor = this.anchor();
    if (!anchor) {
      this.anchor.set([r, c]);
      this.hover.set([r, c]);
      return;
    }
    this.anchor.set(null);
    this.hover.set(null);
    if (anchor[0] === r && anchor[1] === c) return;
    this.add({ from: anchor, to: [r, c] });
  }

  onCellHover(r: number, c: number): void {
    if (this.anchor() && !this.dragFrom) this.hover.set([r, c]);
  }

  removeLine(index: number): void {
    if (this.readOnly()) return;
    this.lines.update((lines) => lines.filter((_, i) => i !== index));
    this.emit();
  }

  undo(): void {
    if (!this.canUndo()) return;
    this.lines.update((lines) => lines.slice(0, -1));
    this.emit();
  }

  reset(): void {
    if (!this.canUndo()) return;
    this.lines.set([]);
    this.anchor.set(null);
    this.emit();
  }

  cellAria(r: number, c: number): string {
    return `${this.letter(r, c)} ${r + 1}-${c + 1}`;
  }

  private add(line: WordSearchLine): void {
    if (!isStraight(line.from, line.to) || this.limitReached()) return;
    if (this.lines().some((l) => sameLine(l, line))) return;
    this.lines.update((lines) => [...lines, line]);
    this.emit();
  }

  private cellAt(x: number, y: number): Cell | null {
    const el = document.elementFromPoint(x, y)?.closest<HTMLElement>('[data-cell]');
    const key = el?.dataset['cell'];
    if (!key) return null;
    const [r, c] = key.split(',').map(Number);
    return Number.isInteger(r) && Number.isInteger(c) ? [r, c] : null;
  }

  private emit(): void {
    if (!this._plan()) return;
    const payload = buildWordSearchPayload(this.lines());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    this.lines.set(parseWordSearchAnswer(json, this._plan()));
  }
}

import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  CrosswordClue,
  CrosswordDir,
  CrosswordFeedback,
  CrosswordGrid,
  CrosswordPlanV1,
  buildCrosswordPayload,
  cellKey,
  cellNumbers,
  clueCells,
  clueNumbers,
  emptyGrid,
  parseCrosswordAnswer,
  parseCrosswordPlan,
  parseCrosswordSolution,
  solutionGrid,
  toCellChar,
  whiteCells,
} from '../../../models/crossword';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

export interface CrosswordCellView {
  row: number;
  col: number;
  key: string;
  white: boolean;
  number: number | null;
}

/**
 * Issue #586: çengel bulmaca oynatıcısı (bileşen G, `crossword`). Görsel (varsa) etkileşimsiz üstte; ızgara HTML olarak çizilir.
 * Hücreye yazınca etkin ipucunda sonraki hücreye geçilir; Backspace siler / geri gider, ok tuşları dolaşır, aynı hücreye yeniden
 * tıklamak (ya da boşluk) yönü değiştirir. İpucu listesinden seçilen ipucu ızgarada vurgulanır. Kontrol sonrası doğru kelimeler
 * yeşil, yanlışlar kırmızı (hangi harfin yanlış olduğu söylenmez); çözüm açıkken harfler çözümden gösterilir. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-crossword',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-crossword.component.html',
  styleUrl: './question-canvas-crossword.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasCrosswordComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.activeClue.set(null);
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseCrosswordPlan(value));
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
  @Input() set feedback(value: CrosswordFeedback | null | undefined) {
    this._feedback.set(value ?? null);
  }

  /** true iken ızgara salt okunur, harfler çözümden gösterilir. */
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

  /** Son yayınlanan payload: ebeveynin geri verdiği yankı ekrandaki ızgarayı ezmez. */
  private lastEmitted: string | null = null;
  private toggleOnClick = false;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<CrosswordPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<CrosswordFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly grid = signal<CrosswordGrid>([]);
  readonly activeClue = signal<string | null>(null);
  readonly focusedCell = signal<string | null>(null);

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());

  readonly numbers = computed(() => {
    const plan = this._plan();
    return plan ? clueNumbers(plan) : new Map<string, number>();
  });

  readonly cells = computed<CrosswordCellView[][]>(() => {
    const plan = this._plan();
    if (!plan) return [];
    const white = whiteCells(plan);
    const numbers = cellNumbers(plan);
    return Array.from({ length: plan.rows }, (_, row) =>
      Array.from({ length: plan.cols }, (_, col) => {
        const key = cellKey(row, col);
        return { row, col, key, white: white.has(key), number: numbers.get(key) ?? null };
      })
    );
  });

  readonly across = computed(() => this.sortedClues('across'));
  readonly down = computed(() => this.sortedClues('down'));

  readonly solution = computed(() => {
    const plan = this._plan();
    return plan ? solutionGrid(plan, parseCrosswordSolution(this._solutionJson(), plan)) : [];
  });

  /** Gösterilen harfler: çözüm açıkken çözümden, değilse öğrencinin ızgarası. */
  readonly shown = computed(() => (this._showSolution() ? this.solution() : this.grid()));

  readonly feedbackState = this._feedback.asReadonly();
  readonly clueMarks = computed(() => new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r.correct])));

  /** Kontrol sonrası hücre durumu: doğru bir kelimenin hücresi `ok`, yalnız yanlış kelimelerde kalan hücre `bad`. */
  readonly cellMarks = computed(() => {
    const plan = this._plan();
    const marks = this.clueMarks();
    const out = new Map<string, 'ok' | 'bad'>();
    if (!plan || marks.size === 0) return out;
    for (const clue of plan.clues) {
      const mark = marks.get(clue.id);
      if (mark === undefined) continue;
      for (const [r, c] of clueCells(clue)) {
        const key = cellKey(r, c);
        if (mark) out.set(key, 'ok');
        else if (!out.has(key)) out.set(key, 'bad');
      }
    }
    return out;
  });

  readonly correctCount = computed(() => [...this.clueMarks().values()].filter(Boolean).length);

  /** Çözüm açıkken öğrencinin farklı yazdığı hücreler. */
  readonly fixedCells = computed(() => {
    const out = new Set<string>();
    if (!this._showSolution()) return out;
    const solution = this.solution();
    const grid = this.grid();
    solution.forEach((row, r) =>
      row.forEach((ch, c) => {
        const mine = grid[r]?.[c] ?? ' ';
        if (mine !== ' ' && mine !== ch) out.add(cellKey(r, c));
      })
    );
    return out;
  });

  readonly activeCells = computed(() => {
    const clue = this.clueById(this.activeClue());
    return new Set(clue ? clueCells(clue).map(([r, c]) => cellKey(r, c)) : []);
  });

  readonly activeClueView = computed(() => this.clueById(this.activeClue()));

  readonly canReset = computed(() => !this.readOnly() && this.grid().some((row) => row.some((ch) => ch !== ' ')));

  clueNumber(clue: CrosswordClue): number {
    return this.numbers().get(clue.id) ?? 0;
  }

  letter(cell: CrosswordCellView): string {
    const ch = this.shown()[cell.row]?.[cell.col] ?? ' ';
    return ch === ' ' ? '' : ch;
  }

  /** Hücreye odaklanınca etkin ipucu: mevcut yönde bir ipucu varsa o, yoksa diğeri. */
  onFocus(cell: CrosswordCellView): void {
    this.focusedCell.set(cell.key);
    const current = this.clueById(this.activeClue());
    if (current && this.contains(current, cell)) return;
    const dir = current?.dir ?? 'across';
    this.activeClue.set((this.clueAt(cell, dir) ?? this.clueAt(cell, dir === 'across' ? 'down' : 'across'))?.id ?? null);
  }

  /** Basış anında hücre zaten odaklıysa tıklama yönü değiştirir (odak tıklamadan önce gelir). */
  onPointerDown(cell: CrosswordCellView): void {
    this.toggleOnClick = this.focusedCell() === cell.key;
  }

  onCellClick(cell: CrosswordCellView): void {
    if (this.toggleOnClick) this.toggleDir(cell);
    this.toggleOnClick = false;
  }

  onBlur(cell: CrosswordCellView): void {
    if (this.focusedCell() === cell.key) this.focusedCell.set(null);
  }

  onInput(event: Event, cell: CrosswordCellView): void {
    const input = event.target as HTMLInputElement;
    if (this.readOnly()) {
      input.value = this.letter(cell);
      return;
    }
    const ch = toCellChar(input.value);
    input.value = ch;
    this.setCell(cell, ch || ' ');
    if (ch) this.moveInClue(cell, 1);
  }

  onKeydown(event: KeyboardEvent, cell: CrosswordCellView): void {
    switch (event.key) {
      case 'Backspace': {
        event.preventDefault();
        if (this.readOnly()) return;
        if (this.grid()[cell.row]?.[cell.col] !== ' ') {
          this.setCell(cell, ' ');
        } else {
          const prev = this.moveInClue(cell, -1);
          if (prev) this.setCell(prev, ' ');
        }
        return;
      }
      case 'ArrowRight':
      case 'ArrowLeft':
      case 'ArrowUp':
      case 'ArrowDown': {
        event.preventDefault();
        const [dr, dc] = { ArrowRight: [0, 1], ArrowLeft: [0, -1], ArrowUp: [-1, 0], ArrowDown: [1, 0] }[event.key]!;
        this.step(cell, dr, dc);
        return;
      }
      case ' ': {
        event.preventDefault();
        this.toggleDir(cell);
        return;
      }
    }
  }

  selectClue(clue: CrosswordClue): void {
    this.activeClue.set(clue.id);
    const [r, c] = clueCells(clue).find(([r, c]) => (this.grid()[r]?.[c] ?? ' ') === ' ') ?? [clue.row, clue.col];
    this.focus(r, c);
  }

  reset(): void {
    const plan = this._plan();
    if (!plan || !this.canReset()) return;
    this.grid.set(emptyGrid(plan));
    this.emit();
  }

  cellAria(cell: CrosswordCellView): string {
    const clue = this.clueById(this.activeClue());
    return clue && this.contains(clue, cell) ? `${this.clueNumber(clue)} ${clue.text ?? ''}`.trim() : `${cell.row + 1}-${cell.col + 1}`;
  }

  private sortedClues(dir: CrosswordDir): CrosswordClue[] {
    const plan = this._plan();
    if (!plan) return [];
    const numbers = this.numbers();
    return plan.clues.filter((c) => c.dir === dir).sort((a, b) => (numbers.get(a.id) ?? 0) - (numbers.get(b.id) ?? 0));
  }

  private clueById(id: string | null): CrosswordClue | null {
    return this._plan()?.clues.find((c) => c.id === id) ?? null;
  }

  private contains(clue: CrosswordClue, cell: CrosswordCellView): boolean {
    return clueCells(clue).some(([r, c]) => r === cell.row && c === cell.col);
  }

  private clueAt(cell: CrosswordCellView, dir: CrosswordDir): CrosswordClue | null {
    return this._plan()?.clues.find((c) => c.dir === dir && this.contains(c, cell)) ?? null;
  }

  private toggleDir(cell: CrosswordCellView): void {
    const current = this.clueById(this.activeClue());
    const other = this.clueAt(cell, current?.dir === 'across' ? 'down' : 'across');
    if (other) this.activeClue.set(other.id);
  }

  /** Etkin ipucunda bir sonraki / önceki hücreye geçer; ipucunun ucundaysa yerinde kalır. */
  private moveInClue(cell: CrosswordCellView, delta: 1 | -1): CrosswordCellView | null {
    const clue = this.clueById(this.activeClue());
    if (!clue) return null;
    const cells = clueCells(clue);
    const index = cells.findIndex(([r, c]) => r === cell.row && c === cell.col);
    const target = cells[index + delta];
    if (index < 0 || !target) return null;
    this.focus(target[0], target[1]);
    return this.cells()[target[0]]?.[target[1]] ?? null;
  }

  /** Ok tuşu: o yönde ilk beyaz hücreye atlar; yön ipucu yönünü de seçer. */
  private step(cell: CrosswordCellView, dr: number, dc: number): void {
    const cells = this.cells();
    let r = cell.row + dr;
    let c = cell.col + dc;
    while (cells[r]?.[c] && !cells[r][c].white) {
      r += dr;
      c += dc;
    }
    const target = cells[r]?.[c];
    if (!target) return;
    const dir: CrosswordDir = dr === 0 ? 'across' : 'down';
    const clue = this.clueAt(target, dir);
    if (clue) this.activeClue.set(clue.id);
    this.focus(r, c);
  }

  private focus(row: number, col: number): void {
    const input = this.host.nativeElement.querySelector<HTMLInputElement>(`input[data-cell="${cellKey(row, col)}"]`);
    input?.focus();
    input?.select();
  }

  private setCell(cell: CrosswordCellView, ch: string): void {
    if (!cell.white) return;
    this.grid.update((grid) => grid.map((row, r) => (r === cell.row ? row.map((x, c) => (c === cell.col ? ch : x)) : row)));
    this.emit();
  }

  private emit(): void {
    if (!this._plan()) return;
    const payload = buildCrosswordPayload(this.grid());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    this.grid.set(parseCrosswordAnswer(json, this._plan()));
  }
}

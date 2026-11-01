import { NgStyle } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, map } from 'rxjs';

import { ActivityFeedbackRow } from '../../../models/activity';
import { QuestionRegion } from '../../../models/draws';
import {
  MatrixBox,
  MatrixChoicePlanV1,
  MatrixChoiceRow,
  MatrixSelections,
  buildMatrixChoicePayload,
  cellOf,
  emptyMatrixSelections,
  matrixHasAllCells,
  parseMatrixChoicePlan,
  parseMatrixChoiceSelections,
  parseMatrixChoiceSolution,
  selectionOf,
  withSelection,
} from '../../../models/matrix-choice';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { seededShuffle } from '../../utils/seeded-shuffle';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

export type MatrixRowMark = 'correct' | 'wrong' | null;

/**
 * Issue #574: matris seçim oynatıcısı (D/Y, ✓ tablo, çoklu seçim, <, >, =). Planın tüm satırlarında tüm kolonlar için hücre
 * kutusu varsa öğrenci görsel üstündeki hücrelere dokunur; yoksa görselin altında satır listesi + seçenek düğmeleri çıkar
 * (satır kutusu varsa görselde numarasıyla vurgulanır). Klavye: her seçenek bir düğme (`aria-pressed`), ipucu `aria-live`.
 *
 * Girdi/çıktılar sürükle-bırak oynatıcısıyla aynı: test ekranı ve etkinlik oynatıcısı aynı sözleşmeyle besler.
 * Çözüm (`interactionSolutionJson`) yalnız verilirse kullanılır (#570); puan/yüzde yok, kontrol satır başına doğru/yanlış.
 */
@Component({
  selector: 'app-question-canvas-matrix-choice',
  standalone: true,
  imports: [NgStyle, MatButtonModule, MatIconModule, TranslocoDirective, TranslocoPipe, StorageImageRetryDirective],
  templateUrl: './question-canvas-matrix-choice.component.html',
  styleUrl: './question-canvas-matrix-choice.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasMatrixChoiceComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    // Oynatıcı aynı bileşeni sıradaki maddeye yeniden kullanır: önceki maddenin duyurusu taşınmasın.
    if (value !== this._region()) this.resetTransientState();
    this._region.set(value);
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this.resetTransientState();
    this._plan.set(parseMatrixChoicePlan(value));
    this.restoreFromAnswerPayload();
  }

  /** #570: çözüm (`{"r1":["c2"]}`) — backend yalnız örnek soruda, biten testte, çözüm istendiğinde ya da sahibe doldurur. */
  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solutionJson.set(value ?? null);
  }

  @Input() imageRefresh: StorageImageResolver | null = null;

  /** Örnek soru: çözüm geldiyse cevaplanan satırlar istemcide işaretlenir. */
  @Input() set practiceFeedback(value: boolean) {
    this._practiceFeedback.set(!!value);
  }

  @Input() hidePassage = false;

  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restoreFromAnswerPayload();
  }

  /** Sunucu kontrolünün satır sonucu (satır id = plan satır id'si). Verilirse `practiceFeedback`in önüne geçer. */
  @Input() set feedbackRows(value: ActivityFeedbackRow[] | null | undefined) {
    if (!value) {
      this._feedback.set(null);
      return;
    }
    this._feedback.set(new Map(value.map((row) => [row.id, row.correct])));
  }

  /** true iken öğrencinin seçimi yerine çözüm gösterilir (salt okunur); false olunca öğrencinin seçimi geri gelir. */
  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
  }

  /** Etkinlik oynatıcısı (dokunma hedefleri ≥ 44 px). Test ekranında da aynı etkileşim çalışır. */
  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  /** Issue #590: `shuffleRows` planında satır sırasının tohumu (oynatıcı madde id'si verir; yoksa satır id'leri). */
  @Input() set shuffleSeed(value: string | number | null | undefined) {
    this._shuffleSeed.set(value ?? null);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private readonly transloco = inject(TranslocoService);
  private readonly i18nTick = toSignal(
    this.transloco.events$.pipe(
      filter((e) => e.type === 'langChanged' || e.type === 'translationLoadSuccess'),
      map(() => Date.now())
    ),
    { initialValue: 0 }
  );

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<MatrixChoicePlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<Map<string, boolean> | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _practiceFeedback = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _shuffleSeed = signal<string | number | null>(null);

  /** Öğrencinin seçimleri (satır id → kolon id'leri). */
  readonly selections = signal<MatrixSelections>(emptyMatrixSelections());
  /** Liste modunda odaktaki satır: görselde kutusu vurgulanır. */
  readonly focusedRowId = signal<string | null>(null);
  /** Son seçimin ekran okuyucu duyurusu. */
  private readonly announcement = signal<string | null>(null);

  readonly plan = this._plan.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl ?? null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly rows = computed(() => this._plan()?.rows ?? []);
  /**
   * Liste modunda gösterim sırası: `shuffleRows` planında tohumlu karışık (aynı madde hep aynı sırada), değilse plan sırası.
   * Görsel üstü (hücre) modu ve numaralar görsele bağlı olduğundan orada karıştırılmaz. Satır başlığı numarası gösterim sırasıdır.
   */
  readonly listRows = computed(() => {
    const plan = this._plan();
    const rows = plan?.rows ?? [];
    if (!plan?.shuffleRows || matrixHasAllCells(plan)) return rows;
    const seed = this._shuffleSeed() ?? rows.map((r) => r.id).join('|');
    return seededShuffle(rows, `${seed}`);
  });
  readonly columns = computed(() => this._plan()?.columns ?? []);
  readonly multi = computed(() => this._plan()?.multiPerRow === true);
  /** Hücre kutuları tam: görsel üstünde dokunma; değilse görselin altında liste. */
  readonly cellMode = computed(() => matrixHasAllCells(this._plan()));
  readonly solution = computed(() => parseMatrixChoiceSolution(this._solutionJson(), this._plan()));

  /** #679: çözüm açık ve öğrenci seçim yapmış — öğrencinin seçimleri yeşil / kırmızı, kaçırılan doğru seçenek kesikli. */
  readonly compare = computed(
    () =>
      this._showSolution() &&
      Object.keys(this.solution()).length > 0 &&
      this.rows().some((row) => selectionOf(this.selections(), row.id).length > 0)
  );

  /** Ekrandaki seçimler: öğrencinin (karşılaştırmada da); çözüm açık ama seçim yoksa doğru cevap. */
  readonly displaySelections = computed<MatrixSelections>(() =>
    this._showSolution() && !this.compare() ? this.solution() : this.selections()
  );

  /** Karşılaştırmada hücre: doğru seçim (hit), yanlış seçim (wrong), seçilmemiş doğru seçenek (missed). */
  compareCell(rowId: string, columnId: string): 'hit' | 'wrong' | 'missed' | null {
    if (!this.compare()) return null;
    const expected = selectionOf(this.solution(), rowId).includes(columnId);
    const actual = selectionOf(this.selections(), rowId).includes(columnId);
    return expected ? (actual ? 'hit' : 'missed') : actual ? 'wrong' : null;
  }

  readonly rowMark = computed(() => {
    const feedback = this._feedback();
    const showSolution = this._showSolution();
    const practice = this._practiceFeedback();
    const solution = this.solution();
    const selections = this.selections();
    const compare = this.compare();
    return (rowId: string): MatrixRowMark => {
      if (compare) return sameSet(selectionOf(solution, rowId), selectionOf(selections, rowId)) ? 'correct' : 'wrong';
      if (showSolution) return null;
      if (feedback) {
        if (!feedback.has(rowId)) return null;
        return feedback.get(rowId) ? 'correct' : 'wrong';
      }
      // İstemci tarafı işaret yalnız örnek soruda ve çözüm geldiyse; cevaplanmamış satır işaretlenmez.
      if (!practice) return null;
      const expected = selectionOf(solution, rowId);
      const actual = selectionOf(selections, rowId);
      if (!expected.length || !actual.length) return null;
      return sameSet(expected, actual) ? 'correct' : 'wrong';
    };
  });

  readonly answeredCount = computed(() => {
    const selections = this.selections();
    return this.rows().filter((row) => selectionOf(selections, row.id).length > 0).length;
  });

  readonly hint = computed(() => {
    this.i18nTick();
    if (this._showSolution()) return this.t(this.compare() ? 'solutionCompareHint' : 'solutionShownHint');
    const announcement = this.announcement();
    if (announcement) return announcement;
    return this.t(this.multi() ? 'hintMulti' : 'hintSingle');
  });

  /** Plan koordinatları soru görseli pikselinde (region.width/height); kaplama görselle birlikte ölçeklenir. */
  boxStyle(box: MatrixBox | null | undefined): Record<string, string> {
    const region = this._region();
    if (!region || !box || !region.width || !region.height) return { display: 'none' };
    return {
      left: `${(box.x / region.width) * 100}%`,
      top: `${(box.y / region.height) * 100}%`,
      width: `${(box.width / region.width) * 100}%`,
      height: `${(box.height / region.height) * 100}%`,
    };
  }

  /** Satır işaretinin konumu: satır kutusunun (yoksa hücrelerin) sağ üst köşesi. */
  markStyle(row: MatrixChoiceRow): Record<string, string> {
    const bounds = row.box ?? unionBox(row.cells ? Object.values(row.cells) : []);
    const region = this._region();
    if (!region || !bounds || !region.width || !region.height) return { display: 'none' };
    return {
      left: `${((bounds.x + bounds.width) / region.width) * 100}%`,
      top: `${(bounds.y / region.height) * 100}%`,
    };
  }

  isSelected(rowId: string, columnId: string): boolean {
    return selectionOf(this.displaySelections(), rowId).includes(columnId);
  }

  /** Satırın gösterilen adı: yazılı metin ya da "Satır n". */
  rowTitle(row: MatrixChoiceRow, index: number): string {
    this.i18nTick();
    return row.label?.trim() || this.t('rowFallback', { n: index + 1 });
  }

  rowAria(row: MatrixChoiceRow, index: number): string {
    const base = this.t('rowAria', { n: index + 1, label: this.rowTitle(row, index) });
    const mark = this.rowMark()(row.id);
    return mark ? `${base}, ${this.t(mark === 'correct' ? 'markCorrect' : 'markWrong')}` : base;
  }

  cellAria(row: MatrixChoiceRow, index: number, columnLabel: string): string {
    return this.t('cellAria', { row: this.rowTitle(row, index), column: columnLabel });
  }

  /** Seçeneğe dokunma / Enter / Space: tekli seçimde değiştirir (aynısına tekrar dokununca kaldırır), çokluda aç/kapa. */
  toggle(rowId: string, columnId: string): void {
    if (this._showSolution()) return;
    const plan = this._plan();
    if (!plan) return;
    const rowIndex = plan.rows.findIndex((r) => r.id === rowId);
    const column = plan.columns.find((c) => c.id === columnId);
    if (rowIndex < 0 || !column) return;

    const current = selectionOf(this.selections(), rowId);
    let next: string[];
    if (plan.multiPerRow) {
      next = current.includes(columnId) ? current.filter((c) => c !== columnId) : [...current, columnId];
      // Kolon sırası korunsun (cevap imzası tıklama sırasına bağlı olmasın).
      next = plan.columns.map((c) => c.id).filter((id) => next.includes(id));
    } else {
      next = current.length === 1 && current[0] === columnId ? [] : [columnId];
    }

    this.selections.set(withSelection(this.selections(), rowId, next));

    const rowTitle = this.rowTitle(plan.rows[rowIndex], rowIndex);
    this.announcement.set(
      next.includes(columnId)
        ? this.t('announceSelected', { row: rowTitle, column: column.label })
        : this.t('announceCleared', { row: rowTitle, column: column.label })
    );
    this.emitPayload();
    this.answered.emit();
  }

  clearAll(): void {
    if (this._showSolution()) return;
    this.selections.set(emptyMatrixSelections());
    this.announcement.set(null);
    this.emitPayload();
  }

  /** Madde değişince: `aria-live` duyurusu ve liste odağı sıfırlanır (ipucu varsayılan metne döner). */
  private resetTransientState(): void {
    this.announcement.set(null);
    this.focusedRowId.set(null);
  }

  private restoreFromAnswerPayload(): void {
    this.selections.set(parseMatrixChoiceSelections(this._answerPayloadJson(), this._plan()));
  }

  private emitPayload(): void {
    this.answerPayloadChange.emit(buildMatrixChoicePayload(this.selections()));
  }

  /** Şablon için: hücre kutusu (yalnız kendi özelliği). */
  cell(row: MatrixChoiceRow, columnId: string): MatrixBox | null {
    return cellOf(row, columnId);
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`shared.questionCanvas.matrix.${key}`, params) ?? '';
  }
}

function sameSet(a: string[], b: string[]): boolean {
  if (a.length !== b.length) return false;
  const set = new Set(a);
  return b.every((x) => set.has(x));
}

function unionBox(boxes: MatrixBox[]): MatrixBox | null {
  if (boxes.length === 0) return null;
  const x1 = Math.min(...boxes.map((b) => b.x));
  const y1 = Math.min(...boxes.map((b) => b.y));
  const x2 = Math.max(...boxes.map((b) => b.x + b.width));
  const y2 = Math.max(...boxes.map((b) => b.y + b.height));
  return { x: x1, y: y1, width: x2 - x1, height: y2 - y1 };
}

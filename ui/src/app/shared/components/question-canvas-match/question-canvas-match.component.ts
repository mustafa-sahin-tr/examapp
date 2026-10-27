import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, map } from 'rxjs';

import { ActivityFeedbackRow } from '../../../models/activity';
import { QuestionRegion } from '../../../models/draws';
import {
  MatchItem,
  MatchMap,
  MatchPlanV1,
  MatchSide,
  buildMatchPayload,
  emptyMatchMap,
  leftOf,
  parseMatchPairs,
  parseMatchPlan,
  parseMatchSolution,
  rightOf,
  withPair,
} from '../../../models/match';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';
import { QuestionImageCropComponent } from '../question-image-crop/question-image-crop.component';
import { seededShuffle } from '../../utils/seeded-shuffle';

export type MatchMark = 'correct' | 'wrong' | null;

interface MatchSelection {
  side: MatchSide;
  id: string;
}

/** Ekranda çizilen eşleşme çizgisi (pano pikselinde). */
export interface MatchLine {
  leftId: string;
  rightId: string;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  kind: 'example' | 'own' | 'solution';
  mark: MatchMark;
}

interface DisplayPair {
  leftId: string;
  rightId: string;
  kind: MatchLine['kind'];
}

/**
 * Issue #576: eşleştirme oynatıcısı. İki sütun (sol / sağ; öğeler metin ya da soru görselinden kırpma). Dokun-dokun:
 * bir öğeye dokun, karşı sütundan eşine dokun; bağlı bir öğeye dokununca eşleşme çözülür ve öğe seçili kalır. Klavye:
 * Enter/Boşluk seç (odak karşı sütuna geçer), oklar sütun içinde / sütunlar arasında gezer, Esc vazgeçer. Eşleşmeler
 * sütunlar arasında SVG çizgiyle gösterilir (pano boyutu değişince yeniden ölçülür). Önceden bağlı örnek çift bağlı ve
 * pasif görünür, payload'a girmez. Kontrolden sonra (`feedbackRows`, satır id = sol id) çizgi ve sol öğe ✓/✗ alır.
 */
@Component({
  selector: 'app-question-canvas-match',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, TranslocoPipe, StorageImageRetryDirective, QuestionImageCropComponent],
  templateUrl: './question-canvas-match.component.html',
  styleUrl: './question-canvas-match.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasMatchComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    if (value !== this._region()) {
      this.selected.set(null);
      this.announcement.set(null);
    }
    this._region.set(value);
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this.lastEmitted = null;
    this.selected.set(null);
    this.announcement.set(null);
    this._plan.set(parseMatchPlan(value));
    this.pairs.set(emptyMatchMap());
    this.restoreFromAnswerPayload();
  }

  /** #570: çözüm (`{"l1":"r2"}`) — backend yalnız örnek soruda, biten testte, çözüm istendiğinde ya da sahibe doldurur. */
  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solutionJson.set(value ?? null);
  }

  @Input() imageRefresh: StorageImageResolver | null = null;

  @Input() set practiceFeedback(value: boolean) {
    this._practiceFeedback.set(!!value);
  }

  @Input() hidePassage = false;

  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restoreFromAnswerPayload();
  }

  /** Sunucu kontrolünün sonucu (satır id = sol öğe id'si). */
  @Input() set feedbackRows(value: ActivityFeedbackRow[] | null | undefined) {
    this._feedback.set(value ? new Map(value.map((row) => [row.id, row.correct])) : null);
  }

  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
    if (value) this.selected.set(null);
  }

  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  /** Ebeveyn kilidi (etkinlikte kontrol / bitirme sürerken): etkileşim kapalı, seçim düşer. */
  @Input() set disabled(value: boolean) {
    this._disabled.set(!!value);
    if (value) this.selected.set(null);
  }

  /** Sağ sütun sırasını karıştırma tohumu (ör. oturum maddesi id'si); verilmezse plandan türetilir. */
  @Input() set shuffleSeed(value: string | number | null | undefined) {
    this._shuffleSeed.set(value ?? null);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly board = viewChild<ElementRef<HTMLElement>>('board');
  private readonly transloco = inject(TranslocoService);
  private readonly i18nTick = toSignal(
    this.transloco.events$.pipe(
      filter((e) => e.type === 'langChanged' || e.type === 'translationLoadSuccess'),
      map(() => Date.now())
    ),
    { initialValue: 0 }
  );

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<MatchPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<Map<string, boolean> | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _practiceFeedback = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);
  private readonly _shuffleSeed = signal<string | number | null>(null);
  private lastEmitted: string | null = null;
  private measureFrame: number | null = null;

  /** Öğrencinin eşleşmeleri (önceden bağlı çift hariç). */
  readonly pairs = signal<MatchMap>(emptyMatchMap());
  readonly selected = signal<MatchSelection | null>(null);
  readonly lines = signal<MatchLine[]>([]);
  readonly boardSize = signal<{ width: number; height: number }>({ width: 0, height: 0 });
  private readonly announcement = signal<string | null>(null);

  readonly sides: readonly MatchSide[] = ['left', 'right'];
  readonly plan = this._plan.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly region = this._region.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl ?? null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly left = computed(() => this._plan()?.left ?? []);
  /** Sağ sütun: tohuma göre karıştırılmış, aynı maddede sabit (plan sırası eşleşmeyi ele vermesin). */
  readonly right = computed(() => {
    const items = this._plan()?.right ?? [];
    const seed = this._shuffleSeed() ?? items.map((i) => i.id).join('|');
    return seededShuffle(items, `${seed}`);
  });
  /** Etkileşim kapalı: çözüm gösteriliyor ya da ebeveyn kilitledi. */
  readonly locked = computed(() => this._showSolution() || this._disabled());
  readonly solution = computed(() => parseMatchSolution(this._solutionJson(), this._plan()));
  private readonly prelinkedMap = computed(() => {
    const result = emptyMatchMap();
    (this._plan()?.prelinked ?? []).forEach((p) => (result[p.leftId] = p.rightId));
    return result;
  });
  private readonly leftIndex = computed(() => new Map(this.left().map((item, i) => [item.id, i])));
  private readonly rightIndex = computed(() => new Map(this.right().map((item, i) => [item.id, i])));

  /** Ekranda bağlı görünen çiftler: örnek çift + (çözüm açıkken doğru, değilse öğrencinin) eşleşmeler. */
  readonly displayPairs = computed<DisplayPair[]>(() => {
    const prelinked = this._plan()?.prelinked ?? [];
    const own = this._showSolution() ? this.solution() : this.pairs();
    const kind: DisplayPair['kind'] = this._showSolution() ? 'solution' : 'own';
    return [
      ...prelinked.map((p) => ({ ...p, kind: 'example' as const })),
      ...this.left()
        .map((item) => ({ leftId: item.id, rightId: rightOf(own, item.id) ?? '', kind }))
        .filter((p) => !!p.rightId),
    ];
  });

  readonly pairedCount = computed(() => Object.keys(this.pairs()).length);

  readonly leftMark = computed(() => {
    const feedback = this._feedback();
    const showSolution = this._showSolution();
    const practice = this._practiceFeedback();
    const solution = this.solution();
    const pairs = this.pairs();
    const prelinked = this.prelinkedMap();
    return (leftId: string): MatchMark => {
      if (showSolution || Object.hasOwn(prelinked, leftId)) return null;
      if (feedback) {
        if (!feedback.has(leftId)) return null;
        return feedback.get(leftId) ? 'correct' : 'wrong';
      }
      const own = rightOf(pairs, leftId);
      const expected = rightOf(solution, leftId);
      if (!practice || !own || !expected) return null;
      return own === expected ? 'correct' : 'wrong';
    };
  });

  readonly hint = computed(() => {
    this.i18nTick();
    const announcement = this.announcement();
    if (announcement) return announcement;
    if (this._showSolution()) return this.t('solutionShownHint');
    const selection = this.selected();
    if (selection) {
      return this.t(selection.side === 'left' ? 'hintPickRight' : 'hintPickLeft', { item: this.itemText(selection.side, selection.id) });
    }
    return this.t('hintIdle');
  });

  constructor() {
    // Eşleşme / öğe / çözüm görünümü değişince çizgiler yeniden ölçülür (DOM güncellendikten sonra).
    effect(() => {
      // Etiket (dil) ve seçim değişince öğe yükseklikleri değişebilir: onlar da izlenir.
      this.displayPairs();
      this.leftMark();
      this.i18nTick();
      this.selected();
      this.scheduleMeasure();
    });
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => {
      if (this.measureFrame !== null && typeof cancelAnimationFrame !== 'undefined') cancelAnimationFrame(this.measureFrame);
    });
    if (typeof ResizeObserver !== 'undefined') {
      const observer = new ResizeObserver(() => this.scheduleMeasure());
      observer.observe(this.host.nativeElement);
      destroyRef.onDestroy(() => observer.disconnect());
    }
    // Kırpma görselleri yüklenince öğe yükseklikleri değişir (`load` kabarcıklanmaz: yakalama evresinde dinlenir).
    const onLoad = () => this.scheduleMeasure();
    this.host.nativeElement.addEventListener('load', onLoad, true);
    destroyRef.onDestroy(() => this.host.nativeElement.removeEventListener('load', onLoad, true));
  }

  // ---------------------------------------------------------------------------
  // Görünüm yardımcıları
  // ---------------------------------------------------------------------------

  isPrelinked(side: MatchSide, id: string): boolean {
    const plan = this._plan();
    if (!plan) return false;
    return plan.prelinked.some((p) => (side === 'left' ? p.leftId : p.rightId) === id);
  }

  isSelected(side: MatchSide, id: string): boolean {
    const selection = this.selected();
    return selection?.side === side && selection.id === id;
  }

  /** Öğenin ekranda bağlı olduğu karşı öğe (örnek çift dahil). */
  partnerOf(side: MatchSide, id: string): string | null {
    const pair = this.displayPairs().find((p) => (side === 'left' ? p.leftId : p.rightId) === id);
    return pair ? (side === 'left' ? pair.rightId : pair.leftId) : null;
  }

  itemText(side: MatchSide, id: string): string {
    this.i18nTick();
    const items = side === 'left' ? this.left() : this.right();
    const index = (side === 'left' ? this.leftIndex() : this.rightIndex()).get(id);
    const item = index === undefined ? null : items[index];
    if (!item) return id;
    return item.label?.trim() || this.t(side === 'left' ? 'leftFallback' : 'rightFallback', { n: (index ?? 0) + 1 });
  }

  itemAria(side: MatchSide, item: MatchItem): string {
    const name = this.itemText(side, item.id);
    const partner = this.partnerOf(side, item.id);
    const other: MatchSide = side === 'left' ? 'right' : 'left';
    let text = partner ? this.t('itemAriaLinked', { item: name, partner: this.itemText(other, partner) }) : name;
    if (this.isPrelinked(side, item.id)) text = `${text}, ${this.t('exampleAria')}`;
    const mark = side === 'left' ? this.leftMark()(item.id) : null;
    return mark ? `${text}, ${this.t(mark === 'correct' ? 'markCorrect' : 'markWrong')}` : text;
  }

  // ---------------------------------------------------------------------------
  // Etkileşim
  // ---------------------------------------------------------------------------

  /** Öğeye dokunma / Enter. Klavyeyle seçilince odak karşı sütunun ilk uygun öğesine geçer. */
  onItem(side: MatchSide, id: string, event?: Event): void {
    if (this.locked() || this.isPrelinked(side, id)) return;
    const keyboard = event instanceof MouseEvent && event.detail === 0;
    const selection = this.selected();
    const other: MatchSide = side === 'left' ? 'right' : 'left';
    if (selection && selection.side !== side) {
      const leftId = side === 'left' ? id : selection.id;
      const rightId = side === 'right' ? id : selection.id;
      this.selected.set(null);
      this.announcement.set(this.t('announceLinked', { left: this.itemText('left', leftId), right: this.itemText('right', rightId) }));
      this.commit(withPair(this.pairs(), leftId, rightId));
      return;
    }
    if (selection?.side === side && selection.id === id) {
      this.cancelSelection();
      return;
    }
    // Bağlı öğeye dokunma (seçim yokken): eşleşme çözülür, öğe seçili kalır (yeni eşini seçmek için).
    const partner = side === 'left' ? rightOf(this.pairs(), id) : leftOf(this.pairs(), id);
    if (!selection && partner) {
      const leftId = side === 'left' ? id : partner;
      this.commit(withPair(this.pairs(), leftId, null));
      this.selected.set({ side, id });
      this.announcement.set(this.t('announceUnlinked', { item: this.itemText(side, id) }));
      if (keyboard) this.focusColumn(other, 0);
      return;
    }
    this.selected.set({ side, id });
    this.announcement.set(null);
    if (keyboard) this.focusColumn(other, 0);
  }

  /** Oklar: sütun içinde yukarı / aşağı, sağ / sol ile karşı sütun. */
  onItemKeydown(side: MatchSide, index: number, event: KeyboardEvent): void {
    const key = event.key;
    if (key === 'ArrowDown' || key === 'ArrowUp') {
      event.preventDefault();
      this.focusColumn(side, index + (key === 'ArrowDown' ? 1 : -1), key === 'ArrowDown' ? 1 : -1);
    } else if (key === 'ArrowRight' || key === 'ArrowLeft') {
      event.preventDefault();
      this.focusColumn(key === 'ArrowRight' ? 'right' : 'left', index);
    }
  }

  cancelSelection(): void {
    if (!this.selected()) return;
    this.selected.set(null);
    this.announcement.set(this.t('announceCancelled'));
  }

  clearAll(): void {
    if (this.locked()) return;
    this.selected.set(null);
    this.commit(emptyMatchMap());
    this.announcement.set(this.t('announceCleared'));
  }

  /** Çizgileri hemen ölçer (testler ve yeniden boyutlandırma). */
  measure(): void {
    this.measureFrame = null;
    const board = this.board()?.nativeElement;
    if (!board) {
      this.lines.set([]);
      return;
    }
    const origin = board.getBoundingClientRect();
    const find = (side: MatchSide, id: string) =>
      board.querySelector<HTMLElement>(`[data-side="${side}"][data-item="${cssEscape(id)}"]`);
    const mark = this.leftMark();
    const lines: MatchLine[] = [];
    for (const pair of this.displayPairs()) {
      const l = find('left', pair.leftId)?.getBoundingClientRect();
      const r = find('right', pair.rightId)?.getBoundingClientRect();
      if (!l || !r) continue;
      lines.push({
        leftId: pair.leftId,
        rightId: pair.rightId,
        x1: round(l.right - origin.left),
        y1: round(l.top + l.height / 2 - origin.top),
        x2: round(r.left - origin.left),
        y2: round(r.top + r.height / 2 - origin.top),
        kind: pair.kind,
        mark: pair.kind === 'own' ? mark(pair.leftId) : null,
      });
    }
    this.boardSize.set({ width: round(origin.width), height: round(origin.height) });
    this.lines.set(lines);
  }

  // ---------------------------------------------------------------------------
  // Durum
  // ---------------------------------------------------------------------------

  private scheduleMeasure(): void {
    if (typeof requestAnimationFrame === 'undefined') return;
    if (this.measureFrame !== null) return;
    this.measureFrame = requestAnimationFrame(() => this.measure());
  }

  private commit(next: MatchMap): void {
    const plan = this._plan();
    const payload = buildMatchPayload(next, plan);
    const changed = payload !== buildMatchPayload(this.pairs(), plan);
    this.pairs.set(next);
    if (!changed) return;
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restoreFromAnswerPayload(): void {
    const plan = this._plan();
    const incoming = parseMatchPairs(this._answerPayloadJson(), plan);
    const payload = buildMatchPayload(incoming, plan);
    if (payload === buildMatchPayload(this.pairs(), plan)) return;
    if (this.lastEmitted !== null && payload === this.lastEmitted) return;
    this.pairs.set(incoming);
  }

  /** Sütundaki `index`. etkin öğeye odak (pasif öğeler `step` yönünde atlanır). */
  private focusColumn(side: MatchSide, index: number, step = 1): void {
    queueMicrotask(() => {
      const buttons = Array.from(
        this.host.nativeElement.querySelectorAll<HTMLButtonElement>(`button[data-side="${side}"]`)
      );
      const enabled = buttons.map((button, i) => ({ button, i })).filter((entry) => !entry.button.disabled);
      if (enabled.length === 0) return;
      const at = Math.max(0, Math.min(index, buttons.length - 1));
      const ordered = step > 0 ? enabled : [...enabled].reverse();
      const pick = ordered.find((entry) => (step > 0 ? entry.i >= at : entry.i <= at)) ?? ordered[ordered.length - 1];
      pick.button.focus();
    });
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`shared.questionCanvas.match.${key}`, params) ?? '';
  }
}

function round(value: number): number {
  return Math.round(value * 10) / 10;
}

function cssEscape(value: string): string {
  return typeof CSS !== 'undefined' && CSS.escape ? CSS.escape(value) : value.replace(/["\\]/g, '\\$&');
}

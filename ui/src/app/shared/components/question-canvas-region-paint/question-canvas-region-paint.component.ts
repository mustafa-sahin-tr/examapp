import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  PaintCells,
  PaintColor,
  PaintFills,
  PaintRegion,
  RegionPaintFeedback,
  RegionPaintPlanV1,
  RegionPaintReason,
  RegionPaintRegionsPlanV1,
  buildRegionPaintFillsPayload,
  buildRegionPaintPayload,
  countPainted,
  emptyCells,
  groupName,
  isEmptyPaint,
  paintCss,
  parseRegionPaintAnswer,
  parseRegionPaintFills,
  parseRegionPaintFillsSolution,
  parseRegionPaintPlan,
  parseRegionPaintRegionsPlan,
  parseRegionPaintSolution,
  polygonBounds,
  pieSlicePath,
  solutionCells,
} from '../../../models/region-paint';

/** Bölge modunda çizilen bölge: SVG köşe dizisi, daire stili için elips ve kontrol işareti. */
export interface PaintRegionView {
  region: PaintRegion;
  no: number;
  /** Numaralı boyamada boyanın anahtarı grup id'si, değilse bölge id'si. */
  key: string;
  points: string;
  ellipse: { cx: number; cy: number; rx: number; ry: number };
  /** Gösterilen boya (çözüm açıkken çözüm) — palet dizini ya da null. */
  paint: number | null;
  mark: 'correct' | RegionPaintReason | null;
  /** #679 karşılaştırma: doğru boyanmamış hedef bölgenin beklenen rengi (palet dizini); değilse null. */
  target: number | null;
}
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/**
 * Issue #587: boyama oynatıcısı (bileşen E, `regionPaint`), dilim a: ızgara modu. Paletten renk seçilir, hücreye dokunmak / üzerinde
 * sürüklemek boyar; seçili renkle boyalı hücreden başlayan sürükleme siler (silgi de var). Renk başına boyanan hücre sayısı canlı
 * gösterilir (öğrenci oranı kendisi hesaplar; hedef sayı gösterilmez). Kontrol sonrası palet satırları yeşil / kırmızı; çözüm açıkken
 * hedef sayılar satır öncelikli örnek dolgu ile çizilir. Sürükleme bitince bir kez emit edilir. OnPush: tüm durum signal.
 *
 * Dilim b — bölge modu: görselin üstünde SVG çokgenler; bölgeye dokunmak seçili rengi verir, aynı renkli bölgeye dokunmak (ya da
 * silgi) boyayı kaldırır. `circle` stilinde boya bölgeyi çevreleyen elips olarak çizilir; tek renkli palette renk seçici gizlidir.
 * Kontrol sonrası yalnız boyalı bölgeler işaretlenir (doğru yeşil, fazla kırmızı, yanlış renk turuncu) ve "Bulunan · Kalan · Fazla"
 * sayacı gösterilir; çözüm açıkken hedef bölgeler kendi renkleriyle çizilir.
 *
 * Dilim c — numaralı boyama (planda `groups`): bölgeye dokunmak o numaralı (gruptaki) bütün bölgeleri boyar; altta soru numaraları
 * listesi aynı işi klavyeyle yapar ve kontrol sonrası grup başına doğru / yanlış gösterir.
 */
@Component({
  selector: 'app-question-canvas-region-paint',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-region-paint.component.html',
  styleUrl: './question-canvas-region-paint.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasRegionPaintComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseRegionPaintPlan(value));
    this._regionsPlan.set(parseRegionPaintRegionsPlan(value));
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

  @Input() set feedback(value: RegionPaintFeedback | null | undefined) {
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

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private lastEmitted: string | null = null;
  /** Süren sürüklemenin yazdığı değer (palet dizini ya da silme için null); sürükleme yoksa undefined. */
  private stroke: number | null | undefined = undefined;
  private strokeChanged = false;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<RegionPaintPlanV1 | null>(null);
  private readonly _regionsPlan = signal<RegionPaintRegionsPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<RegionPaintFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly cells = signal<PaintCells>([]);
  /** Bölge modunda öğrencinin boyası (bölge id'si → palet id'si). */
  readonly fills = signal<PaintFills>({});
  /** Seçili palet dizini; null = silgi. */
  readonly selected = signal<number | null>(0);

  readonly plan = this._plan.asReadonly();
  readonly regionsPlan = this._regionsPlan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());
  readonly feedbackState = this._feedback.asReadonly();

  /**
   * #679: çözüm açık ve öğrenci boyamış — öğrencinin boyası kalır. Izgara / daire: renk sayacı yeşil / kırmızı, yanlışta hedef sayı;
   * bölge: yanlış ya da boyanmamış hedef bölgede doğru renk kesikli çerçeve.
   */
  readonly compare = computed(() => {
    if (!this._showSolution() || !this._solutionJson()) return false;
    return this._regionsPlan() ? Object.keys(this.fills()).length > 0 : !!this._plan() && !isEmptyPaint(this.cells());
  });
  /** Izgara / daire hedefi: renk başına sayı. */
  private readonly targetCounts = computed(() => parseRegionPaintSolution(this._solutionJson(), this._plan()));

  /** Çizilen ızgara: öğrencinin boyası (karşılaştırmada da); çözüm açık ama boya yoksa örnek dolgu. */
  readonly shown = computed<PaintCells>(() => {
    const plan = this._plan();
    if (!plan) return [];
    return this._showSolution() && !this.compare() ? solutionCells(plan, this.targetCounts()) : this.cells();
  });
  readonly counts = computed(() => countPainted(this.shown(), this._plan()?.palette.length ?? 0));
  readonly total = computed(() => {
    const plan = this._plan();
    return plan ? plan.rows * plan.cols : 0;
  });
  readonly rowMarks = computed(() => new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r.correct])));
  readonly correctCount = computed(() => [...this.rowMarks().values()].filter(Boolean).length);
  readonly canReset = computed(() =>
    this._regionsPlan() ? !this.readOnly() && Object.keys(this.fills()).length > 0 : !this.readOnly() && !isEmptyPaint(this.cells())
  );

  // ---- Bölge modu ----

  /** Görselin piksel boyutu (bölge köşeleri soru görseline göreli): SVG viewBox. */
  readonly viewBox = computed(() => {
    const region = this._region();
    return region && region.width > 0 && region.height > 0 ? `0 0 ${region.width} ${region.height}` : null;
  });

  /** Gösterilen boya: çözüm açıkken çözüm, değilse öğrencinin boyası. */
  private readonly shownFills = computed<PaintFills>(() => {
    const plan = this._regionsPlan();
    if (!plan) return {};
    return this._showSolution() && !this.compare() ? parseRegionPaintFillsSolution(this._solutionJson(), plan) : this.fills();
  });
  private readonly solutionFills = computed<PaintFills>(() => parseRegionPaintFillsSolution(this._solutionJson(), this._regionsPlan()));

  /** Karşılaştırmada anahtarın (bölge / grup) durumu ve beklenen renk dizini. */
  private compareKey(key: string, grouped: boolean): { mark: PaintRegionView['mark']; target: number | null } {
    const plan = this._regionsPlan()!;
    const solution = this.solutionFills();
    const own = Object.hasOwn(this.fills(), key) ? this.fills()[key] : null;
    if (!Object.hasOwn(solution, key)) return { mark: own !== null ? 'extra' : null, target: null };
    if (own === solution[key]) return { mark: 'correct', target: null };
    const target = plan.palette.findIndex((p) => p.id === solution[key]);
    return { mark: own !== null || grouped ? 'color' : null, target: target < 0 ? null : target };
  }

  readonly regionViews = computed<PaintRegionView[]>(() => {
    const plan = this._regionsPlan();
    if (!plan) return [];
    const fills = this.shownFills();
    const index = new Map(plan.palette.map((p, i) => [p.id, i]));
    const marks = new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r]));
    const grouped = plan.groups.length > 0;
    return plan.regions.map((region, i) => {
      const b = polygonBounds(region.points);
      // Daire: bölgeyi çevreleyen elips (kenarlar biraz dışarıda kalsın).
      const pad = Math.max(4, Math.min(b.width, b.height) * 0.12);
      const key = grouped ? (region.group ?? region.id) : region.id;
      const paintId = Object.hasOwn(fills, key) ? fills[key] : null;
      const row = this._showSolution() ? undefined : marks.get(key);
      // Numaralı boyamada yanlış grup "yanlış renk" gibi işaretlenir (boyanmamış da olsa).
      const wrong: RegionPaintReason = grouped ? 'color' : (row?.reason ?? 'extra');
      const cmp = this.compare() ? this.compareKey(key, grouped) : null;
      return {
        region,
        no: i + 1,
        key,
        points: region.points.map(([x, y]) => `${x},${y}`).join(' '),
        ellipse: { cx: b.x + b.width / 2, cy: b.y + b.height / 2, rx: b.width / 2 + pad, ry: b.height / 2 + pad },
        paint: paintId === null ? null : (index.get(paintId) ?? null),
        mark: cmp ? cmp.mark : row ? (row.correct ? 'correct' : wrong) : null,
        target: cmp?.target ?? null,
      };
    });
  });

  /** Numaralı boyama: soru numaraları (grup) listesi — gösterilen boya ve kontrol işareti. */
  readonly groupViews = computed(() => {
    const plan = this._regionsPlan();
    if (!plan || plan.groups.length === 0) return [];
    const fills = this.shownFills();
    const index = new Map(plan.palette.map((p, i) => [p.id, i]));
    const marks = this.rowMarks();
    return plan.groups.map((group) => {
      const paintId = Object.hasOwn(fills, group.id) ? fills[group.id] : null;
      const cmp = this.compare() ? this.compareKey(group.id, true) : null;
      const mark = cmp ? cmp.mark === 'correct' : this._showSolution() ? undefined : marks.get(group.id);
      return {
        id: group.id,
        name: groupName(plan, group.id),
        paint: paintId === null ? null : (index.get(paintId) ?? null),
        mark: mark === undefined ? null : mark ? ('ok' as const) : ('bad' as const),
        target: cmp?.target ?? null,
      };
    });
  });

  /** Renk başına boyalı bölge sayısı. */
  readonly regionCounts = computed(() => {
    const plan = this._regionsPlan();
    const counts = Array.from({ length: plan?.palette.length ?? 0 }, () => 0);
    for (const view of this.regionViews()) if (view.paint !== null) counts[view.paint]++;
    return counts;
  });

  /** Tek renkli palette (ör. yalnız "daire içine al") renk seçici gösterilmez; dokunmak aç / kapat. */
  readonly showPalette = computed(() => (this._regionsPlan()?.palette.length ?? 0) > 1);
  readonly regionStyle = computed(() => this._regionsPlan()?.style ?? 'fill');

  regionPaintCss(paint: number | null): string | null {
    const plan = this._regionsPlan();
    return paint === null || !plan || !plan.palette[paint] ? null : paintCss(plan.palette[paint].color);
  }

  /**
   * Bölgeye dokunma: seçili renk verilir; aynı renkle boyalıysa (ya da silgide) boya kalkar. Numaralı boyamada anahtar grup id'sidir
   * (gruptaki bütün bölgeler birlikte boyanır; `regionId` olarak grup id'si de verilebilir).
   */
  toggleRegion(regionId: string): void {
    const plan = this._regionsPlan();
    if (this.readOnly() || !plan) return;
    if (plan.groups.length > 0) {
      const group = plan.groups.some((g) => g.id === regionId) ? regionId : plan.regions.find((r) => r.id === regionId)?.group;
      if (group) this.togglePaint(plan, group);
      return;
    }
    if (!plan.regions.some((r) => r.id === regionId)) return;
    this.togglePaint(plan, regionId);
  }

  private togglePaint(plan: RegionPaintRegionsPlanV1, regionId: string): void {
    const selected = plan.palette.length === 1 ? 0 : this.selected();
    const current = this.fills();
    const paint = selected === null ? null : (plan.palette[selected]?.id ?? null);
    const next: PaintFills = { ...current };
    if (paint === null || current[regionId] === paint) delete next[regionId];
    else next[regionId] = paint;
    this.fills.set(next);
    this.emit();
  }

  regionAria(view: PaintRegionView, t: (key: string, params?: Record<string, unknown>) => string): string {
    const plan = this._regionsPlan();
    const group = view.region.group && plan ? groupName(plan, view.region.group) : null;
    const name = group ? t('regions.groupNo', { n: group }) : view.region.label || t('regions.regionNo', { n: view.no });
    const entry = view.paint === null || !plan ? null : plan.palette[view.paint];
    const state = entry ? entry.label || t('colors.' + entry.color) : t('regions.unpainted');
    return view.mark ? `${name}: ${state}, ${t('regions.mark.' + view.mark)}` : `${name}: ${state}`;
  }

  /**
   * Kare boyu: dar ekranda ızgara sayfaya sığsın (sürükleme `touch-action: none` olduğundan ızgara üzerinde kaydırılamaz); 14–32 px.
   */
  cellSize(cols: number): string {
    return `clamp(14px, calc((100vw - 64px) / ${cols}), 32px)`;
  }

  groupAria(view: { name: string; paint: number | null; mark: 'ok' | 'bad' | null }, t: (key: string, params?: Record<string, unknown>) => string): string {
    const plan = this._regionsPlan();
    const entry = view.paint === null || !plan ? null : plan.palette[view.paint];
    const state = entry ? entry.label || t('colors.' + entry.color) : t('regions.unpainted');
    const mark = view.mark === 'ok' ? `, ${t('regions.mark.correct')}` : view.mark === 'bad' ? `, ${t('regions.mark.wrong')}` : '';
    return `${t('regions.groupNo', { n: view.name })}: ${state}${mark}`;
  }

  css(color: PaintColor): string {
    return paintCss(color);
  }

  cellColor(value: number | null): string | null {
    const plan = this._plan();
    return value === null || !plan || !plan.palette[value] ? null : paintCss(plan.palette[value].color);
  }

  /** Karşılaştırmada yanlış rengin hedef sayısı; değilse null. */
  entryTarget(id: string): number | null {
    return this.compare() && this.entryState(id) === 'bad' ? (this.targetCounts()[id] ?? 0) : null;
  }

  entryState(id: string): 'ok' | 'bad' | null {
    const plan = this._plan();
    if (plan && this.compare()) {
      const index = plan.palette.findIndex((p) => p.id === id);
      return this.counts()[index] === (this.targetCounts()[id] ?? 0) ? 'ok' : 'bad';
    }
    const mark = this.rowMarks().get(id);
    return mark === undefined ? null : mark ? 'ok' : 'bad';
  }

  select(index: number | null): void {
    if (this.readOnly()) return;
    this.selected.set(index);
  }

  /** Sürüklemeyi başlatır: seçili renkle boyalı hücrede silme, değilse seçili renk (silgide hep silme). */
  startStroke(event: PointerEvent, r: number, c: number): void {
    if (this.readOnly() || !this._plan()) return;
    event.preventDefault();
    const selected = this.selected();
    const current = this.cells()[r]?.[c] ?? null;
    this.stroke = selected === null || current === selected ? null : selected;
    this.strokeChanged = false;
    this.apply(r, c);
  }

  /** Sürükleme sırasında parmağın / farenin altındaki hücreyi boyar (pointer yakalaması olmadan `elementFromPoint`). */
  moveStroke(event: PointerEvent): void {
    if (this.stroke === undefined) return;
    const target = document.elementFromPoint(event.clientX, event.clientY)?.closest<HTMLElement>('[data-cell]');
    if (!target) return;
    const r = Number(target.dataset['r']);
    const c = Number(target.dataset['c']);
    if (Number.isInteger(r) && Number.isInteger(c)) this.apply(r, c);
  }

  endStroke(): void {
    if (this.stroke === undefined) return;
    this.stroke = undefined;
    if (this.strokeChanged) this.emit();
    this.strokeChanged = false;
  }

  /** Klavye: Enter / Space hücreyi seçili renkle boyar ya da (aynı renkse) siler. */
  toggle(r: number, c: number): void {
    if (this.readOnly() || !this._plan()) return;
    const selected = this.selected();
    const current = this.cells()[r]?.[c] ?? null;
    this.stroke = selected === null || current === selected ? null : selected;
    this.strokeChanged = false;
    this.apply(r, c);
    this.endStroke();
  }

  reset(): void {
    if (!this.canReset()) return;
    if (this._regionsPlan()) {
      this.fills.set({});
      this.emit();
      return;
    }
    const plan = this._plan();
    if (!plan) return;
    this.cells.set(emptyCells(plan));
    this.emit();
  }

  slicePath(index: number, slices: number): string {
    return pieSlicePath(index, slices, 100);
  }

  sliceAria(index: number, value: number | null, t: (key: string, params?: Record<string, unknown>) => string): string {
    const entry = value === null ? null : this._plan()?.palette[value];
    return `${t('pie.slice', { n: index + 1 })}: ${entry ? entry.label || t('colors.' + entry.color) : '—'}`;
  }

  cellAria(r: number, c: number, value: number | null): string {
    const plan = this._plan();
    const entry = value === null || !plan ? null : plan.palette[value];
    return `${r + 1}, ${c + 1}: ${entry ? entry.label || entry.color : '—'}`;
  }

  private apply(r: number, c: number): void {
    const value = this.stroke;
    if (value === undefined) return;
    const cells = this.cells();
    if (!cells[r] || c < 0 || c >= cells[r].length || cells[r][c] === value) return;
    const next = cells.map((row, i) => (i === r ? row.map((v, j) => (j === c ? value : v)) : row));
    this.cells.set(next);
    this.strokeChanged = true;
  }

  private emit(): void {
    const regionsPlan = this._regionsPlan();
    if (!this._plan() && !regionsPlan) return;
    const payload = regionsPlan ? buildRegionPaintFillsPayload(this.fills(), regionsPlan) : buildRegionPaintPayload(this.cells());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    const plan = this._plan();
    const regionsPlan = this._regionsPlan();
    this.cells.set(parseRegionPaintAnswer(json, plan));
    this.fills.set(parseRegionPaintFills(json, regionsPlan));
    const paletteSize = plan?.palette.length ?? regionsPlan?.palette.length ?? 0;
    if (paletteSize > 0 && (this.selected() ?? 0) >= paletteSize) this.selected.set(0);
  }
}

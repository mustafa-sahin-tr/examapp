import { computed, signal } from '@angular/core';
import { SolutionStroke } from '../../../models/solution-recording';
import { PenColor, penColorCss } from '../../utils/pen-color';
import { DrawnStroke } from '../../utils/solution-strokes';

/** Bundan yakın noktalar kaydedilmez (sayfa birimi); kaydı küçük tutar. */
const MIN_POINT_DISTANCE = 3;
/** Sunucu sınırının (`SolutionStrokeDocument.MaxPointsPerStroke`) altında kalınır. */
const MAX_POINTS_PER_STROKE = 4000;

/**
 * Issue #1226: çözüm kaydında kalem girişi. Her nokta zamanıyla tutulur (`now`: kaydın başından ms). Geri al / temizle
 * çizgiyi silmez, silindiği anı yazar: oynatıcı o ana kadar çizgiyi gösterir. Kalem görüldükten sonra dokunma çizmez
 * (avuç içi), karalama alanındaki kuralla aynı (#782).
 */
export class SolutionStrokeCapture {
  private readonly all = signal<SolutionStroke[]>([]);
  private readonly drawing = signal<SolutionStroke | null>(null);
  private activePointerId: number | null = null;
  private lastPoint: { x: number; y: number } | null = null;
  private penSeen = false;

  /** Sayfada duran (silinmemiş) çizgiler. */
  readonly visible = computed<DrawnStroke[]>(() =>
    this.all()
      .filter((stroke) => stroke.e === undefined)
      .map((stroke) => ({ d: fullPath(stroke.p), css: penColorCss(stroke.c) })),
  );
  readonly current = computed<DrawnStroke | null>(() => {
    const stroke = this.drawing();
    return stroke ? { d: fullPath(stroke.p), css: penColorCss(stroke.c) } : null;
  });
  readonly canUndo = computed(() => this.all().some((stroke) => stroke.e === undefined));

  constructor(
    private readonly now: () => number,
    private readonly pageWidth: number,
    private readonly pageHeight: number,
  ) {}

  /** Kaydedilecek çizgiler (silinenler de: oynatıcı silinme anına kadar gösterir). */
  strokes(): SolutionStroke[] {
    return this.all();
  }

  start(event: PointerEvent, color: PenColor): boolean {
    if (this.activePointerId !== null || !event.isPrimary) return false;
    if (event.pointerType === 'pen') {
      this.penSeen = true;
    } else if (event.pointerType === 'touch' && this.penSeen) {
      return false;
    }
    event.preventDefault();
    const surface = event.currentTarget as Element;
    try {
      surface.setPointerCapture?.(event.pointerId);
    } catch {
      // İşaretçi yakalanamazsa (ör. çoktan bırakılmış) çizgi yine de başlar.
    }
    this.activePointerId = event.pointerId;
    const point = this.toPoint(event, surface);
    this.lastPoint = point;
    this.drawing.set({ c: color, t: Math.round(this.now()), p: [point.x, point.y, 0] });
    return true;
  }

  extend(event: PointerEvent): void {
    const stroke = this.drawing();
    if (event.pointerId !== this.activePointerId || !stroke) return;
    event.preventDefault();
    const surface = event.currentTarget as Element;
    const events = event.getCoalescedEvents?.() ?? [];
    const points = [...stroke.p];
    const dt = Math.max(0, Math.round(this.now()) - stroke.t);
    for (const moved of events.length ? events : [event]) {
      if (points.length >= MAX_POINTS_PER_STROKE * 3) break;
      const point = this.toPoint(moved, surface);
      if (this.lastPoint && Math.hypot(point.x - this.lastPoint.x, point.y - this.lastPoint.y) < MIN_POINT_DISTANCE) {
        continue;
      }
      points.push(point.x, point.y, dt);
      this.lastPoint = point;
    }
    this.drawing.set({ ...stroke, p: points });
  }

  end(event: PointerEvent): void {
    if (event.pointerId !== this.activePointerId) return;
    this.finish();
  }

  /** Çizilmekte olan çizgiyi kapatır (kayıt duraklarken ya da biterken). */
  finish(): void {
    const stroke = this.drawing();
    this.activePointerId = null;
    this.lastPoint = null;
    this.drawing.set(null);
    if (stroke) this.all.update((all) => [...all, stroke]);
  }

  undo(): void {
    const at = Math.round(this.now());
    this.all.update((all) => {
      let index = all.length - 1;
      while (index >= 0 && all[index].e !== undefined) index--;
      return index < 0 ? all : all.map((stroke, i) => (i === index ? { ...stroke, e: Math.max(at, stroke.t) } : stroke));
    });
  }

  clear(): void {
    const at = Math.round(this.now());
    this.all.update((all) => all.map((stroke) => (stroke.e === undefined ? { ...stroke, e: Math.max(at, stroke.t) } : stroke)));
  }

  reset(): void {
    this.finish();
    this.all.set([]);
  }

  private toPoint(event: PointerEvent, surface: Element): { x: number; y: number } {
    const rect = surface.getBoundingClientRect();
    const unit = this.pageWidth / rect.width;
    return {
      x: Math.min(this.pageWidth, Math.max(0, Math.round((event.clientX - rect.left) * unit))),
      y: Math.min(this.pageHeight, Math.max(0, Math.round((event.clientY - rect.top) * unit))),
    };
  }
}

function fullPath(points: number[]): string {
  let d = `M${points[0]} ${points[1]}L${points[0]} ${points[1]}`;
  for (let i = 3; i < points.length; i += 3) d += `L${points[i]} ${points[i + 1]}`;
  return d;
}

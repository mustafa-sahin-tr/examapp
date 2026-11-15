import { signal } from '@angular/core';
import { SCRATCH_VIEW_WIDTH } from './test-solve-scratch-store';

/** Bundan yakın noktalar kaydedilmez (alan birimi); notları küçük tutar. */
const MIN_POINT_DISTANCE = 3;

/**
 * Issue #782, #1175: bir SVG yüzeyinde parmak, kalem ya da fareyle tek çizgi çizme. Koordinatlar yüzey genişliği
 * {@link SCRATCH_VIEW_WIDTH} sayılarak tutulur. Kalem görüldükten sonra dokunma çizmez (avuç içi).
 */
export class ScratchStrokeInput {
  /** Çizilmekte olan çizgi; bırakılınca {@link end} döner. */
  readonly current = signal<string | null>(null);

  private activePointerId: number | null = null;
  private lastPoint: { x: number; y: number } | null = null;
  private penSeen = false;

  /** Çizgi başladıysa true. */
  start(event: PointerEvent): boolean {
    if (this.activePointerId !== null || !event.isPrimary) return false;
    if (event.pointerType === 'pen') {
      this.penSeen = true;
    } else if (event.pointerType === 'touch' && this.penSeen) {
      return false;
    }
    event.preventDefault();
    const surface = event.currentTarget as Element;
    surface.setPointerCapture?.(event.pointerId);
    this.activePointerId = event.pointerId;
    const point = toPoint(event, surface);
    this.lastPoint = point;
    // Tek dokunuş da nokta olarak görünsün diye ilk nokta iki kez yazılır.
    this.current.set(`M${point.x} ${point.y}L${point.x} ${point.y}`);
    return true;
  }

  extend(event: PointerEvent): void {
    if (event.pointerId !== this.activePointerId) return;
    event.preventDefault();
    const surface = event.currentTarget as Element;
    const events = event.getCoalescedEvents?.() ?? [];
    let stroke = this.current() ?? '';
    for (const moved of events.length ? events : [event]) {
      const point = toPoint(moved, surface);
      if (this.lastPoint && Math.hypot(point.x - this.lastPoint.x, point.y - this.lastPoint.y) < MIN_POINT_DISTANCE) {
        continue;
      }
      stroke += `L${point.x} ${point.y}`;
      this.lastPoint = point;
    }
    this.current.set(stroke);
  }

  /** Biten çizgi; bu çizginin işaretçisi değilse null. */
  end(event: PointerEvent): string | null {
    if (event.pointerId !== this.activePointerId) return null;
    const stroke = this.current();
    this.activePointerId = null;
    this.lastPoint = null;
    this.current.set(null);
    return stroke;
  }
}

function toPoint(event: PointerEvent, surface: Element): { x: number; y: number } {
  const rect = surface.getBoundingClientRect();
  const unit = SCRATCH_VIEW_WIDTH / rect.width;
  return {
    x: Math.max(0, Math.round((event.clientX - rect.left) * unit)),
    y: Math.max(0, Math.round((event.clientY - rect.top) * unit)),
  };
}

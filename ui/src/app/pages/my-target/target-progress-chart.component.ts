import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

import { TargetProgressPoint } from '../public/score-calculator/target-gap';

const WIDTH = 640;
const HEIGHT = 220;
const PAD = { top: 16, right: 16, bottom: 12, left: 44 };
const GRID_LINES = 4;

/** Deneme puanları ve hedef çizgisi. Tek seri olduğu için lejant yok; satırlar sayfadaki tabloda da var. */
@Component({
  selector: 'app-target-progress-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './target-progress-chart.component.scss',
  template: `
    <div class="tpc" (mouseleave)="active.set(null)">
      <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" role="img" [attr.aria-label]="label()">
        @for (tick of ticks(); track tick.value) {
          <line class="tpc__grid" [attr.x1]="padLeft" [attr.x2]="width - padRight" [attr.y1]="tick.y" [attr.y2]="tick.y" />
          <text class="tpc__axis" [attr.x]="padLeft - 8" [attr.y]="tick.y + 4" text-anchor="end">{{ tick.value }}</text>
        }
        @if (targetY(); as y) {
          <line class="tpc__target" [attr.x1]="padLeft" [attr.x2]="width - padRight" [attr.y1]="y" [attr.y2]="y" data-testid="target-line" />
          <text class="tpc__target-label" [attr.x]="labelAtStart() ? padLeft + 4 : width - padRight" [attr.y]="y - 6" [attr.text-anchor]="labelAtStart() ? 'start' : 'end'">{{ targetLabel() }}</text>
        }
        @if (line(); as d) {
          <polyline class="tpc__line" [attr.points]="d" />
        }
        @for (mark of marks(); track mark.point.id; let index = $index) {
          <circle class="tpc__dot" [attr.cx]="mark.x" [attr.cy]="mark.y" r="5" data-testid="progress-point" />
          <circle
            class="tpc__hit"
            [attr.cx]="mark.x"
            [attr.cy]="mark.y"
            r="20"
            tabindex="0"
            [attr.aria-label]="mark.point.name + ': ' + mark.point.score"
            (mouseenter)="active.set(index)"
            (focus)="active.set(index)"
            (blur)="active.set(null)"
          />
        }
      </svg>
      @if (activeMark(); as mark) {
        <div class="tpc__tip" [class.tpc__tip--end]="mark.x > width * 0.75" role="status" [style.left.%]="(mark.x / width) * 100" [style.top.%]="(mark.y / height) * 100" data-testid="progress-tip">
          <strong>{{ mark.point.score }}</strong>
          <span>{{ mark.point.name }}</span>
        </div>
      }
    </div>
  `,
})
export class TargetProgressChartComponent {
  readonly points = input.required<TargetProgressPoint[]>();
  readonly target = input<number | null>(null);
  readonly targetLabel = input('');
  readonly label = input('');

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly padLeft = PAD.left;
  protected readonly padRight = PAD.right;
  protected readonly active = signal<number | null>(null);

  /** Eksen 0'dan başlamaz: puan farkları küçük, çizginin yönü asıl bilgidir. Grid 10'un katlarına oturur. */
  private readonly domain = computed(() => {
    const values = this.points().map((p) => p.score);
    const target = this.target();
    if (target !== null) values.push(target);
    if (values.length === 0) return { min: 0, max: 100 };
    const min = Math.floor((Math.min(...values) - 5) / 10) * 10;
    const max = Math.ceil((Math.max(...values) + 5) / 10) * 10;
    return { min: Math.max(0, min), max };
  });

  private y(value: number): number {
    const { min, max } = this.domain();
    return PAD.top + (1 - (value - min) / (max - min)) * (HEIGHT - PAD.top - PAD.bottom);
  }

  protected readonly ticks = computed(() => {
    const { min, max } = this.domain();
    const step = (max - min) / GRID_LINES;
    return Array.from({ length: GRID_LINES + 1 }, (_, i) => {
      const value = Math.round(min + step * i);
      return { value, y: this.y(value) };
    });
  });

  protected readonly targetY = computed(() => {
    const target = this.target();
    return target === null ? null : this.y(target);
  });

  protected readonly marks = computed(() => {
    const points = this.points();
    const inner = WIDTH - PAD.left - PAD.right - 24;
    return points.map((point, index) => ({
      point,
      x: PAD.left + 12 + (points.length === 1 ? inner / 2 : (inner * index) / (points.length - 1)),
      y: this.y(point.score),
    }));
  });

  protected readonly line = computed(() => {
    const marks = this.marks();
    return marks.length < 2 ? null : marks.map((m) => `${m.x},${m.y}`).join(' ');
  });

  /** Son nokta hedef çizgisine yakınsa etiket onun üstüne binmesin, sola geçer. */
  protected readonly labelAtStart = computed(() => {
    const y = this.targetY();
    const last = this.marks().at(-1);
    return y !== null && last !== undefined && Math.abs(last.y - y) < 24;
  });

  protected readonly activeMark = computed(() => {
    const index = this.active();
    return index === null ? null : (this.marks()[index] ?? null);
  });
}

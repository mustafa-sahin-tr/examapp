import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { SOLUTION_PAGE_HEIGHT, SOLUTION_PAGE_WIDTH } from '../../../models/solution-recording';
import { DrawnStroke } from '../../utils/solution-strokes';

/**
 * Issue #1226: çözüm sayfası — beyaz sayfa, üstte soru görseli, üstünde çizgiler. Görsel SVG'nin içindedir: çizgiler her
 * ekran boyunda görselin aynı yerine düşer. Kaydedici ve oynatıcı aynı sayfayı kullanır.
 */
@Component({
  selector: 'app-solution-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg class="page" [attr.viewBox]="'0 0 ' + width() + ' ' + height()" preserveAspectRatio="xMidYMin meet" role="img"
      [attr.aria-label]="label()">
      <rect class="page__paper" x="0" y="0" [attr.width]="width()" [attr.height]="height()" />
      @if (imageUrl(); as href) {
        <image [attr.href]="href" x="0" y="0" [attr.width]="width()" [attr.height]="height()"
          preserveAspectRatio="xMidYMin meet" />
      }
      @for (stroke of strokes(); track $index) {
        <path class="page__stroke" [attr.d]="stroke.d" [style.stroke]="stroke.css" />
      }
      @if (current(); as stroke) {
        <path class="page__stroke" [attr.d]="stroke.d" [style.stroke]="stroke.css" />
      }
    </svg>
  `,
  styles: `
    :host {
      display: block;
      // Sayfa her temada beyazdır (soru görselleri beyaz zeminlidir); varsayılan kalem koyu mürekkep.
      color: #1f2937;
    }

    .page {
      display: block;
      width: 100%;
      height: auto;
      border-radius: 8px;
    }

    .page__paper {
      fill: #fff;
    }

    .page__stroke {
      fill: none;
      stroke: currentColor;
      stroke-width: 2.5px;
      stroke-linecap: round;
      stroke-linejoin: round;
      vector-effect: non-scaling-stroke;
    }
  `,
})
export class SolutionPageComponent {
  readonly imageUrl = input<string | null>(null);
  readonly strokes = input<DrawnStroke[]>([]);
  /** Çizilmekte olan çizgi (kaydedici). */
  readonly current = input<DrawnStroke | null>(null);
  readonly width = input(SOLUTION_PAGE_WIDTH);
  readonly height = input(SOLUTION_PAGE_HEIGHT);
  readonly label = input('');
}

import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

/** #395: yazı + görsel bölümü. Metinler `landing.<section>.*` anahtarlarından, görsel içerik olarak gelir. */
@Component({
  selector: 'app-landing-spotlight',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  templateUrl: './landing-spotlight.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingSpotlightComponent {
  /** Bölüm çapası (`id`). */
  readonly anchor = input.required<string>();
  /** Çeviri öneki, ör. `teachers`. */
  readonly section = input.required<string>();
  readonly kickerIcon = input.required<string>();
  readonly itemCount = input.required<number>();
  readonly ctaUrl = input<string>('');
  readonly showFootnote = input(false);
  readonly flip = input(false);
  readonly alt = input(false);

  readonly itemNumbers = computed(() => Array.from({ length: this.itemCount() }, (_, i) => i + 1));
}

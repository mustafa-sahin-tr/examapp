import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';

/**
 * Issue #1183: şık çipindeki harf seçici — şıkkın harfini (= sırasını) seçtirir. Seçim `picked` ile sıra numarası olarak
 * döner (0 → A); taşıma ve yeniden harflendirme tuvalde (`setAnswerLetter`) yapılır.
 */
@Component({
  selector: 'app-answer-letter-picker',
  standalone: true,
  imports: [MatIconModule, MatMenuModule],
  templateUrl: './answer-letter-picker.component.html',
  styleUrl: './answer-letter-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnswerLetterPickerComponent {
  /** Sorudaki şık sayısı. */
  readonly count = input.required<number>();
  /** Bu şıkkın sırası (0 → A). */
  readonly index = input.required<number>();
  readonly disabled = input(false);
  readonly ariaLabel = input.required<string>();
  readonly picked = output<number>();

  protected readonly letters = computed(() =>
    Array.from({ length: this.count() }, (_, i) => String.fromCharCode(65 + i))
  );
}

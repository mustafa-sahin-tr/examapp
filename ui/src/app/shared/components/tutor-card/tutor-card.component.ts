import { Component, computed, input, output } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { TutorSearchResult } from '../../../models/tutor.model';

/**
 * Issue #95 — öğrenci arama sonuçlarındaki tek bir bağımsız öğretmen kartı.
 * Salt sunum: veri çekmez, tıklanınca `selected` ile teacherId yayar.
 */
@Component({
  selector: 'app-tutor-card',
  standalone: true,
  imports: [MatChipsModule, MatIconModule],
  templateUrl: './tutor-card.component.html',
  styleUrls: ['./tutor-card.component.scss'],
})
export class TutorCardComponent {
  readonly tutor = input.required<TutorSearchResult>();
  readonly selected = output<number>();

  /** Avatar görseli yok; ad-soyaddan en fazla iki baş harf üretilir. */
  readonly initials = computed(() => {
    const parts = this.tutor().fullName.trim().split(/\s+/).filter(Boolean);
    if (!parts.length) {
      return '?';
    }
    return parts
      .slice(0, 2)
      .map((p) => p.charAt(0).toLocaleUpperCase('tr-TR'))
      .join('');
  });

  onSelect(): void {
    this.selected.emit(this.tutor().teacherId);
  }
}

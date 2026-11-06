import { CommonModule } from '@angular/common';
import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-section-header',
  templateUrl: './section-header.component.html',
  styleUrls: ['./section-header.component.scss'],
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [CommonModule, MatIconModule, MatButtonModule],
})
export class SectionHeaderComponent {
  @Input() title: string = ''; // Başlık
  @Input() subtitle: string = ''; // Alt başlık
  /** #724: başlık bu bileşenle yazılan sayfada `1` verilir, sayfanın tek `h1`'i olur. */
  @Input() headingLevel: 1 | 2 = 2;
  @Input() showBackButton: boolean = false; // Geri butonu gösterme
  @Output() onBackClick = new EventEmitter<void>(); // Geri butonuna tıklama olayı

  onBack(): void {
    this.onBackClick.emit();
  }
}

import { CommonModule } from '@angular/common';
import { Component, Input, Output, EventEmitter } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-section-header',
  templateUrl: './section-header.component.html',
  styleUrls: ['./section-header.component.scss'],
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule],
})
export class SectionHeaderComponent {
  @Input() title: string = ''; // Başlık
  @Input() icon: string = ''; // Material veya özel ikon
  @Input() subtitle: string = ''; // Alt başlık
  @Input() showBackButton: boolean = false; // Geri butonu gösterme
  @Output() onBackClick = new EventEmitter<void>(); // Geri butonuna tıklama olayı

  onBack(): void {
    this.onBackClick.emit();
  }
}

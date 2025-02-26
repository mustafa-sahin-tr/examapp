import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-section-header',
  templateUrl: './section-header.component.html',
  styleUrls: ['./section-header.component.scss'],
  standalone: true,
  imports: [CommonModule]
})
export class SectionHeaderComponent {
  @Input() title: string = '';  // Başlık
  @Input() icon: string = '';  // Material veya özel ikon
}

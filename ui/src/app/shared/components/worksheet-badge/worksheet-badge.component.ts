import { NgStyle, UpperCasePipe } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-worksheet-badge',
  imports: [NgStyle, UpperCasePipe],
  templateUrl: './worksheet-badge.component.html',
  styleUrl: './worksheet-badge.component.scss'
})
export class WorksheetBadgeComponent {
  @Input() size: string = '100px'; // Default size
  @Input() color: string = 'inherit'; // Default color
  @Input() transform: string = 'scale(1)'; // Default transform
  @Input() centerLabel: string = 'center'; // Default center label
}

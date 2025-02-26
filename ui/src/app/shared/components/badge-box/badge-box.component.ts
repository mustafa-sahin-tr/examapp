import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-badge-box',
  templateUrl: './badge-box.component.html',
  styleUrls: ['./badge-box.component.scss'],
  standalone : true,
  imports: [MatIconModule, MatCardModule]
})
export class BadgeBoxComponent {
  @Input() badgeTitle: string = '';  // Rozet başlığı (örn: "Activity")
  @Input() badgeLevel: number = 1;  // Rozet seviyesi (örn: 4)
  @Input() badgeIcon: string = '';  // Rozet ikonu (örn: "activity.png")
}

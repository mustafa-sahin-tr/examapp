import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-point-card',
  templateUrl: './point-card.component.html',
  styleUrls: ['./point-card.component.scss'],
  standalone: true,
  imports: [MatCardModule, MatIconModule]
})
export class PointCardComponent {
  @Input() title: string = '';  // Kart başlığı (örn: "My Points")
  @Input() value: string | number = '';  // Kart değeri (örn: "2500")
  @Input() icon: string = '';  // Material icon (örn: "whatshot")
}

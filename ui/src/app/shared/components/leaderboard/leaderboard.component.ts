import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-leaderboard',
  templateUrl: './leaderboard.component.html',
  styleUrls: ['./leaderboard.component.scss'],
  standalone: true,
  imports: [MatCardModule, CommonModule]
})
export class LeaderboardComponent {
  @Input() leaderboardTitle: string = '';  // Başlık (örn: "Class Leaderboard")
  @Input() leaderboardData: any[] = [];  // Kullanıcı listesi
  @Input() showDetails: boolean = false; // Grup seviyesinde detayları gösterip göstermeyeceğimizi belirler
}

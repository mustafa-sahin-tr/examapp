import { NgClass, NgFor, NgIf } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatCardMdImage, MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-reward-path',
  templateUrl: './reward-path.component.html',
  styleUrls: ['./reward-path.component.css'],
  standalone: true,
  imports: [MatIconModule, MatCardModule]
})
export class RewardPathComponent implements OnInit {
  rewards = [
    { name: '1st Book', pointsRequired: 10, unlocked: true, inProgress: false, image: 'https://web-cdn.chungchy.com/Highlights_Global_V8_aws/library/public_html/resource/images/reward/step-10.png', lockedImage: 'https://web-cdn.chungchy.com/Highlights_Global_V8_aws/library/public_html/resource/images/reward/step-lock-10.png' },
    { name: 'Reading Ace', pointsRequired: 20, unlocked: true, inProgress: false, image: 'https://web-cdn.chungchy.com/Highlights_Global_V8_aws/library/public_html/resource/images/reward/step-20.png', lockedImage: 'https://web-cdn.chungchy.com/Highlights_Global_V8_aws/library/public_html/resource/images/reward/step-lock-20.png' },
    { name: 'Reading Hero', pointsRequired: 30, unlocked: false, inProgress: true, image: 'https://web-cdn.chungchy.com/Highlights_Global_V8_aws/library/public_html/resource/images/reward/step-30.png', lockedImage: 'https://web-cdn.chungchy.com/Highlights_Global_V8_aws/library/public_html/resource/images/reward/step-lock-30.png' },
    // Diğer ödüller burada sıralanabilir
  ];

  constructor() { }

  ngOnInit(): void {
  }

  onPuzzleClick(reward: any) {
    console.log('Puzzle clicked for', reward.name);
    // Puzzle tıklama işlemi burada işlenebilir
  }

  onTrophyClick(reward: any) {
    console.log('Trophy clicked for', reward.name);
    // Trophy tıklama işlemi burada işlenebilir
  }
}



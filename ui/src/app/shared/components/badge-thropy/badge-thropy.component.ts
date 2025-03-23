import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';

@Component({
  selector: 'app-badge-thropy',
  imports: [CommonModule],
  templateUrl: './badge-thropy.component.html',
  styleUrl: './badge-thropy.component.scss'
})
export class BadgeThropyComponent {
  @Input() title: string = 'Annual View Time';
  @Input() subtitle: string = 'Stay committed and celebrate your effort and passion for skilling up.';
  @Input() badges: { id: number, name: string, icon: string, time: string, description: string, progress: number, completed: string, total: string }[] = 
  [
    {
      "id": 1,
      "name": "Fast Starter",
      "icon": "achievements/disabled-dark.8b5f55.svg",
      "time": "5 min",
      "description": "You have 3 min remaining to unlock this badge.",
      "progress": 20,
      "completed": "1 min",
      "total": "5 min"
    },
    {
      "id": 2,
      "name": "Show Time",
      "icon": "achievements/disabled-dark.230c05.svg",
      "time": "30 min",
      "description": "Watch 30 minutes of content to unlock this badge.",
      "progress": 45,
      "completed": "0 min",
      "total": "30 min"
    },
    {
      "id": 3,
      "name": "Knowledge Seeker",
      "icon": "achievements/disabled-dark.c8ad09.svg",
      "time": "2 hrs",
      "description": "Seek knowledge by watching 2 hours of content.",
      "progress": 0,
      "completed": "0 min",
      "total": "2 hrs"
    },
    {
      "id": 4,
      "name": "Primetime",
      "icon": "achievements/disabled-dark.a341ac.svg",
      "time": "5 hrs",
      "description": "Watch 5 hours of content to earn this badge.",
      "progress": 0,
      "completed": "0 min",
      "total": "5 hrs"
    },
    {
      "id": 5,
      "name": "Wise Watcher",
      "icon": "achievements/disabled-dark.041736.svg",
      "time": "10 hrs",
      "description": "Earn this badge by watching 10 hours of content.",
      "progress": 0,
      "completed": "0 min",
      "total": "10 hrs"
    },
    {
      "id": 6,
      "name": "View Time Machine",
      "icon": "achievements/disabled-dark.70f2c6.svg",
      "time": "15 hrs",
      "description": "Watch 15 hours of content to travel through time.",
      "progress": 0,
      "completed": "0 min",
      "total": "15 hrs"
    },
    {
      "id": 7,
      "name": "Scholarly Pursuit",
      "icon": "achievements/disabled-dark.555600.svg",
      "time": "25 hrs",
      "description": "Become a scholar by watching 25 hours of content.",
      "progress": 0,
      "completed": "0 min",
      "total": "25 hrs"
    },
    {
      "id": 8,
      "name": "Elite Achiever",
      "icon": "achievements/disabled-dark.6fbcc8.svg",
      "time": "50 hrs",
      "description": "Watch 50 hours to achieve elite status.",
      "progress": 0,
      "completed": "0 min",
      "total": "50 hrs"
    }
  ];
  
  
  @Output() badgeSelected = new EventEmitter<number>();
  
  selectedBadge = signal<{ id: number, name: string, icon: string, time: string, description: string, progress: number, completed: string, total: string } | null>(null);

  selectBadge(badge: any) {
    this.selectedBadge.set(this.selectedBadge() === badge ? null : badge);
    this.badgeSelected.emit(badge.id);
  }
}

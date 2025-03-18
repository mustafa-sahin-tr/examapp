import { CommonModule, NgClass } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-countdown',
  standalone: true,
  templateUrl: './countdown.component.html',
  styleUrls: ['./countdown.component.scss'],
  imports: [CommonModule]
})
export class CountdownComponent {
  @Input() durationInSeconds: number = 0; // Üst component'ten gelen saniye değeri
  @Input() showLabel: boolean = true; // Üst component'ten gelen saniye değeri
  @Input() size: 'small' | 'medium' | 'large' = 'medium'; // Kullanıcıdan gelen boyut
  @Input() color: 'primary' | 'accent' | 'warn' = 'primary'; // Kullanıcıdan gelen renk
  @Input() label: string = 'Time Remaining'; // Kullanıcıdan gelen etiket
  @Input() timeUpMessage: string = 'Time is up!'; // Kullanıcıdan gelen süre doldu mesajı
  @Input() timerStatusPosition: 'top' | 'bottom' | 'left' | 'right' = 'right';
  get minutes(): number {
    return Math.floor(this.durationInSeconds / 60);
  }

  get formattedSeconds(): string {
    return (this.durationInSeconds % 60).toString().padStart(2, '0');
  }

  get isTimeUp(): boolean {
    return this.durationInSeconds <= 0;
  }
}

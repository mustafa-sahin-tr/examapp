import { CommonModule, NgClass } from '@angular/common';
import { Component, Input, signal, WritableSignal } from '@angular/core';

@Component({
  selector: 'app-countdown',
  standalone: true,
  templateUrl: './countdown.component.html',
  styleUrls: ['./countdown.component.scss'],
  imports: [CommonModule]
})
export class CountdownComponent {
  private _duration = signal(0);

  @Input()
  set durationInSeconds(value: number) {
    this._duration.set(value);
  }
  get durationInSeconds(): number {
    return this._duration();
  }

  @Input() showLabel: boolean = true; // Üst component'ten gelen saniye değeri
  @Input() size: 'small' | 'medium' | 'large' = 'medium'; // Kullanıcıdan gelen boyut
  @Input() color: 'primary' | 'accent' | 'warn' = 'primary'; // Kullanıcıdan gelen renk
  @Input() label: string = 'Time Remaining'; // Kullanıcıdan gelen etiket
  @Input() timeUpMessage: string = 'Süre Doldu!'; // Kullanıcıdan gelen süre doldu mesajı
  @Input() timerStatusPosition: 'top' | 'bottom' | 'left' | 'right' = 'right';
  @Input() totalDurationInSeconds: number = 10; // Kullanıcıdan gelen toplam süre
  @Input() showProgressBar: boolean = false; // Kullanıcıdan gelen ilerleme çubuğu durumu

  get minutes(): number {
    if(this.isTimeUp) {
      return 0;
    }
    if(this.totalDurationInSeconds > 0) {
      return Math.floor((this.totalDurationInSeconds - this.durationInSeconds) / 60);
    }
    return Math.floor(this.durationInSeconds / 60);
  }

  get formattedSeconds(): string {
    if(this.isTimeUp) {
      return '00';
    }
    if(this.totalDurationInSeconds > 0) {
      return  ((this.totalDurationInSeconds - this.durationInSeconds) % 60).toString().padStart(2, '0');
    }
    return (this.durationInSeconds % 60).toString().padStart(2, '0');
  }

  
  get progressPercentageTop(): string {
    const perc = (this.durationInSeconds / this.totalDurationInSeconds) * 100;
    return (100 - Math.min(100, Math.max(0,(perc - 75)) * 4)).toFixed(2) + '%';      
  }

  get progressPercentageRight(): string {
    const perc = (this.durationInSeconds / this.totalDurationInSeconds) * 100;
    return (100 - Math.min(100, Math.max(0,(perc - 50)) * 4)).toFixed(2) + '%';         
  }

  get progressPercentageBottom(): string {
    const perc = (this.durationInSeconds / this.totalDurationInSeconds) * 100;
    return (100 - (Math.min(100, Math.max(0,(perc - 26)) * 4))).toFixed(2) + '%';         
  }

  get progressPercentageLeft(): string {
    const perc = (this.durationInSeconds / this.totalDurationInSeconds) * 100;
    return (100 - Math.min(100, (perc) * 4)).toFixed(2) + '%';         
  }

  get borderColor(): string {
    const perc = (this.durationInSeconds / this.totalDurationInSeconds) * 100;
    if(perc > 90) {
      return 'var(--ms-colors-status-error)';
    } else if(perc > 75) {
      return 'var(--ms-colors-status-warning)';    
    } else {
      return 'var(--ms-colors-status-success)';
    }    
  }

  get isTimeUp(): boolean {
    return this.showProgressBar && this.durationInSeconds >= this.totalDurationInSeconds;
  }
}

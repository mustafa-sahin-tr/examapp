import { CommonModule } from '@angular/common';
import { Component, Input, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

@Component({
  selector: 'app-countdown',
  standalone: true,
  templateUrl: './countdown.component.html',
  styleUrls: ['./countdown.component.scss'],
  imports: [CommonModule]
})
export class CountdownComponent {
  private readonly transloco = inject(TranslocoService);
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
  /** Boş bırakılırsa sözlükten (`shared.countdown.label`) gelir. */
  @Input() label = '';
  /** Boş bırakılırsa sözlükten (`shared.countdown.timeUp`) gelir. */
  @Input() timeUpMessage = '';

  /** Şablonda gösterilen etiket: dışarıdan verilen metin öncelikli, yoksa çeviri. */
  get statusText(): string {
    if (this.isTimeUp) {
      return this.timeUpMessage || this.translate('shared.countdown.timeUp');
    }
    return this.label || this.translate('shared.countdown.label');
  }

  private translate(key: string): string {
    return this.transloco.translate<string>(key) ?? '';
  }
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

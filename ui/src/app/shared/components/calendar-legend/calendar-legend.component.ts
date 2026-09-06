import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

interface LegendItem {
  variant: string;
  icon: string;
  label: string;
}

/**
 * Takvim rozetlerinin renk/ikon anahtarı (issue #38). Rozetle aynı token'ları kullanır.
 * Mobilde yatay kaydırılabilir.
 */
@Component({
  selector: 'app-calendar-legend',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './calendar-legend.component.html',
  styleUrls: ['./calendar-legend.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CalendarLegendComponent {
  readonly items: readonly LegendItem[] = [
    { variant: 'reminder-pending', icon: 'event_available', label: 'Hatırlatma' },
    { variant: 'reminder-sent', icon: 'notifications_off', label: 'Gönderilmiş hatırlatma' },
    { variant: 'deadline-open', icon: 'flag', label: 'Teslim tarihi' },
    { variant: 'deadline-done', icon: 'check_circle', label: 'Tamamlanan atama' },
  ];
}

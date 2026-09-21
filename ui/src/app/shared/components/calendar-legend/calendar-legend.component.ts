import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

interface LegendItem {
  variant: string;
  icon: string;
  /** `shared.calendar.variant.*` altındaki çeviri anahtarı (issue #183). */
  labelKey: string;
}

/**
 * Takvim rozetlerinin renk/ikon anahtarı (issue #38). Rozetle aynı token'ları kullanır.
 * Mobilde yatay kaydırılabilir.
 */
@Component({
  selector: 'app-calendar-legend',
  standalone: true,
  imports: [MatIconModule, TranslocoDirective],
  templateUrl: './calendar-legend.component.html',
  styleUrls: ['./calendar-legend.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CalendarLegendComponent {
  readonly items: readonly LegendItem[] = [
    { variant: 'reminder-pending', icon: 'event_available', labelKey: 'reminderPending' },
    { variant: 'reminder-sent', icon: 'notifications_off', labelKey: 'reminderSent' },
    { variant: 'deadline-open', icon: 'flag', labelKey: 'deadlineOpen' },
    { variant: 'deadline-done', icon: 'check_circle', labelKey: 'deadlineDone' },
    { variant: 'program-plan', icon: 'menu_book', labelKey: 'programPlan' },
    { variant: 'program-plan-done', icon: 'check_circle', labelKey: 'programPlanDone' },
    { variant: 'booking', icon: 'cast_for_education', labelKey: 'booking' },
  ];
}

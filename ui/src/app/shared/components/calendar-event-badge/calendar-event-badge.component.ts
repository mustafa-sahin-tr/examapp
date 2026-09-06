import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { CalendarEvent } from '../../../models/calendar-event';

type BadgeVariant = 'reminder-pending' | 'reminder-sent' | 'deadline-open' | 'deadline-done';

const VARIANT_ICON: Record<BadgeVariant, string> = {
  'reminder-pending': 'event_available',
  'reminder-sent': 'notifications_off',
  'deadline-open': 'flag',
  'deadline-done': 'check_circle',
};

const VARIANT_LABEL: Record<BadgeVariant, string> = {
  'reminder-pending': 'Hatırlatma',
  'reminder-sent': 'Gönderilmiş hatırlatma',
  'deadline-open': 'Teslim tarihi',
  'deadline-done': 'Tamamlanan atama',
};

/**
 * Takvim hücresinde tek bir etkinliği gösteren rozet (issue #38).
 * `compact` true iken yalnızca renkli bir nokta + anlamlı `aria-label` render eder;
 * false iken ikon + başlık (+ varsa ders satırı). Renkler tamamen SCSS token'larından
 * gelir — türetme burada yalnızca varyant class'ı seçer.
 */
@Component({
  selector: 'app-calendar-event-badge',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './calendar-event-badge.component.html',
  styleUrls: ['./calendar-event-badge.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CalendarEventBadgeComponent {
  readonly event = input.required<CalendarEvent>();
  readonly compact = input(false);

  readonly variant = computed<BadgeVariant>(() => {
    const e = this.event();
    if (e.kind === 'reminder') {
      return e.status === 'Sent' ? 'reminder-sent' : 'reminder-pending';
    }
    return e.isCompleted ? 'deadline-done' : 'deadline-open';
  });

  readonly icon = computed(() => VARIANT_ICON[this.variant()]);

  readonly ariaLabel = computed(() => `${VARIANT_LABEL[this.variant()]}: ${this.event().worksheetTitle}`);
}

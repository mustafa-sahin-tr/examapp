import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoService } from '@jsverse/transloco';
import { CalendarEvent } from '../../../models/calendar-event';

type BadgeVariant = 'reminder-pending' | 'reminder-sent' | 'deadline-open' | 'deadline-done' | 'booking';

const VARIANT_ICON: Record<BadgeVariant, string> = {
  'reminder-pending': 'event_available',
  'reminder-sent': 'notifications_off',
  'deadline-open': 'flag',
  'deadline-done': 'check_circle',
  booking: 'cast_for_education',
};

/** Kök sözlükteki `shared.calendar.variant.*` anahtarları (takvim rozeti/göstergesi ortak kullanır). */
const VARIANT_LABEL_KEY: Record<BadgeVariant, string> = {
  'reminder-pending': 'shared.calendar.variant.reminderPending',
  'reminder-sent': 'shared.calendar.variant.reminderSent',
  'deadline-open': 'shared.calendar.variant.deadlineOpen',
  'deadline-done': 'shared.calendar.variant.deadlineDone',
  booking: 'shared.calendar.variant.booking',
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
  private readonly transloco = inject(TranslocoService);
  readonly event = input.required<CalendarEvent>();
  readonly compact = input(false);

  readonly variant = computed<BadgeVariant>(() => {
    const e = this.event();
    if (e.kind === 'booking') {
      return 'booking';
    }
    if (e.kind === 'reminder') {
      return e.status === 'Sent' ? 'reminder-sent' : 'reminder-pending';
    }
    return e.isCompleted ? 'deadline-done' : 'deadline-open';
  });

  readonly icon = computed(() => VARIANT_ICON[this.variant()]);

  readonly ariaLabel = computed(
    () =>
      `${this.transloco.translate<string>(VARIANT_LABEL_KEY[this.variant()]) ?? ''}: ${
        this.event().worksheetTitle
      }`
  );
}

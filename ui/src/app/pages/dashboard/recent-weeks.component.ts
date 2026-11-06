import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { UserActivityDay } from '../../services/badge.service';
import { LocaleService } from '../../services/locale.service';

/** Bir gün hücresi: `level` 0 (hiç) … 3 (çok) — renk yoğunluğu; `future` bugünden sonraki günler (boş çizilir). */
export interface RecentDayCell {
  key: string;
  date: Date;
  questions: number;
  level: 0 | 1 | 2 | 3;
  today: boolean;
  future: boolean;
}

/** Yerel tarihten `YYYY-MM-DD` (sunucu `dateUtc`'nin ilk 10 karakteriyle eşleşir). */
export function dayKey(date: Date): string {
  const m = `${date.getMonth() + 1}`.padStart(2, '0');
  const d = `${date.getDate()}`.padStart(2, '0');
  return `${date.getFullYear()}-${m}-${d}`;
}

export function activityLevel(questions: number): 0 | 1 | 2 | 3 {
  if (questions <= 0) {
    return 0;
  }
  return questions < 5 ? 1 : questions < 15 ? 2 : 3;
}

/**
 * Pazartesi başlayan `weeks` haftalık ızgara; son satır bugünün haftası. Öğrenci panelinde yıllık ısı haritasının yerine
 * (#835): yeni öğrenciyi aylarca boş kareyle karşılamaz, son günleri okunur boyutta gösterir.
 */
export function buildRecentWeeks(days: readonly UserActivityDay[], weeks: number, now: Date = new Date()): RecentDayCell[][] {
  const byDay = new Map<string, number>();
  for (const day of days) {
    const key = (day?.dateUtc ?? '').slice(0, 10);
    if (key) {
      byDay.set(key, (byDay.get(key) ?? 0) + (day.questionCount ?? 0));
    }
  }
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const mondayOffset = (today.getDay() + 6) % 7;
  const start = new Date(today);
  start.setDate(today.getDate() - mondayOffset - (weeks - 1) * 7);

  const rows: RecentDayCell[][] = [];
  for (let w = 0; w < weeks; w++) {
    const row: RecentDayCell[] = [];
    for (let d = 0; d < 7; d++) {
      const date = new Date(start);
      date.setDate(start.getDate() + w * 7 + d);
      const key = dayKey(date);
      const future = date.getTime() > today.getTime();
      const questions = future ? 0 : byDay.get(key) ?? 0;
      row.push({ key, date, questions, level: activityLevel(questions), today: key === dayKey(today), future });
    }
    rows.push(row);
  }
  return rows;
}

@Component({
  selector: 'app-recent-weeks',
  standalone: true,
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="rw" role="group" [attr.aria-label]="'dashboard.activity.title' | transloco">
      <div class="rw__head" aria-hidden="true">
        @for (name of weekdayNames(); track $index) {
          <span>{{ name }}</span>
        }
      </div>
      @for (row of rows(); track $index) {
        <div class="rw__row">
          @for (cell of row; track cell.key) {
            <span
              class="rw__cell"
              [attr.data-level]="cell.level"
              [class.rw__cell--today]="cell.today"
              [class.rw__cell--future]="cell.future"
              [attr.title]="cell.future ? null : ('dashboard.activity.dayCell' | transloco: { date: dateLabel(cell.date), count: cell.questions })"
              [attr.aria-label]="cell.future ? null : ('dashboard.activity.dayCell' | transloco: { date: dateLabel(cell.date), count: cell.questions })"
              [attr.aria-hidden]="cell.future ? 'true' : null"
              [attr.data-key]="cell.key"
            ></span>
          }
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .rw {
      display: grid;
      gap: 6px;
    }

    .rw__head,
    .rw__row {
      display: grid;
      grid-template-columns: repeat(7, minmax(0, 1fr));
      gap: 6px;
    }

    .rw__head span {
      font-size: 11px;
      color: var(--ms-text-weak);
      text-align: center;
    }

    .rw__cell {
      height: 26px;
      border-radius: 6px;
      background: var(--ms-surface-strong);
    }

    .rw__cell[data-level='1'] {
      background: color-mix(in srgb, var(--ms-success-border) 35%, var(--ms-surface-strong));
    }

    .rw__cell[data-level='2'] {
      background: color-mix(in srgb, var(--ms-success-border) 65%, var(--ms-surface-strong));
    }

    .rw__cell[data-level='3'] {
      background: var(--ms-success-border);
    }

    .rw__cell--today {
      outline: 2px solid var(--primary-text);
      outline-offset: 1px;
    }

    .rw__cell--future {
      background: transparent;
      border: 1px dashed var(--ms-border-weak);
    }
  `,
})
export class RecentWeeksComponent {
  private readonly localeService = inject(LocaleService);

  readonly days = input<readonly UserActivityDay[]>([]);
  readonly weeks = input(4);
  /** Testte sabit gün vermek için. */
  readonly now = input<Date | null>(null);

  readonly rows = computed(() => buildRecentWeeks(this.days(), this.weeks(), this.now() ?? new Date()));

  readonly weekdayNames = computed(() => {
    const locale = this.localeService.localeDefinition().angularLocale;
    const format = new Intl.DateTimeFormat(locale, { weekday: 'short' });
    // 2024-01-01 pazartesi.
    return Array.from({ length: 7 }, (_, i) => format.format(new Date(2024, 0, 1 + i)));
  });

  dateLabel(date: Date): string {
    const locale = this.localeService.localeDefinition().angularLocale;
    return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long' }).format(date);
  }
}

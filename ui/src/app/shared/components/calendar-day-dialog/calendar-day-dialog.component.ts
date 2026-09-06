import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { CalendarEvent } from '../../../models/calendar-event';
import { formatFullDate } from '../../utils/calendar-month.util';

/** Girdi: bir günün tarihi + o güne düşen etkinlikler. */
export interface CalendarDayDialogData {
  date: Date;
  events: CalendarEvent[];
}

type DayEventVariant = 'reminder-pending' | 'reminder-sent' | 'deadline-open' | 'deadline-done';

interface DayEventAction {
  label: string;
  icon: string;
  /** Gezinme yapar; önce dialog/bottom-sheet kapatılır. */
  run: () => void;
}

interface DayEventRow {
  /** Sıralama için etkinlik zamanı (epoch ms). */
  time: number;
  variant: DayEventVariant;
  icon: string;
  title: string;
  meta: string;
  /** Geçmiş/tamamlanmış etkinlik — soluk render, açık etiketle. */
  sunk: boolean;
  sunkLabel: string | null;
  sunkIcon: string | null;
  actions: DayEventAction[];
}

const VARIANT_ICON: Record<DayEventVariant, string> = {
  'reminder-pending': 'event_available',
  'reminder-sent': 'notifications_off',
  'deadline-open': 'flag',
  'deadline-done': 'check_circle',
};

const TIME_FMT = new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' });
const DEADLINE_FMT = new Intl.DateTimeFormat('tr-TR', {
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
});
const WEEKDAY_FMT = new Intl.DateTimeFormat('tr-TR', { weekday: 'long' });

/**
 * Bir günün etkinliklerini özet gösteren görünüm (issue #39). Hem `MatDialog`
 * hem `MatBottomSheet` içeriği olarak kullanılabilir — her iki DATA token'ı ve
 * ref'i opsiyonel inject edilir, `close()` hangisi mevcutsa onu kapatır.
 * Renkler tamamen SCSS token'larından gelir.
 */
@Component({
  selector: 'app-calendar-day-dialog',
  standalone: true,
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './calendar-day-dialog.component.html',
  styleUrls: ['./calendar-day-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CalendarDayDialogComponent {
  private readonly router = inject(Router);
  private readonly dialogRef = inject<MatDialogRef<CalendarDayDialogComponent>>(MatDialogRef, { optional: true });
  private readonly sheetRef = inject<MatBottomSheetRef<CalendarDayDialogComponent>>(MatBottomSheetRef, {
    optional: true,
  });

  private readonly data =
    inject<CalendarDayDialogData>(MAT_DIALOG_DATA, { optional: true }) ??
    inject<CalendarDayDialogData>(MAT_BOTTOM_SHEET_DATA, { optional: true }) ??
    ({ date: new Date(), events: [] } as CalendarDayDialogData);

  readonly titleId = 'calendar-day-dialog-title';

  /** Örn. "7 Eylül 2026 Pazartesi" — grid komşu ay günlerini de gösterdiği için ay/yıl dahil. */
  readonly title = `${formatFullDate(this.data.date)} ${WEEKDAY_FMT.format(this.data.date)}`;

  readonly rows: DayEventRow[] = this.data.events
    .map((ev) => this.toRow(ev))
    .sort((a, b) => {
      if (a.sunk !== b.sunk) {
        return a.sunk ? 1 : -1;
      }
      return a.time - b.time;
    });

  close(): void {
    this.dialogRef?.close();
    this.sheetRef?.dismiss();
  }

  private navigate(worksheetId: number, queryParams?: Record<string, string>): void {
    this.close();
    void this.router.navigate(['/test', worksheetId], queryParams ? { queryParams } : {});
  }

  private toRow(ev: CalendarEvent): DayEventRow {
    const at = new Date(ev.date);
    const time = at.getTime();

    if (ev.kind === 'reminder') {
      const sent = ev.status === 'Sent';
      const variant: DayEventVariant = sent ? 'reminder-sent' : 'reminder-pending';
      return {
        time,
        variant,
        icon: VARIANT_ICON[variant],
        title: ev.worksheetTitle,
        meta: this.metaLine([ev.subject, TIME_FMT.format(at), ev.teacherName]),
        sunk: sent,
        sunkLabel: sent ? 'Gönderildi' : null,
        sunkIcon: sent ? 'notifications_off' : null,
        actions: [
          { label: 'Detaya git', icon: 'open_in_new', run: () => this.navigate(ev.worksheetId) },
          {
            label: 'Hatırlatıcıyı düzenle',
            icon: 'edit',
            run: () => this.navigate(ev.worksheetId, { reminder: 'edit' }),
          },
        ],
      };
    }

    const done = ev.isCompleted === true;
    const variant: DayEventVariant = done ? 'deadline-done' : 'deadline-open';
    return {
      time,
      variant,
      icon: VARIANT_ICON[variant],
      title: ev.worksheetTitle,
      meta: this.metaLine([ev.subject, `Son tarih: ${DEADLINE_FMT.format(at)}`, ev.teacherName]),
      sunk: done,
      sunkLabel: done ? 'Tamamlandı' : null,
      sunkIcon: done ? 'check_circle' : null,
      actions: [
        done
          ? { label: 'Sonucu gör', icon: 'grading', run: () => this.navigate(ev.worksheetId) }
          : { label: 'Çözmeye başla', icon: 'play_arrow', run: () => this.navigate(ev.worksheetId) },
      ],
    };
  }

  private metaLine(parts: (string | null)[]): string {
    return parts.filter((p): p is string => !!p).join(' · ');
  }
}

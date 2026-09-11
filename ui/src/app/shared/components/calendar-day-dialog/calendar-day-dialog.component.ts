import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { CalendarEvent } from '../../../models/calendar-event';
import { UserProgram } from '../../../models/program.interfaces';
import { ProgramService } from '../../../services/program.service';
import { AuthService } from '../../../services/auth.service';
import { formatFullDate } from '../../utils/calendar-month.util';

/** Girdi: bir günün tarihi + o güne düşen etkinlikler. */
export interface CalendarDayDialogData {
  date: Date;
  events: CalendarEvent[];
}

type DayEventVariant =
  | 'reminder-pending'
  | 'reminder-sent'
  | 'deadline-open'
  | 'deadline-done'
  | 'program-plan'
  | 'program-plan-done'
  | 'booking';

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
  /** program-study-page satırları: "X/Y sayfa tamamlandı" bilgisini sonradan eklemek için. */
  programId: number | null;
}

const VARIANT_ICON: Record<DayEventVariant, string> = {
  'reminder-pending': 'event_available',
  'reminder-sent': 'notifications_off',
  'deadline-open': 'flag',
  'deadline-done': 'check_circle',
  'program-plan': 'menu_book',
  'program-plan-done': 'check_circle',
  booking: 'cast_for_education',
};

const TIME_FMT = new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' });
const DEADLINE_FMT = new Intl.DateTimeFormat('tr-TR', {
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
});
const RANGE_FMT = new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short' });
const WEEKDAY_FMT = new Intl.DateTimeFormat('tr-TR', { weekday: 'long' });

/**
 * Bir günün etkinliklerini özet gösteren görünüm (issue #39). Hem `MatDialog`
 * hem `MatBottomSheet` içeriği olarak kullanılabilir — her iki DATA token'ı ve
 * ref'i opsiyonel inject edilir, `close()` hangisi mevcutsa onu kapatır.
 * Renkler tamamen SCSS token'larından gelir.
 *
 * Program planı satırları (issue #113): satır senkron kurulur, "X/Y sayfa tamamlandı"
 * bilgisi `ProgramService.getProgramById` cevabı geldikçe `rows` signal'ı üzerinden eklenir.
 * Aynı programId için tek istek atılır; hata sessizce yutulur (meta'nın kalanı korunur).
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
  private readonly programService = inject(ProgramService);
  /** Randevu satırının hedefi role göre değişir (öğretmen gelen kutusu / öğrenci listesi). */
  private readonly isTeacher = inject(AuthService).hasRealmRole('Teacher');
  private readonly destroyRef = inject(DestroyRef);
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

  readonly rows = signal<DayEventRow[]>(
    this.data.events
      .map((ev) => this.toRow(ev))
      .sort((a, b) => {
        if (a.sunk !== b.sunk) {
          return a.sunk ? 1 : -1;
        }
        return a.time - b.time;
      }),
  );

  constructor() {
    this.loadProgramProgress();
  }

  close(): void {
    this.dialogRef?.close();
    this.sheetRef?.dismiss();
  }

  private navigate(worksheetId: number, queryParams?: Record<string, string>): void {
    this.close();
    void this.router.navigate(['/test', worksheetId], queryParams ? { queryParams } : {});
  }

  /** Öğrenci için tekil study-page rotası yok; her zaman program detayına gidilir. */
  private navigateToProgram(programId: number): void {
    this.close();
    void this.router.navigate(['/programs', programId, 'detail']);
  }

  /** Benzersiz programId'ler için ilerlemeyi çekip ilgili satırların meta'sına ekler. */
  private loadProgramProgress(): void {
    const ids = new Set<number>();
    for (const row of this.rows()) {
      if (row.programId !== null) {
        ids.add(row.programId);
      }
    }
    for (const programId of ids) {
      this.programService
        .getProgramById(programId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (program) => this.applyProgress(programId, program),
          error: () => {
            /* sessizce yut — "X/Y sayfa tamamlandı" kısmı gösterilmez */
          },
        });
    }
  }

  private applyProgress(programId: number, program: UserProgram): void {
    const completed = program.completedPageCount ?? 0;
    const total = program.totalPageCount ?? 0;
    const progress = `${completed}/${total} sayfa tamamlandı`;
    this.rows.update((rows) =>
      rows.map((row) =>
        row.programId === programId ? { ...row, meta: this.metaLine([row.meta, progress]) } : row,
      ),
    );
  }

  private toRow(ev: CalendarEvent): DayEventRow {
    const at = new Date(ev.date);
    const time = at.getTime();

    if (ev.kind === 'booking') {
      // Onaylanmış ders randevusu (issue #96) — başlık backend'den "<Ad> ile ders" olarak gelir.
      const end = ev.endDate ? new Date(ev.endDate) : null;
      const range = end ? `${TIME_FMT.format(at)} – ${TIME_FMT.format(end)}` : TIME_FMT.format(at);
      const target = this.isTeacher ? '/booking-requests' : '/my-bookings';
      return {
        time,
        variant: 'booking',
        icon: VARIANT_ICON['booking'],
        title: ev.worksheetTitle || 'Ders randevusu',
        meta: this.metaLine([range, 'Onaylı randevu']),
        sunk: time < Date.now(),
        sunkLabel: null,
        sunkIcon: null,
        actions: [
          {
            label: 'Randevularıma git',
            icon: 'event_available',
            run: () => {
              this.close();
              void this.router.navigate([target]);
            },
          },
        ],
        programId: null,
      };
    }

    if (ev.kind === 'program-study-page') {
      const done = ev.isCompleted === true;
      const variant: DayEventVariant = done ? 'program-plan-done' : 'program-plan';
      const end = ev.endDate ? new Date(ev.endDate) : null;
      const range = end ? `${RANGE_FMT.format(at)} – ${RANGE_FMT.format(end)}` : RANGE_FMT.format(at);
      const programId = ev.programId;
      return {
        time,
        variant,
        icon: VARIANT_ICON[variant],
        title: ev.studyPageTitle || 'Çalışma planı',
        meta: this.metaLine([ev.programName, range]),
        sunk: done,
        sunkLabel: done ? 'Tamamlandı' : null,
        sunkIcon: done ? 'check_circle' : null,
        actions:
          programId !== null
            ? [{ label: 'Programa git', icon: 'menu_book', run: () => this.navigateToProgram(programId) }]
            : [],
        programId,
      };
    }

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
        programId: null,
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
      programId: null,
    };
  }

  private metaLine(parts: (string | null)[]): string {
    return parts.filter((p): p is string => !!p).join(' · ');
  }
}

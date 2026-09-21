import { isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  PLATFORM_ID,
  ViewEncapsulation,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FullCalendarComponent, FullCalendarModule } from '@fullcalendar/angular';
import type { CalendarOptions, DatesSetArg, EventContentArg } from '@fullcalendar/core';
import trLocale from '@fullcalendar/core/locales/tr';
import timeGridPlugin from '@fullcalendar/timegrid';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { AvailabilitySlot } from '../../../models/booking.model';
import { activeAppLocale } from '../../utils/active-locale.util';
import { AvailabilityGridEventProps, SlotStatusClass, toGridEvents } from './availability-week-grid.util';

const TEACHER_AVAILABILITY_SCOPE = 'teacher-availability';

/** Açıklama satırında gösterilen durumlar (sıra: boş, bekleyen, onaylı). */
interface LegendItem {
  statusClass: SlotStatusClass;
  statusKey: string;
}

/**
 * Öğretmen müsaitlik slotlarının haftalık, salt okunur grid'i (issue #175).
 * FullCalendar `timeGridWeek` (30 dk hücre, Pazartesi başlangıçlı) üzerine kuruludur; başlık/gezinme
 * FullCalendar'ın kendi toolbar'ı yerine Material butonlarıyla kendi başlığımızda yapılır.
 *
 * Yalnızca tarayıcıda render edilir (SSR/prerender'da `document` gerektiren kütüphane çalıştırılmaz).
 * Olay içeriği metin olarak (`eventContent` şablonu) basılır; HTML enjekte edilmez.
 *
 * Stil `ViewEncapsulation.None` ile yazılır çünkü FullCalendar DOM'unu kendisi üretir; tüm kurallar
 * `.awg` kök sınıfına kapsanır ve renkler yalnızca proje token'larından türer.
 */
@Component({
  selector: 'app-availability-week-grid',
  standalone: true,
  imports: [FullCalendarModule, MatButtonModule, MatIconModule, MatTooltipModule, TranslocoDirective],
  providers: [provideTranslocoScope(TEACHER_AVAILABILITY_SCOPE)],
  templateUrl: './availability-week-grid.component.html',
  styleUrls: ['./availability-week-grid.component.scss'],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AvailabilityWeekGridComponent {
  readonly slots = input<AvailabilitySlot[]>([]);
  /** Yenileme sürerken grid soluklaşır ve `aria-busy` olur; mevcut olaylar yerinde kalır. */
  readonly loading = input(false);

  protected readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly calendar = viewChild(FullCalendarComponent);

  /** FullCalendar'ın yerelleştirilmiş görünüm başlığı ("15 – 21 Eylül 2026"); `datesSet` ile güncellenir. */
  protected readonly rangeTitle = signal('');

  protected readonly legend: readonly LegendItem[] = [
    { statusClass: 'is-free', statusKey: 'status.free' },
    { statusClass: 'is-pending', statusKey: 'status.pending' },
    { statusClass: 'is-approved', statusKey: 'status.approved' },
  ];

  /** Değişmeyen seçenekler bir kez kurulur; böylece FullCalendar yalnızca `events` farkını uygular. */
  private readonly baseOptions: CalendarOptions = {
    plugins: [timeGridPlugin],
    initialView: 'timeGridWeek',
    locales: [trLocale],
    locale: activeAppLocale(),
    timeZone: 'local',
    firstDay: 1,
    slotDuration: '00:30:00',
    scrollTime: '08:00:00',
    allDaySlot: false,
    headerToolbar: false,
    nowIndicator: true,
    height: '38rem',
    dayHeaderFormat: { weekday: 'short', day: 'numeric' },
    datesSet: (arg: DatesSetArg) => {
      // İlk `datesSet` render sırasında (change detection içinde) tetiklenir; sinyal yazımını ertele.
      const title = arg.view.title;
      queueMicrotask(() => this.rangeTitle.set(title));
    },
  };

  protected readonly options = computed<CalendarOptions>(() => ({
    ...this.baseOptions,
    events: toGridEvents(this.slots()),
  }));

  protected previous(): void {
    this.calendar()?.getApi().prev();
  }

  protected next(): void {
    this.calendar()?.getApi().next();
  }

  protected today(): void {
    this.calendar()?.getApi().today();
  }

  /** Tooltip / aria-label: gün · saat aralığı · durum [· öğrenci] [· geçmiş]; boş parçalar atlanır. */
  protected tooltipText(p: AvailabilityGridEventProps, status: string, student: string, past: string): string {
    return [p.dayLabel, p.timeRange, status, student, past].filter((part) => part).join(' · ');
  }

  /** `eventContent` şablonunda tipli erişim için `extendedProps` dönüşümü. */
  protected props(arg: EventContentArg): AvailabilityGridEventProps {
    return arg.event.extendedProps as AvailabilityGridEventProps;
  }
}

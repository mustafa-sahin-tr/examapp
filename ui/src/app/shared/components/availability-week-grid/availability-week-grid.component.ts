import { isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  PLATFORM_ID,
  ViewEncapsulation,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FullCalendarComponent, FullCalendarModule } from '@fullcalendar/angular';
import type { CalendarOptions, DatesSetArg, EventContentArg, EventInput } from '@fullcalendar/core';
import trLocale from '@fullcalendar/core/locales/tr';
import interactionPlugin, { DateClickArg } from '@fullcalendar/interaction';
import timeGridPlugin from '@fullcalendar/timegrid';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { AvailabilitySlot, CreateAvailabilitySlotRequest } from '../../../models/booking.model';
import { activeAppLocale, activeIntlLocale } from '../../utils/active-locale.util';
import {
  DRAFT_MAX_ADVANCE_DAYS,
  DRAFT_MAX_MINUTES,
  DraftRange,
  DraftRejection,
  applyCellClick,
  formatDraftLabel,
  nextUtcDayBoundary,
  toSlotRequest,
} from './availability-draft.util';
import { AvailabilityGridEventProps, SlotStatusClass, toGridEvents } from './availability-week-grid.util';

const TEACHER_AVAILABILITY_SCOPE = 'teacher-availability';

/** Taslağın FullCalendar olay kimliği; gerçek slot kimlikleri sayısaldır, çakışmaz. */
const DRAFT_EVENT_ID = 'awg-draft';

/** Açıklama satırında gösterilen durumlar (sıra: boş, bekleyen, onaylı). */
interface LegendItem {
  statusClass: SlotStatusClass;
  statusKey: string;
}

/**
 * Öğretmen müsaitlik slotlarının haftalık grid'i (issue #175). Varsayılan olarak salt okunurdur.
 * FullCalendar `timeGridWeek` (30 dk hücre, Pazartesi başlangıçlı) üzerine kuruludur; başlık/gezinme
 * FullCalendar'ın kendi toolbar'ı yerine Material butonlarıyla kendi başlığımızda yapılır.
 *
 * Yalnızca tarayıcıda render edilir (SSR/prerender'da `document` gerektiren kütüphane çalıştırılmaz).
 * Olay içeriği metin olarak (`eventContent` şablonu) basılır; HTML enjekte edilmez.
 *
 * Klavye erişimi (issue #208): tab durağı şablondaki `.awg__ev` elemanıdır — çevirili `aria-label` ve
 * `matTooltip` aynı elemanda durur. FullCalendar'ın `eventInteractive` seçeneği bilinçli olarak kapalıdır;
 * açılırsa olay başına iç içe iki tab durağı oluşur.
 *
 * Tıkla-seç ile aralık oluşturma (issue #176, `editable`): boş hücreye tıklama 30 dk'lık TASLAK kurar,
 * sonraki tıklamalar genişletir/daraltır (kurallar `applyCellClick`). Sürükleme yoktur; `dateClick`
 * fare ve dokunmatik dokunuşta aynı çalışır (`@fullcalendar/interaction`). Taslak arka plan olayı olarak
 * çizilir — böylece üstüne yapılan tıklama da `dateClick` üretir. Onay çubuğu Kaydet'te
 * `createRequested` yayar; API çağrısı, `saving`/`saveError` durumu ve taslağın temizlenmesi sayfanın işidir.
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
  // Escape yalnızca odak grid'in içindeyken dinlenir; sayfadaki menü/dialog'un Escape'i taslağı silmez.
  host: { '(keydown.escape)': 'onEscape($event)' },
})
export class AvailabilityWeekGridComponent {
  readonly slots = input<AvailabilitySlot[]>([]);
  /** Yenileme sürerken grid soluklaşır ve `aria-busy` olur; mevcut olaylar yerinde kalır. */
  readonly loading = input(false);

  /** Tıkla-seç ile taslak kurmayı açar; kapalıyken grid salt okunurdur. */
  readonly editable = input(false);
  /** Taslak aralık (mutlak anlar; grid yerel saatte çizer). İki yönlü: grid tıklamayla yazar, sayfa kayıt başarısında `null` yapar. */
  readonly draft = model<DraftRange | null>(null);
  /** Kayıt sürerken onay butonları ve hücre tıklamaları kapalıdır (çift gönderim olmaz). */
  readonly saving = input(false);
  /** Sunucu hatası; onay çubuğunda `role="alert"` ile gösterilir, taslak yerinde kalır. */
  readonly saveError = input<string | null>(null);
  /** Kaydet'e basıldı: taslağın `POST /booking/slots` gövdesi (tıklanan anın UTC günü + saati, bkz. `toSlotRequest`). */
  readonly createRequested = output<CreateAvailabilitySlotRequest>();

  protected readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly calendar = viewChild(FullCalendarComponent);
  private readonly scrollRegion = viewChild<ElementRef<HTMLElement>>('scrollRegion');

  /** Son tıklamanın neden uygulanmadığı (geçmiş, dolu, süre sınırı ...); geçerli tıklamada temizlenir. */
  protected readonly hint = signal<DraftRejection | null>(null);
  /** Gün sınırının (UTC 00:00) son tıklanan güne göre YEREL saati, ör. TR'de "03:00"; ipucu metninde kullanılır. */
  private readonly boundaryTime = signal('');
  /** İpucu metinlerindeki sınır değerleri. */
  protected readonly hintParams = computed(() => ({
    hours: DRAFT_MAX_MINUTES / 60,
    days: DRAFT_MAX_ADVANCE_DAYS,
    time: this.boundaryTime(),
  }));

  /** Onay çubuğu metni: "Cum 25 Eyl · 14:00 – 15:30". */
  protected readonly draftLabel = computed(() => {
    const draft = this.draft();
    return draft ? formatDraftLabel(draft) : '';
  });

  constructor() {
    // Taslak kalktığında (Vazgeç, Escape, tek hücreyi kapatma ya da sayfanın kayıt başarısında `null` yazması):
    // bayat ipucu silinir ve odak — grid'in içindeyse — kaydırma bölgesine taşınır. Effect, şablon
    // yenilenmeden ÖNCE çalışır; Kaydet/Vazgeç butonu henüz DOM'dadır, böylece odak `body`'ye düşmeden taşınır.
    let hadDraft = false;
    effect(() => {
      const hasDraft = this.draft() !== null;
      if (hadDraft && !hasDraft) {
        untracked(() => {
          this.hint.set(null);
          this.focusScrollRegionIfInside();
        });
      }
      hadDraft = hasDraft;
    });
  }

  /** FullCalendar'ın yerelleştirilmiş görünüm başlığı ("15 – 21 Eylül 2026"); `datesSet` ile güncellenir. */
  protected readonly rangeTitle = signal('');

  protected readonly legend: readonly LegendItem[] = [
    { statusClass: 'is-free', statusKey: 'status.free' },
    { statusClass: 'is-pending', statusKey: 'status.pending' },
    { statusClass: 'is-approved', statusKey: 'status.approved' },
  ];

  /** Değişmeyen seçenekler bir kez kurulur; böylece FullCalendar yalnızca `events` farkını uygular. */
  private readonly baseOptions: CalendarOptions = {
    plugins: [timeGridPlugin, interactionPlugin],
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
    // Bir kez bağlanır (seçenek nesnesi taslak değiştikçe yeniden kurulmasın); `editable`/`saving` kontrolü handler'dadır.
    dateClick: (arg: DateClickArg) => this.onDateClick(arg),
    datesSet: (arg: DatesSetArg) => {
      // İlk `datesSet` render sırasında (change detection içinde) tetiklenir; sinyal yazımını ertele.
      const title = arg.view.title;
      queueMicrotask(() => this.rangeTitle.set(title));
    },
  };

  private readonly slotEvents = computed(() => toGridEvents(this.slots()));

  protected readonly options = computed<CalendarOptions>(() => {
    const draft = this.draft();
    const events: EventInput[] = [...this.slotEvents()];
    if (draft) {
      events.push({
        id: DRAFT_EVENT_ID,
        start: draft.start,
        end: draft.end,
        display: 'background',
        classNames: ['awg-draft'],
      });
    }
    return { ...this.baseOptions, events };
  });

  /** Hücre tıklaması → taslak. Salt okunur grid'de ve kayıt sürerken yok sayılır. */
  protected onDateClick(arg: Pick<DateClickArg, 'date'>): void {
    if (!this.editable() || this.saving()) {
      return;
    }
    const current = this.draft();
    const result = applyCellClick(current, arg.date, this.slotEvents(), new Date());
    if (result.rejection === 'crossesDayBoundary') {
      const earliest = current && current.start.getTime() < arg.date.getTime() ? current.start : arg.date;
      this.boundaryTime.set(
        new Intl.DateTimeFormat(activeIntlLocale(), { hour: '2-digit', minute: '2-digit' }).format(
          nextUtcDayBoundary(earliest)
        )
      );
    }
    this.hint.set(result.rejection);
    // Host'taki Escape dinleyicisi için odak grid'in içinde olmalı (dokunuşta tarayıcı odağı taşımayabilir).
    if (!this.host.nativeElement.contains(document.activeElement)) {
      this.scrollRegion()?.nativeElement.focus({ preventScroll: true });
    }
    if (result.draft !== this.draft()) {
      this.draft.set(result.draft);
    }
  }

  protected confirm(): void {
    const draft = this.draft();
    if (!draft || this.saving()) {
      return;
    }
    this.createRequested.emit(toSlotRequest(draft));
  }

  /** Vazgeç: taslağı siler; ipucu ve odak, taslağın kalkmasını izleyen effect'te toparlanır. */
  protected cancel(): void {
    if (!this.draft() || this.saving()) {
      return;
    }
    this.draft.set(null);
  }

  /** Başka bir katman (menü, select, dialog) Escape'i zaten tükettiyse (`defaultPrevented`) taslağa dokunulmaz. */
  protected onEscape(event: Event): void {
    if (event.defaultPrevented || !this.editable() || this.saving()) {
      return;
    }
    this.cancel();
  }

  /** Odak grid'in içindeyse kaybolmasın diye kaydırma bölgesine taşınır; sayfanın başka yerindeyse dokunulmaz. */
  private focusScrollRegionIfInside(): void {
    const region = this.scrollRegion()?.nativeElement;
    if (region && this.host.nativeElement.contains(document.activeElement)) {
      region.focus({ preventScroll: true });
    }
  }

  /** `eventContent` şablonu taslak (arka plan) olayı için de çağrılır; o olayın `extendedProps`'u yoktur. */
  protected isDraftEvent(arg: EventContentArg): boolean {
    return arg.event.id === DRAFT_EVENT_ID;
  }

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

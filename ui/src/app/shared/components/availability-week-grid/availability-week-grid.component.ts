import { isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
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
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FullCalendarComponent, FullCalendarModule } from '@fullcalendar/angular';
import type { CalendarOptions, DatesSetArg, EventClickArg, EventContentArg, EventInput } from '@fullcalendar/core';
import trLocale from '@fullcalendar/core/locales/tr';
import interactionPlugin, { DateClickArg } from '@fullcalendar/interaction';
import timeGridPlugin from '@fullcalendar/timegrid';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  AvailabilitySlot,
  CreateAvailabilitySlotRequest,
  CreateRecurringRuleRequest,
} from '../../../models/booking.model';
import { activeAppLocale, activeIntlLocale } from '../../utils/active-locale.util';
import {
  DRAFT_MAX_ADVANCE_DAYS,
  DRAFT_MAX_MINUTES,
  DraftRange,
  DraftRejection,
  RecurringUntilBounds,
  applyCellClick,
  formatDraftLabel,
  nextUtcDayBoundary,
  recurringUntilBounds,
  toRecurringRuleRequest,
  toSlotRequest,
} from './availability-draft.util';
import { AvailabilityGridEvent, AvailabilityGridEventProps, SlotStatusClass, toGridEvents } from './availability-week-grid.util';

const TEACHER_AVAILABILITY_SCOPE = 'teacher-availability';

/** Taslağın FullCalendar olay kimliği; gerçek slot kimlikleri sayısaldır, çakışmaz. */
const DRAFT_EVENT_ID = 'awg-draft';

/** Randevulu slota tıklamada "silinemez" ipucunun ekranda kalma süresi. */
const LOCKED_HINT_MS = 4000;

/** Açıklama satırında gösterilen girdiler (sıra: boş, bekleyen, onaylı, tekrarlayan). */
interface LegendItem {
  /** Renk kutusunun sınıfı; `is-recurring` renk yerine ikon taşır. */
  swatchClass: SlotStatusClass | 'is-recurring';
  labelKey: string;
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
 * `matTooltip` aynı elemanda durur. FullCalendar'ın `eventInteractive` seçeneği bilinçli olarak KAPALI
 * yazılır: `eventClick` handler'ı bağlıyken FullCalendar bu seçeneği varsayılan olarak açar ve `.fc-event`'e
 * de `tabindex` ekler (olay başına iç içe iki tab durağı). `eventClick` ise `eventInteractive`'den bağımsız,
 * `.fc-event` üzerindeki delege `click` ile tetiklenir; klavye (Enter/Space) `.awg__ev`'de bizde dinlenir.
 *
 * Tıkla-seç ile aralık oluşturma (issue #176, `editable`): boş hücreye tıklama 30 dk'lık TASLAK kurar,
 * sonraki tıklamalar genişletir/daraltır (kurallar `applyCellClick`). Sürükleme yoktur; `dateClick`
 * fare ve dokunmatik dokunuşta aynı çalışır (`@fullcalendar/interaction`). Taslak arka plan olayı olarak
 * çizilir — böylece üstüne yapılan tıklama da `dateClick` üretir. Onay çubuğu Kaydet'te
 * `createRequested` yayar; API çağrısı, `saving`/`saveError` durumu ve taslağın temizlenmesi sayfanın işidir.
 *
 * Grid üzerinden silme (issue #177, `editable`): kayıtlı BOŞ slota tıklama onu seçer (`selectedSlotId`) ve
 * aynı onay çubuğu "silme modu"na geçer (aralık + "Bu aralığı sil?" + Sil/Vazgeç). Sil `deleteRequested`
 * yayar; API çağrısı, `deleting`/`deleteError` ve slotun listeden düşmesi sayfanın işidir. Randevulu slota
 * tıklama seçim kurmaz, kısa süreli "silinemez" ipucu gösterir; asıl kısıt yine sunucudadır (hata
 * `deleteError` ile çubukta görünür, seçim korunur). Taslak ve seçim aynı anda olmaz: birine geçiş diğerini siler.
 *
 * Tekrarlayan haftalık aralık (issue #179): taslak varken onay çubuğunda "Her hafta tekrarla" kutusu ve opsiyonel
 * "şu tarihe kadar" seçicisi vardır; işaretliyse Kaydet `createRequested` yerine `createRecurringRequested`
 * (`POST /booking/recurring-rules` gövdesi, bkz. `toRecurringRuleRequest`) yayar. Kutunun ve tarihin durumu grid'e
 * aittir ve taslakla birlikte sıfırlanır. Kuraldan üretilen slotlar (`recurringAvailabilityRuleId`) `is-recurring`
 * sınıfı + tekrar ikonu alır; böyle bir slot seçildiğinde silme modu iki seçenek sunar: "Sadece bu hafta"
 * (`deleteRequested`, tekil slot silme) ve "Tüm seri" (`deleteSeriesRequested` ile kural kimliği).
 *
 * Stil `ViewEncapsulation.None` ile yazılır çünkü FullCalendar DOM'unu kendisi üretir; tüm kurallar
 * `.awg` kök sınıfına kapsanır ve renkler yalnızca proje token'larından türer.
 */
@Component({
  selector: 'app-availability-week-grid',
  standalone: true,
  imports: [
    FullCalendarModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatTooltipModule,
    ReactiveFormsModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(TEACHER_AVAILABILITY_SCOPE)],
  templateUrl: './availability-week-grid.component.html',
  styleUrls: ['./availability-week-grid.component.scss'],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Escape yalnızca odak grid'in içindeyken dinlenir; sayfadaki menü/dialog'un Escape'i taslağı/seçimi silmez.
  host: { '(keydown.escape)': 'onEscape($event)' },
})
export class AvailabilityWeekGridComponent {
  readonly slots = input<AvailabilitySlot[]>([]);
  /** Yenileme sürerken grid soluklaşır ve `aria-busy` olur; mevcut olaylar yerinde kalır. */
  readonly loading = input(false);

  /** Tıkla-seç ile taslak kurmayı ve slot seçerek silmeyi açar; kapalıyken grid salt okunurdur. */
  readonly editable = input(false);
  /** Taslak aralık (mutlak anlar; grid yerel saatte çizer). İki yönlü: grid tıklamayla yazar, sayfa kayıt başarısında `null` yapar. */
  readonly draft = model<DraftRange | null>(null);
  /** Kayıt sürerken onay butonları ve hücre tıklamaları kapalıdır (çift gönderim olmaz). */
  readonly saving = input(false);
  /** Sunucu hatası; onay çubuğunda `role="alert"` ile gösterilir, taslak yerinde kalır. */
  readonly saveError = input<string | null>(null);
  /** Kaydet'e basıldı: taslağın `POST /booking/slots` gövdesi (tıklanan anın UTC günü + saati, bkz. `toSlotRequest`). */
  readonly createRequested = output<CreateAvailabilitySlotRequest>();
  /** "Her hafta tekrarla" işaretliyken Kaydet: `POST /booking/recurring-rules` gövdesi (issue #179). */
  readonly createRecurringRequested = output<CreateRecurringRuleRequest>();

  /** Silinmek üzere seçilen slot (issue #177). İki yönlü: grid tıklamayla yazar, sayfa silme başarısında `null` yapar. */
  readonly selectedSlotId = model<number | null>(null);
  /** Silme sürerken Sil/Vazgeç ve tıklamalar kapalıdır (çift gönderim olmaz). */
  readonly deleting = input(false);
  /** Silme hatası (ör. sunucunun "randevusu var" reddi); çubukta `role="alert"` ile gösterilir, seçim yerinde kalır. */
  readonly deleteError = input<string | null>(null);
  /** Sil / "Sadece bu hafta"ya basıldı: seçili slotun kimliği (`DELETE /booking/slots/{id}`). */
  readonly deleteRequested = output<number>();
  /** Tekrarlayan slotta "Tüm seri"ye basıldı: kural kimliği (`DELETE /booking/recurring-rules/{id}`, issue #179). */
  readonly deleteSeriesRequested = output<number>();

  protected readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly calendar = viewChild(FullCalendarComponent);
  private readonly scrollRegion = viewChild<ElementRef<HTMLElement>>('scrollRegion');

  /** Son tıklamanın neden uygulanmadığı (geçmiş, dolu, süre sınırı ...); geçerli tıklamada temizlenir. */
  protected readonly hint = signal<DraftRejection | null>(null);
  /** Randevulu slota tıklandı: "silinemez" ipucu; kısa süre sonra ya da sonraki tıklamada kalkar. */
  protected readonly lockedHint = signal(false);
  private lockedHintTimer: ReturnType<typeof setTimeout> | null = null;
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

  /** "Her hafta tekrarla" kutusu (issue #179); taslakla birlikte sıfırlanır. */
  protected readonly repeatWeekly = signal(false);
  /**
   * Tekrarın son günü (yerel gece yarısı); null = süresiz. Reactive kontrol: `matDatepicker`'ın
   * min/max/parse validator'ları buna bağlanır, böylece ekranda görünen ama geçersiz bir tarih hiçbir zaman
   * sessizce "süresiz" olarak gönderilmez (Kaydet kilitlenir, `mat-error` görünür).
   */
  protected readonly untilControl = new FormControl<Date | null>(null);
  /** Datepicker'daki değer (parse edilemeyen metinde null'a düşer; hata `untilStatus`'tan okunur). */
  protected readonly repeatUntil = toSignal(this.untilControl.valueChanges, { initialValue: null });
  private readonly untilStatus = toSignal(this.untilControl.statusChanges, { initialValue: this.untilControl.status });
  /** Hata, dokunulmadan da görünsün: taslak başka güne taşınınca tarih sınır dışına düşebilir. */
  protected readonly untilErrorMatcher: ErrorStateMatcher = { isErrorState: (control) => !!control && control.invalid };
  /** Datepicker sınırları: taslak günü + 7 .. + 1 yıl; taslak yokken anlamsız (null). */
  protected readonly untilBounds = computed<RecurringUntilBounds | null>(() => {
    const draft = this.draft();
    return draft ? recurringUntilBounds(draft) : null;
  });
  /** Sınırların içindeki bitiş günü; dışındaysa ya da geçersizse null. */
  private readonly effectiveUntil = computed<Date | null>(() => {
    const until = this.repeatUntil();
    const bounds = this.untilBounds();
    if (!until || !bounds || Number.isNaN(until.getTime())) {
      return null;
    }
    return until.getTime() >= bounds.min.getTime() && until.getTime() <= bounds.max.getTime() ? until : null;
  });
  /** Tarih girildi ama gönderilemez (sınır dışı / parse edilemedi): Kaydet kilitli, hata görünür. */
  protected readonly untilInvalid = computed(
    () => this.untilStatus() === 'INVALID' || (this.repeatUntil() !== null && this.effectiveUntil() === null)
  );

  private readonly slotEvents = computed(() => toGridEvents(this.slots()));

  /**
   * Seçili slotun grid olayı. Kimlik `slots`'ta artık yoksa (silindi ya da yeniden yüklemede düştü)
   * seçim yok sayılır; böylece sayfa slotu listeden çıkardığında çubuk kendiliğinden kapanır.
   */
  protected readonly selectedEvent = computed<AvailabilityGridEvent | null>(() => {
    const id = this.selectedSlotId();
    return id === null ? null : (this.slotEvents().find((ev) => ev.extendedProps.slotId === id) ?? null);
  });

  /** Silme çubuğu metni: "Cum 25 Eyl · 14:00 – 15:00". */
  protected readonly selectedLabel = computed(() => {
    const ev = this.selectedEvent();
    return ev ? formatDraftLabel({ start: ev.start, end: ev.end }) : '';
  });

  /** Çubuktaki hata: silme modunda `deleteError`, taslak modunda `saveError`; modsuz durumda hiçbiri. */
  protected readonly barError = computed<string | null>(() => {
    if (this.selectedEvent()) {
      return this.deleteError();
    }
    return this.draft() ? this.saveError() : null;
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
          this.repeatWeekly.set(false);
          this.untilControl.setValue(null);
          this.focusScrollRegionIfInside();
        });
      }
      hadDraft = hasDraft;
    });

    // Kayıt sürerken tarih girişi de kapanır (reactive kontrolde `[disabled]` bağlanmaz, kontrol üzerinden yapılır).
    effect(() => {
      const saving = this.saving();
      untracked(() => {
        if (saving && this.untilControl.enabled) {
          this.untilControl.disable();
        } else if (!saving && this.untilControl.disabled) {
          this.untilControl.enable();
        }
      });
    });

    // Seçim kalktığında (Vazgeç, Escape, silme başarısı, seçili slotun `slots`'tan düşmesi) aynı toparlama:
    // odak Sil/Vazgeç butonundan kaydırma bölgesine taşınır (buton ve slot olayı DOM'dan silinmeden önce).
    // Kimlik `slots`'ta yokken model'de kalmışsa null'a çekilir ki sayfa `selectedSlotIdChange` ile hatayı temizlesin.
    let hadSelection = false;
    effect(() => {
      const hasSelection = this.selectedEvent() !== null;
      if (hadSelection && !hasSelection) {
        untracked(() => {
          this.clearLockedHint();
          this.focusScrollRegionIfInside();
          if (this.selectedSlotId() !== null) {
            this.selectedSlotId.set(null);
          }
        });
      }
      hadSelection = hasSelection;
    });

    this.destroyRef.onDestroy(() => this.clearLockedHint());
  }

  /** FullCalendar'ın yerelleştirilmiş görünüm başlığı ("15 – 21 Eylül 2026"); `datesSet` ile güncellenir. */
  protected readonly rangeTitle = signal('');

  protected readonly legend: readonly LegendItem[] = [
    { swatchClass: 'is-free', labelKey: 'status.free' },
    { swatchClass: 'is-pending', labelKey: 'status.pending' },
    { swatchClass: 'is-approved', labelKey: 'status.approved' },
    { swatchClass: 'is-recurring', labelKey: 'grid.recurring.legend' },
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
    // `eventClick` bağlıyken FullCalendar `eventInteractive`'i varsayılan açar ve `.fc-event`'e tabindex koyar;
    // tab durağı bizim `.awg__ev`'dedir (issue #208), bu yüzden açıkça kapatılır.
    eventInteractive: false,
    // Bir kez bağlanır (seçenek nesnesi taslak değiştikçe yeniden kurulmasın); `editable`/`saving` kontrolü handler'dadır.
    dateClick: (arg: DateClickArg) => this.onDateClick(arg),
    eventClick: (arg: EventClickArg) => this.onEventClick(arg.event.id),
    datesSet: (arg: DatesSetArg) => {
      // İlk `datesSet` render sırasında (change detection içinde) tetiklenir; sinyal yazımını ertele.
      const title = arg.view.title;
      queueMicrotask(() => this.rangeTitle.set(title));
    },
  };

  protected readonly options = computed<CalendarOptions>(() => {
    const draft = this.draft();
    const selectedId = this.selectedEvent()?.id ?? null;
    const events: EventInput[] = this.slotEvents().map((ev) =>
      ev.id === selectedId ? { ...ev, classNames: [...ev.classNames, 'is-selected'] } : ev
    );
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

  /** Hücre tıklaması → taslak. Salt okunur grid'de ve kayıt/silme sürerken yok sayılır; açık seçimi kapatır. */
  protected onDateClick(arg: Pick<DateClickArg, 'date'>): void {
    if (!this.editable() || this.saving() || this.deleting()) {
      return;
    }
    this.clearLockedHint();
    if (this.selectedSlotId() !== null) {
      this.selectedSlotId.set(null);
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
    this.focusScrollRegionIfOutside();
    if (result.draft !== this.draft()) {
      this.draft.set(result.draft);
    }
  }

  /**
   * Kayıtlı slota tıklama (fare/dokunma: FullCalendar `eventClick`; klavye: `.awg__ev` Enter/Space) → seçim.
   * Taslak (arka plan) olayı da `eventClick` üretir; o yok sayılır. Randevulu slot seçilmez, ipucu gösterir.
   * Seçili slota yeniden tıklama seçimi kaldırır.
   */
  protected onEventClick(eventId: string): void {
    if (!this.editable() || this.saving() || this.deleting() || eventId === DRAFT_EVENT_ID) {
      return;
    }
    const ev = this.slotEvents().find((item) => item.id === eventId);
    if (!ev) {
      return;
    }
    this.focusScrollRegionIfOutside();
    if (ev.extendedProps.statusClass !== 'is-free') {
      // Bayat taslak ipucu ile "silinemez" ipucu aynı anda görünmesin.
      this.hint.set(null);
      this.showLockedHint();
      return;
    }
    this.clearLockedHint();
    this.hint.set(null);
    if (this.draft() !== null) {
      this.draft.set(null);
    }
    const slotId = ev.extendedProps.slotId;
    this.selectedSlotId.set(this.selectedSlotId() === slotId ? null : slotId);
  }

  /** `.awg__ev` odaktayken Enter/Space; varsayılan (Space'te kaydırma) engellenir. */
  protected onEventKey(event: Event, p: AvailabilityGridEventProps): void {
    event.preventDefault();
    this.onEventClick(String(p.slotId));
  }

  /** Kaydet: kutu işaretliyse kural isteği (bitiş günü dahil), değilse tekil slot isteği yayar. */
  protected confirm(): void {
    const draft = this.draft();
    if (!draft || this.saving()) {
      return;
    }
    if (this.repeatWeekly()) {
      if (this.untilInvalid()) {
        return;
      }
      this.createRecurringRequested.emit(toRecurringRuleRequest(draft, this.effectiveUntil()));
      return;
    }
    this.createRequested.emit(toSlotRequest(draft));
  }

  protected onRepeatWeeklyChange(checked: boolean): void {
    this.repeatWeekly.set(checked);
    if (!checked) {
      // Alan DOM'dan kalkmadan önce sıfırlanır ki datepicker'ın parse hatası da temizlensin.
      this.untilControl.setValue(null);
    }
  }

  /** Vazgeç: taslağı siler; ipucu ve odak, taslağın kalkmasını izleyen effect'te toparlanır. */
  protected cancel(): void {
    if (!this.draft() || this.saving()) {
      return;
    }
    this.draft.set(null);
  }

  /** Sil / "Sadece bu hafta": seçili slotun kimliğini yayar; seçimin temizlenmesi sayfanın (başarıda) işidir. */
  protected confirmDelete(): void {
    const ev = this.selectedEvent();
    if (!ev || this.deleting()) {
      return;
    }
    this.deleteRequested.emit(ev.extendedProps.slotId);
  }

  /** "Tüm seri": seçili tekrarlayan slotun kural kimliğini yayar; tekil slotta (kural yok) hiçbir şey yapmaz. */
  protected confirmDeleteSeries(): void {
    const ev = this.selectedEvent();
    const ruleId = ev?.extendedProps.ruleId ?? null;
    if (ruleId === null || this.deleting()) {
      return;
    }
    this.deleteSeriesRequested.emit(ruleId);
  }

  /** Silmekten vazgeç: seçimi kaldırır; odak, seçimin kalkmasını izleyen effect'te toparlanır. */
  protected cancelDelete(): void {
    if (!this.selectedEvent() || this.deleting()) {
      return;
    }
    this.selectedSlotId.set(null);
  }

  /** Başka bir katman (menü, select, dialog) Escape'i zaten tükettiyse (`defaultPrevented`) taslağa/seçime dokunulmaz. */
  protected onEscape(event: Event): void {
    if (event.defaultPrevented || !this.editable() || this.saving() || this.deleting()) {
      return;
    }
    // Bitiş günü alanında yazarken Escape yazımı iptal etmek içindir; tüm taslağı silmemeli.
    if ((event.target as HTMLElement | null)?.closest?.('.awg__repeat-until')) {
      return;
    }
    this.cancel();
    this.cancelDelete();
  }

  private showLockedHint(): void {
    this.clearLockedHint();
    this.lockedHint.set(true);
    this.lockedHintTimer = setTimeout(() => {
      this.lockedHintTimer = null;
      this.lockedHint.set(false);
    }, LOCKED_HINT_MS);
  }

  private clearLockedHint(): void {
    if (this.lockedHintTimer !== null) {
      clearTimeout(this.lockedHintTimer);
      this.lockedHintTimer = null;
    }
    if (this.lockedHint()) {
      this.lockedHint.set(false);
    }
  }

  /** Host'taki Escape dinleyicisi için odak grid'in içinde olmalı (dokunuşta tarayıcı odağı taşımayabilir). */
  private focusScrollRegionIfOutside(): void {
    if (!this.host.nativeElement.contains(document.activeElement)) {
      this.scrollRegion()?.nativeElement.focus({ preventScroll: true });
    }
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

  /** Tooltip / aria-label: gün · saat aralığı · durum [· öğrenci] [· tekrarlayan] [· geçmiş]; boş parçalar atlanır. */
  protected tooltipText(
    p: AvailabilityGridEventProps,
    status: string,
    student: string,
    recurring: string,
    past: string
  ): string {
    return [p.dayLabel, p.timeRange, status, student, recurring, past].filter((part) => part).join(' · ');
  }

  /** Düzenlenebilir grid'de boş slot silinebilir: tooltip ve `aria-description` eylem ipucunu alır. */
  protected deletable(p: AvailabilityGridEventProps): boolean {
    return this.editable() && p.statusClass === 'is-free';
  }

  /** `eventContent` şablonunda tipli erişim için `extendedProps` dönüşümü. */
  protected props(arg: EventContentArg): AvailabilityGridEventProps {
    return arg.event.extendedProps as AvailabilityGridEventProps;
  }
}

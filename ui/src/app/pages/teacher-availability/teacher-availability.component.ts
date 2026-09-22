import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, take } from 'rxjs';
import { AvailabilitySlot, CreateAvailabilitySlotRequest } from '../../models/booking.model';
import { BookingService } from '../../services/booking.service';
import { AvailabilityWeekGridComponent } from '../../shared/components/availability-week-grid/availability-week-grid.component';
import { DraftRange } from '../../shared/components/availability-week-grid/availability-draft.util';
import { isPastSlot } from '../../shared/utils/booking-format.util';

/** Listede tek satır — slotun türetilmiş gösterim alanlarıyla. */
interface SlotRow {
  slot: AvailabilitySlot;
  past: boolean;
  /** Scope'a göreli çeviri anahtarı; şablonda `t()` ile çözülür. */
  statusKey: string;
  /** " · Ayşe" gibi dile bağlı olmayan öğrenci eki; boşsa gösterilmez. */
  studentSuffix: string;
  statusClass: 'is-free' | 'is-pending' | 'is-approved';
}

/**
 * Öğretmenin müsaitlik takvimi (issue #96): haftalık grid üzerinde tıkla-seç ile yeni aralık
 * tanımlama (issue #176) ve kayıtlı aralığa tıklayıp silme (issue #177) — grid taslağı/seçimi kurar,
 * API çağrısını bu sayfa yapar. Altta mevcut aralıkların randevu durumuyla salt okunur listesi.
 * Silme yalnızca aktif randevusu olmayan aralıklarda açıktır (grid randevulu slotu seçtirmez; asıl
 * kısıt sunucudadır ve hatası grid'de gösterilir). Gelen randevu talepleri ayrı sayfada (`/booking-requests`).
 *
 * Çeviriler kendi Transloco scope'unda: `public/i18n/teacher-availability/<lang>.json` (issue #183).
 * Tarih/saat gösterimi `date` pipe'ı üzerinden aktif `LOCALE_ID`'ye bağlıdır.
 */
const TEACHER_AVAILABILITY_SCOPE = 'teacher-availability';

@Component({
  selector: 'app-teacher-availability',
  standalone: true,
  imports: [
    AvailabilityWeekGridComponent,
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(TEACHER_AVAILABILITY_SCOPE)],
  templateUrl: './teacher-availability.component.html',
  styleUrls: ['./teacher-availability.component.scss'],
})
export class TeacherAvailabilityComponent implements OnInit {
  private readonly bookingService = inject(BookingService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly slots = signal<AvailabilitySlot[]>([]);

  /** Grid'deki taslak aralık; kayıt başarısında temizlenir, hata durumunda yerinde kalır. */
  protected readonly draft = signal<DraftRange | null>(null);
  /** Son kayıt denemesinin sunucu hatası; grid'in onay çubuğunda gösterilir. */
  protected readonly saveError = signal<string | null>(null);

  /** Grid'de silinmek üzere seçili slot; silme başarısında temizlenir, hata durumunda yerinde kalır. */
  protected readonly selectedSlotId = signal<number | null>(null);
  protected readonly deleting = signal(false);
  /** Son silme denemesinin sunucu hatası (ör. "randevusu var"); grid'in onay çubuğunda gösterilir. */
  protected readonly deleteError = signal<string | null>(null);

  protected readonly rows = computed<SlotRow[]>(() =>
    [...this.slots()]
      .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())
      .map((slot) => this.toRow(slot))
  );

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.slots().length === 0);

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.bookingService
      .getAllMySlots()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => this.slots.set(res.items ?? []),
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, this.text('messages.loadFailed'))),
      });
  }

  /** Taslak değişince (genişletme/daraltma/vazgeç) eski sunucu hatası geçersizdir. */
  protected onDraftChange(draft: DraftRange | null): void {
    this.draft.set(draft);
    this.saveError.set(null);
  }

  /** Grid'de Kaydet'e basıldı. Hata snackbar yerine grid üzerinde gösterilir ki taslak düzeltilebilsin. */
  protected create(request: CreateAvailabilitySlotRequest): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    this.bookingService
      .createSlot(request)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.success === false) {
            this.saveError.set(res.message || this.text('messages.addFailed'));
            return;
          }
          this.snackBar.open(this.text('messages.added'), this.text('messages.ok'), { duration: 3000 });
          this.draft.set(null);
          // Yeni slot hemen eklenir: `load()` sürerken grid'in dolu listesi bayat kalmaz ve `load()` hata
          // verse bile slot görünür. `load()` yine de sunucudaki kesin durumu getirir.
          const created = res?.slot;
          if (created) {
            this.slots.update((items) => (items.some((s) => s.id === created.id) ? items : [...items, created]));
          }
          this.load();
        },
        error: (err: HttpErrorResponse) =>
          this.saveError.set(this.bookingService.extractError(err, this.text('messages.addFailed'))),
      });
  }

  /** Seçim değişince (başka slot, vazgeç) eski silme hatası geçersizdir. */
  protected onSelectionChange(id: number | null): void {
    this.selectedSlotId.set(id);
    this.deleteError.set(null);
  }

  /**
   * Grid'de Sil'e basıldı. Başarıda slot yerelden düşer (grid anında güncellenir, `load()` gerekmez);
   * hata snackbar yerine grid üzerinde gösterilir ki seçim korunup mesaj bağlamında okunsun.
   */
  protected deleteSlot(id: number): void {
    if (this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.deleteError.set(null);
    this.bookingService
      .deleteSlot(id)
      .pipe(
        finalize(() => this.deleting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.snackBar.open(this.text('messages.deleted'), this.text('messages.ok'), { duration: 3000 });
          this.slots.update((items) => items.filter((s) => s.id !== id));
          this.selectedSlotId.set(null);
        },
        error: (err: HttpErrorResponse) =>
          this.deleteError.set(this.bookingService.extractError(err, this.text('messages.deleteFailed'))),
      });
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string): string {
    return this.transloco.translate<string>(`${TEACHER_AVAILABILITY_SCOPE}.${key}`) ?? '';
  }

  private toRow(slot: AvailabilitySlot): SlotRow {
    const studentSuffix = slot.studentName ? ` · ${slot.studentName}` : '';
    let statusKey = 'status.free';
    let statusClass: SlotRow['statusClass'] = 'is-free';

    if (slot.isBooked) {
      if (slot.bookingStatus === 'Approved') {
        statusKey = 'status.approved';
        statusClass = 'is-approved';
      } else {
        statusKey = 'status.pending';
        statusClass = 'is-pending';
      }
    }

    return {
      slot,
      past: isPastSlot(slot.startUtc),
      statusKey,
      studentSuffix: slot.isBooked ? studentSuffix : '',
      statusClass,
    };
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${TEACHER_AVAILABILITY_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }

}

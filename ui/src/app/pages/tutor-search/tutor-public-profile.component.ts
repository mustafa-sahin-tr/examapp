import { Component, DestroyRef, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Location } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, take } from 'rxjs';
import { TutorPublicProfile } from '../../models/tutor.model';
import { ParentLinkService } from '../../services/parent-link.service';
import { TeacherService } from '../../services/teacher.service';
import {
  BookingSlotDialogChild,
  BookingSlotDialogComponent,
  BookingSlotDialogData,
  BookingSlotDialogResult,
} from '../../shared/components/booking-slot-dialog/booking-slot-dialog.component';
import { LocaleService } from '../../services/locale.service';
import {
  BOOKING_CHILD_ROUTE_PARAM,
  parseBookingChildParam,
  resolveBookingChild,
} from '../../shared/utils/booking-child-context';
import { PARENT_HOME_URL } from '../../shared/guards/parent.guard';
import { TUTOR_SEARCH_SCOPE } from './tutor-search-scope';

/**
 * Issue #95 — öğrencinin gördüğü tekil öğretmen profili (dersler, ders şekli, ücret, tam tanıtım).
 * Backend 404'ü "kayıt yok / onaylı değil" ayrımı yapmadan döner; UI de generic mesaj gösterir.
 * Issue #439: öğrencinin talebi veliye tabiyse `AwaitingParent` döner → "velinin onayına gönderildi" bildirimi. Veli modunda
 * (`/my-children/:studentId/tutors/:id`) talep çocuk adına açılır (veli onayı atlanır) → "öğretmene gönderildi".
 */
@Component({
  selector: 'app-tutor-public-profile',
  standalone: true,
  imports: [MatButtonModule, MatChipsModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(TUTOR_SEARCH_SCOPE)],
  templateUrl: './tutor-public-profile.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./tutor-public-profile.component.scss'],
})
export class TutorPublicProfileComponent implements OnInit {
  private readonly teacherService = inject(TeacherService);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly parentLinkService = inject(ParentLinkService);

  /** Route parametresi; uygulamada component input binding açık değil, paramMap'ten okunur. */
  private readonly teacherId = Number(this.route.snapshot.paramMap.get('id'));

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly notFound = signal(false);
  readonly profile = signal<TutorPublicProfile | null>(null);

  /** Issue #439: veli modu — rotadaki çocuk; öğrenci modunda null. */
  readonly childStudentId = parseBookingChildParam(this.route.snapshot.paramMap.get(BOOKING_CHILD_ROUTE_PARAM));
  readonly childLoading = signal(false);
  readonly childLoadFailed = signal(false);
  readonly child = signal<BookingSlotDialogChild | null>(null);
  /** Veli modunda talep yalnız Active bağlı çocuk doğrulanınca açılabilir. */
  readonly canBook = computed(() => this.childStudentId === null || this.child() !== null);

  readonly initials = computed(() => {
    const parts = (this.profile()?.fullName ?? '').trim().split(/\s+/).filter(Boolean);
    if (!parts.length) {
      return '?';
    }
    return parts
      .slice(0, 2)
      .map((p) => p.charAt(0).toLocaleUpperCase(this.localeService.localeDefinition().angularLocale))
      .join('');
  });

  ngOnInit(): void {
    if (this.childStudentId !== null) {
      this.loadChild();
    }
    this.load();
  }

  /** Issue #439: veli modunda çocuğun adı/bağlantısı velinin çocuk listesinden doğrulanır. */
  loadChild(): void {
    const studentId = this.childStudentId;
    if (studentId === null) return;
    this.childLoading.set(true);
    this.childLoadFailed.set(false);
    resolveBookingChild(this.parentLinkService.getMyChildren(), studentId, '')
      .pipe(
        finalize(() => this.childLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (child) => this.child.set(child),
        error: () => {
          this.child.set(null);
          this.childLoadFailed.set(true);
        },
      });
  }

  load(): void {
    if (!Number.isFinite(this.teacherId)) {
      this.loading.set(false);
      this.notFound.set(true);
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.notFound.set(false);

    this.teacherService
      .getTutorPublicProfile(this.teacherId)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (profile) => this.profile.set(profile),
        error: (err: HttpErrorResponse) => {
          this.profile.set(null);
          if (err.status === 404) {
            this.notFound.set(true);
            return;
          }
          const body = err.error as { message?: string } | null;
          // Yedek mesaj sözlükten gelir; scope henüz yüklenmemiş olabileceği için `selectTranslate`.
          this.transloco
            .selectTranslate<string>('profile.error', {}, TUTOR_SEARCH_SCOPE)
            .pipe(take(1), takeUntilDestroyed(this.destroyRef))
            .subscribe((fallback) => this.error.set(body?.message || fallback));
        },
      });
  }

  goBack(): void {
    this.location.back();
  }

  /**
   * Issue #96 — "Randevu Al": öğretmenin boş müsaitlik aralıklarını dialog'da gösterir.
   * Talep oluşturulursa kullanıcıyı "Ders Randevularım" listesine yönlendirme seçeneği sunar.
   */
  openBookingDialog(): void {
    const profile = this.profile();
    const child = this.child();
    if (!profile || !this.canBook()) {
      return;
    }

    const data: BookingSlotDialogData = { teacherId: this.teacherId, teacherName: profile.fullName, child };
    this.dialog
      .open<BookingSlotDialogComponent, BookingSlotDialogData, BookingSlotDialogResult>(BookingSlotDialogComponent, {
        data,
        width: '32rem',
        restoreFocus: true,
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        if (!result) {
          return;
        }
        if (child) {
          // Veli modu: talep doğrudan öğretmene gitti; eylem veli panelinde o çocuğu açar.
          this.notify('bookingCreatedForChild', 'bookingCreatedForChildAction', [PARENT_HOME_URL], {
            child: child.studentId,
          });
          return;
        }
        const awaitingParent = result.booking?.status === 'AwaitingParent';
        this.notify(awaitingParent ? 'bookingAwaitingParent' : 'bookingCreated', 'bookingCreatedAction', ['/my-bookings']);
      });
  }

  private notify(messageKey: string, actionKey: string, commands: string[], queryParams?: Record<string, number>): void {
    this.snackBar
      .open(
        this.transloco.translate<string>(`${TUTOR_SEARCH_SCOPE}.profile.${messageKey}`) ?? '',
        this.transloco.translate<string>(`${TUTOR_SEARCH_SCOPE}.profile.${actionKey}`) ?? '',
        { duration: 6000 }
      )
      .onAction()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void this.router.navigate(commands, queryParams ? { queryParams } : undefined));
  }
}

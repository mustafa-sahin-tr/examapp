import { Component, DestroyRef, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, take } from 'rxjs';
import { Subject } from '../../models/subject';
import { TutorSearchResult } from '../../models/tutor.model';
import { ParentLinkService } from '../../services/parent-link.service';
import { SubjectService } from '../../services/subject.service';
import { TeacherService } from '../../services/teacher.service';
import { BookingSlotDialogChild } from '../../shared/components/booking-slot-dialog/booking-slot-dialog.component';
import { TutorCardComponent } from '../../shared/components/tutor-card/tutor-card.component';
import {
  BOOKING_CHILD_ROUTE_PARAM,
  childTutorProfileCommands,
  parseBookingChildParam,
  resolveBookingChild,
} from '../../shared/utils/booking-child-context';
import { TUTOR_SEARCH_SCOPE } from './tutor-search-scope';

/**
 * Issue #95 — öğrencinin branş/ders bazlı bağımsız öğretmen araması.
 * Onay filtresi backend'de: burada ApprovalStatus'a bakılmaz, gelen liste olduğu gibi gösterilir.
 * Issue #439: `/my-children/:studentId/tutors` altında veli modunda çalışır — çocuğun adı başlıkta gösterilir, profil linkleri
 * aynı modda açılır ve talep orada çocuk adına (veli onayı atlanarak) oluşturulur.
 */
@Component({
  selector: 'app-tutor-search',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    RouterLink,
    TutorCardComponent,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(TUTOR_SEARCH_SCOPE)],
  templateUrl: './tutor-search.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./tutor-search.component.scss'],
})
export class TutorSearchComponent implements OnInit {
  private readonly teacherService = inject(TeacherService);
  private readonly subjectService = inject(SubjectService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly parentLinkService = inject(ParentLinkService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);
  private readonly transloco = inject(TranslocoService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly results = signal<TutorSearchResult[]>([]);
  readonly subjects = signal<Subject[]>([]);
  /** Ders listesi çekilemedi — mesaj şablondan çevrilir. */
  readonly subjectsFailed = signal(false);
  /** İlk arama tamamlanmadan "sonuç yok" gösterilmesin. */
  readonly searched = signal(false);

  /** Issue #439: veli modu — rotadaki çocuk (`/my-children/:studentId/tutors`); öğrenci modunda null. */
  readonly childStudentId = parseBookingChildParam(this.route.snapshot.paramMap.get(BOOKING_CHILD_ROUTE_PARAM));
  readonly childLoading = signal(false);
  readonly childLoadFailed = signal(false);
  /** Velinin Active bağlı çocuğu; bulunamazsa (bağlantı yok/kaldırılmış) null. */
  readonly child = signal<BookingSlotDialogChild | null>(null);

  readonly isEmpty = computed(
    () => this.searched() && !this.loading() && !this.error() && this.results().length === 0,
  );

  readonly filterForm = this.fb.nonNullable.group({
    subjectId: [null as number | null],
    minPrice: [null as number | null],
    maxPrice: [null as number | null],
    online: [false],
    inPerson: [false],
  });

  ngOnInit(): void {
    if (this.childStudentId !== null) {
      this.loadChild();
    }
    this.loadSubjects();
    this.search();
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

  loadSubjects(): void {
    this.subjectsFailed.set(false);
    this.subjectService
      .loadCategories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => this.subjects.set(list),
        error: () => {
          this.subjects.set([]);
          this.subjectsFailed.set(true);
        },
      });
  }

  search(): void {
    const value = this.filterForm.getRawValue();
    this.loading.set(true);
    this.error.set(null);

    this.teacherService
      .searchTutors({
        subjectId: value.subjectId,
        minPrice: value.minPrice,
        maxPrice: value.maxPrice,
        online: value.online,
        inPerson: value.inPerson,
      })
      .pipe(
        finalize(() => {
          this.loading.set(false);
          this.searched.set(true);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (list) => this.results.set(list),
        error: (err: HttpErrorResponse) => {
          this.results.set([]);
          const body = err.error as { message?: string } | null;
          // Yedek mesaj sözlükten gelir; scope henüz yüklenmemiş olabileceği için `selectTranslate`.
          this.transloco
            .selectTranslate<string>('search.error', {}, TUTOR_SEARCH_SCOPE)
            .pipe(take(1), takeUntilDestroyed(this.destroyRef))
            .subscribe((fallback) => this.error.set(body?.message || fallback));
        },
      });
  }

  resetFilters(): void {
    this.filterForm.reset({ subjectId: null, minPrice: null, maxPrice: null, online: false, inPerson: false });
    this.search();
  }

  openProfile(teacherId: number): void {
    const studentId = this.childStudentId;
    void this.router.navigate(studentId !== null ? childTutorProfileCommands(studentId, teacherId) : ['/tutors', teacherId]);
  }
}

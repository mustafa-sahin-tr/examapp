import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { finalize } from 'rxjs';
import { Subject } from '../../models/subject';
import { TutorSearchResult } from '../../models/tutor.model';
import { SubjectService } from '../../services/subject.service';
import { TeacherService } from '../../services/teacher.service';
import { TutorCardComponent } from '../../shared/components/tutor-card/tutor-card.component';

/**
 * Issue #95 — öğrencinin branş/ders bazlı bağımsız öğretmen araması.
 * Onay filtresi backend'de: burada ApprovalStatus'a bakılmaz, gelen liste olduğu gibi gösterilir.
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
    TutorCardComponent,
  ],
  templateUrl: './tutor-search.component.html',
  styleUrls: ['./tutor-search.component.scss'],
})
export class TutorSearchComponent implements OnInit {
  private readonly teacherService = inject(TeacherService);
  private readonly subjectService = inject(SubjectService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly results = signal<TutorSearchResult[]>([]);
  readonly subjects = signal<Subject[]>([]);
  readonly subjectsError = signal<string | null>(null);
  /** İlk arama tamamlanmadan "sonuç yok" gösterilmesin. */
  readonly searched = signal(false);

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
    this.loadSubjects();
    this.search();
  }

  loadSubjects(): void {
    this.subjectsError.set(null);
    this.subjectService
      .loadCategories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => this.subjects.set(list),
        error: () => {
          this.subjects.set([]);
          this.subjectsError.set('Ders listesi yüklenemedi.');
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
          this.error.set(body?.message || 'Öğretmenler aranırken bir sorun oluştu.');
        },
      });
  }

  resetFilters(): void {
    this.filterForm.reset({ subjectId: null, minPrice: null, maxPrice: null, online: false, inPerson: false });
    this.search();
  }

  openProfile(teacherId: number): void {
    this.router.navigate(['/tutors', teacherId]);
  }
}

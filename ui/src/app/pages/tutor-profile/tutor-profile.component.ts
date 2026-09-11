import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { finalize } from 'rxjs';
import { Subject } from '../../models/subject';
import { TeacherApprovalStatus, TutorProfile } from '../../models/tutor.model';
import { SubjectService } from '../../services/subject.service';
import { TeacherService } from '../../services/teacher.service';

/**
 * Issue #95 — "Özel Ders Profilim": bağımsız öğretmen verdiği dersleri, saatlik ücretini,
 * ders şeklini (online / yüz yüze, en az biri) ve kısa tanıtımını düzenler.
 *
 * Backend tutor-profile'ı bağımsız olmayan öğretmene 400, hiç Teacher kaydı olmayana 404 ile kapatır;
 * ikisi de sayfayı kırmaz, "bu özellik bağımsız öğretmenler içindir" boş durumuna düşer.
 */
@Component({
  selector: 'app-tutor-profile',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
  ],
  templateUrl: './tutor-profile.component.html',
  styleUrls: ['./tutor-profile.component.scss'],
})
export class TutorProfileComponent implements OnInit {
  private readonly teacherService = inject(TeacherService);
  private readonly subjectService = inject(SubjectService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  readonly bioMaxLength = 500;

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  /** 404/400: kullanıcı bağımsız öğretmen değil — form yerine bilgilendirme gösterilir. */
  readonly unavailable = signal<string | null>(null);
  readonly profile = signal<TutorProfile | null>(null);
  readonly subjects = signal<Subject[]>([]);
  readonly subjectsError = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    subjectIds: [[] as number[], Validators.required],
    hourlyRate: [null as number | null, [Validators.required, Validators.min(0.01)]],
    teachesOnline: [false],
    teachesInPerson: [false],
    bio: ['', Validators.maxLength(this.bioMaxLength)],
  });

  /** Form değerini signal'a taşı ki kaydet butonunun durumu computed ile türetilebilsin. */
  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  readonly selectedSubjectIds = computed(() => this.formValue().subjectIds ?? []);
  readonly bioLength = computed(() => (this.formValue().bio ?? '').length);

  readonly canSave = computed(() => {
    const value = this.formValue();
    const hasSubject = (value.subjectIds ?? []).length > 0;
    const hasMode = !!value.teachesOnline || !!value.teachesInPerson;
    const rate = value.hourlyRate;
    const validRate = rate != null && rate > 0;
    const validBio = (value.bio ?? '').length <= this.bioMaxLength;
    return hasSubject && hasMode && validRate && validBio && !this.saving();
  });

  readonly statusLabel = computed(() => {
    switch (this.profile()?.approvalStatus) {
      case TeacherApprovalStatus.Approved:
        return 'Onaylı bağımsız öğretmen';
      case TeacherApprovalStatus.Rejected:
        return 'Başvurunuz reddedildi';
      case TeacherApprovalStatus.Pending:
        return 'Onay bekliyor';
      default:
        return '';
    }
  });

  /** Durum rozetinin renk sınıfı; SCSS'te --ms-success/warning/danger token'larına bağlanır. */
  readonly statusTone = computed(() => {
    switch (this.profile()?.approvalStatus) {
      case TeacherApprovalStatus.Approved:
        return 'success';
      case TeacherApprovalStatus.Rejected:
        return 'danger';
      default:
        return 'warning';
    }
  });

  readonly isApproved = computed(() => this.profile()?.approvalStatus === TeacherApprovalStatus.Approved);

  ngOnInit(): void {
    this.loadSubjects();
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.unavailable.set(null);

    this.teacherService
      .getTutorProfile()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (profile) => this.applyProfile(profile),
        error: (err: HttpErrorResponse) => {
          this.profile.set(null);
          if (err.status === 404 || err.status === 400) {
            this.unavailable.set(
              this.extractMessage(err, 'Bu özellik bağımsız (okula bağlı olmayan) öğretmenler içindir.'),
            );
            return;
          }
          this.error.set(this.extractMessage(err, 'Özel ders profiliniz yüklenirken bir sorun oluştu.'));
        },
      });
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

  /** Seçili derslerden birini chip üzerinden kaldırır. */
  removeSubject(subjectId: number): void {
    const next = this.form.controls.subjectIds.value.filter((id) => id !== subjectId);
    this.form.controls.subjectIds.setValue(next);
  }

  subjectName(subjectId: number): string {
    return this.subjects().find((s) => s.id === subjectId)?.name ?? `Ders #${subjectId}`;
  }

  save(): void {
    if (!this.canSave()) {
      return;
    }
    const value = this.form.getRawValue();
    this.saving.set(true);

    this.teacherService
      .updateTutorProfile({
        subjectIds: value.subjectIds,
        hourlyRate: value.hourlyRate as number,
        teachesOnline: value.teachesOnline,
        teachesInPerson: value.teachesInPerson,
        bio: value.bio?.trim() ? value.bio.trim() : null,
      })
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (profile) => {
          this.applyProfile(profile);
          this.snackBar.open('Özel ders profiliniz güncellendi.', 'Kapat', { duration: 3000 });
        },
        error: (err: HttpErrorResponse) => {
          this.snackBar.open(this.extractMessage(err, 'Profil güncellenemedi.'), 'Kapat', { duration: 5000 });
        },
      });
  }

  private applyProfile(profile: TutorProfile): void {
    this.profile.set(profile);
    this.form.setValue({
      subjectIds: profile.subjects.map((s) => s.subjectId),
      hourlyRate: profile.hourlyRate,
      teachesOnline: profile.teachesOnline,
      teachesInPerson: profile.teachesInPerson,
      bio: profile.bio ?? '',
    });
  }

  private extractMessage(err: HttpErrorResponse, fallback: string): string {
    const body = err.error as { message?: string } | null;
    return body?.message || fallback;
  }
}

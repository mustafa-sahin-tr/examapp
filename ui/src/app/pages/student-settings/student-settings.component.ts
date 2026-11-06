import { Component, DestroyRef, OnInit, ChangeDetectionStrategy, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { Grade } from '../../models/student';
import { StudentProfile } from '../../models/student-profile';
import { StudentService } from '../../services/student.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { AccountSecurityCardComponent } from '../../shared/components/account-security-card/account-security-card.component';
import { StudentParentsCardComponent } from '../../shared/components/student-parents-card/student-parents-card.component';
import { UserThemeSwitcherComponent } from '../../components/user-theme-switcher/user-theme-switcher.component';

/**
 * Öğrencinin ayarları (#847; eskiden profil sayfasının sekmeleriydi): Sınıfım, Tema, Hesabım, Velilerim. Sınıf değişikliği
 * yanlışlıkla olmasın diye açık bir eylem: "Sınıfı değiştir" → seçim → ne değişeceğini söyleyen not → Kaydet / Vazgeç.
 * Metinler profil sayfasının scope'unda (`student-profile.settings.*`).
 */
@Component({
  selector: 'app-student-settings',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    TranslocoDirective,
    PageHeaderComponent,
    AccountSecurityCardComponent,
    StudentParentsCardComponent,
    UserThemeSwitcherComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideTranslocoScope('student-profile')],
  templateUrl: './student-settings.component.html',
  styleUrl: './student-settings.component.scss',
})
export class StudentSettingsComponent implements OnInit {
  private readonly studentService = inject(StudentService);
  private readonly destroyRef = inject(DestroyRef);

  readonly student = signal<StudentProfile | null>(null);
  readonly grades = signal<Grade[]>([]);
  readonly editingGrade = signal(false);
  readonly pendingGradeId = signal<number | null>(null);
  readonly savingGrade = signal(false);
  readonly gradeSaved = signal(false);
  readonly gradeError = signal(false);

  readonly gradeName = computed(() => this.grades().find((g) => g.id === this.student()?.gradeId)?.name ?? '');
  readonly canSaveGrade = computed(
    () => !this.savingGrade() && this.pendingGradeId() !== null && this.pendingGradeId() !== this.student()?.gradeId
  );

  ngOnInit(): void {
    this.studentService
      .getProfile()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((profile) => this.student.set(profile));
    this.studentService
      .loadGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((grades) => this.grades.set(grades));
  }

  startGradeChange(): void {
    this.pendingGradeId.set(this.student()?.gradeId ?? null);
    this.gradeSaved.set(false);
    this.gradeError.set(false);
    this.editingGrade.set(true);
  }

  cancelGradeChange(): void {
    this.editingGrade.set(false);
  }

  saveGrade(): void {
    const gradeId = this.pendingGradeId();
    if (!this.canSaveGrade() || gradeId === null) return;
    this.savingGrade.set(true);
    this.gradeError.set(false);
    this.studentService.updateGrade(gradeId).subscribe({
      next: () => {
        this.student.update((s) => (s ? { ...s, gradeId } : s));
        this.savingGrade.set(false);
        this.editingGrade.set(false);
        this.gradeSaved.set(true);
      },
      error: () => {
        this.savingGrade.set(false);
        this.gradeError.set(true);
      },
    });
  }
}

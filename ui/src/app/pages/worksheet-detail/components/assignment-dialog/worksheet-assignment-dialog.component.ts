import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatRadioModule } from '@angular/material/radio';
import { MatChipsModule } from '@angular/material/chips';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { WorksheetAssignmentRequest } from '../../../../models/assignment';
import { Grade, StudentLookup } from '../../../../models/student';

export interface WorksheetAssignmentDialogData {
  worksheetId: number;
  scope: 'grade' | 'student';
  grades: Grade[];
  students: StudentLookup[];
}

export interface WorksheetAssignmentDialogResult {
  request: WorksheetAssignmentRequest;
}

@Component({
  selector: 'app-worksheet-assignment-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatRadioModule,
    MatChipsModule,
    MatCheckboxModule,
  ],
  templateUrl: './worksheet-assignment-dialog.component.html',
  styleUrl: './worksheet-assignment-dialog.component.scss',
})
export class WorksheetAssignmentDialogComponent {
  private readonly dialogRef = inject(
    MatDialogRef<WorksheetAssignmentDialogComponent, WorksheetAssignmentDialogResult | undefined>
  );
  protected readonly data = inject<WorksheetAssignmentDialogData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  protected readonly grades = this.data.grades;
  protected readonly students = this.data.students;

  protected readonly form = this.fb.nonNullable.group({
    scope: [this.data.scope ?? 'grade', Validators.required],
    gradeId: [this.data.scope === 'grade' ? this.data.grades[0]?.id ?? null : null],
    studentId: [this.data.scope === 'student' ? null : null],
    startDate: [new Date(), Validators.required],
    startTime: ['09:00', [Validators.required, Validators.pattern(/^\d{2}:\d{2}$/)]],
    hasEndDate: [false],
    endDate: [null as Date | null],
    endTime: ['23:59', Validators.pattern(/^\d{2}:\d{2}$/)],
  });

  private readonly scopeSignal = toSignal(this.form.controls.scope.valueChanges, {
    initialValue: this.form.controls.scope.value,
  });

  private readonly gradeIdSignal = toSignal(this.form.controls.gradeId.valueChanges, {
    initialValue: this.form.controls.gradeId.value,
  });

  protected readonly isGradeScope = computed(() => this.scopeSignal() === 'grade');

  protected readonly filteredStudents = computed(() => {
    const gradeId = this.gradeIdSignal();
    if (!gradeId) {
      return this.students;
    }

    return this.students.filter((student) => student.gradeId === gradeId);
  });

  constructor() {
    this.form.controls.scope.valueChanges.pipe(takeUntilDestroyed()).subscribe((scope) => {
      if (scope === 'grade') {
        this.form.controls.gradeId.addValidators([Validators.required]);
        this.form.controls.studentId.clearValidators();
        this.form.controls.studentId.reset();
      } else {
        this.form.controls.studentId.addValidators([Validators.required]);
        this.form.controls.gradeId.clearValidators();
      }

      this.form.controls.gradeId.updateValueAndValidity();
      this.form.controls.studentId.updateValueAndValidity();
    });

    this.form.controls.hasEndDate.valueChanges.pipe(takeUntilDestroyed()).subscribe((hasEnd) => {
      if (hasEnd) {
        this.form.controls.endDate.addValidators([Validators.required]);
        this.form.controls.endTime.addValidators([Validators.required, Validators.pattern(/^\d{2}:\d{2}$/)]);
      } else {
        this.form.controls.endDate.clearValidators();
        this.form.controls.endTime.clearValidators();
        this.form.controls.endDate.reset();
        this.form.controls.endTime.setValue('23:59');
      }

      this.form.controls.endDate.updateValueAndValidity();
      this.form.controls.endTime.updateValueAndValidity();
    });
  }

  protected cancel(): void {
    this.dialogRef.close();
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { scope, gradeId, studentId, startDate, startTime, hasEndDate, endDate, endTime } = this.form.getRawValue();

    if (scope === 'grade' && !gradeId) {
      this.form.controls.gradeId.setErrors({ required: true });
      return;
    }

    if (scope === 'student' && !studentId) {
      this.form.controls.studentId.setErrors({ required: true });
      return;
    }

    const startAt = this.combineDateAndTime(startDate, startTime);
    const endAt = hasEndDate && endDate ? this.combineDateAndTime(endDate, endTime ?? '23:59') : null;

    const request: WorksheetAssignmentRequest = {
      worksheetId: this.data.worksheetId,
      startAt: startAt.toISOString(),
      endAt: endAt ? endAt.toISOString() : undefined,
      gradeId: scope === 'grade' ? gradeId ?? undefined : undefined,
      studentId: scope === 'student' ? studentId ?? undefined : undefined,
    };

    this.dialogRef.close({ request });
  }

  private combineDateAndTime(date: Date | null, time: string): Date {
    if (!date || !time) {
      throw new Error('Geçersiz tarih veya saat');
    }

    const [hours, minutes] = time.split(':').map(Number);
    return new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate(), hours, minutes));
  }

  protected gradeLabel(gradeId?: number | null): string | null {
    if (!gradeId) {
      return null;
    }

    const grade = this.grades.find((g) => g.id === gradeId);
    return grade?.name ?? gradeId.toString();
  }
}

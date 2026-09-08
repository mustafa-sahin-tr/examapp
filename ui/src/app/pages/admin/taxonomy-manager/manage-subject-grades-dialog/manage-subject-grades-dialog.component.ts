import { Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { AdminService } from '../../../../services/admin.service';
import { TaxonomyGrade } from '../../../../models/taxonomy';

export interface ManageSubjectGradesDialogData {
  subjectId: number;
  subjectName: string;
  grades: TaxonomyGrade[];
  /** Dersin şu an bağlı olduğu sınıf id'leri. */
  selectedGradeIds: number[];
}

/**
 * Ders ↔ Sınıf (GradeSubject) bağlantılarını yöneten dialog.
 * Kapanış değeri: `true` → en az bir bağlantı değişti, ana ekran yeniden yüklemeli.
 */
@Component({
  selector: 'app-manage-subject-grades-dialog',
  standalone: true,
  templateUrl: './manage-subject-grades-dialog.component.html',
  styleUrls: ['./manage-subject-grades-dialog.component.scss'],
  imports: [MatDialogModule, MatButtonModule, MatCheckboxModule, MatIconModule, MatProgressBarModule],
})
export class ManageSubjectGradesDialogComponent {
  private readonly admin = inject(AdminService);
  private readonly dialogRef = inject<MatDialogRef<ManageSubjectGradesDialogComponent, boolean>>(MatDialogRef);
  readonly data = inject<ManageSubjectGradesDialogData>(MAT_DIALOG_DATA);

  private readonly initial = new Set(this.data.selectedGradeIds);

  readonly checked = signal<ReadonlySet<number>>(new Set(this.data.selectedGradeIds));
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  /** Kaydet sırasında başarıyla uygulanan değişiklik sayısı (hata olsa bile parent reload etsin diye). */
  private applied = 0;

  readonly toAdd = computed(() => [...this.checked()].filter((id) => !this.initial.has(id)));
  readonly toRemove = computed(() => [...this.initial].filter((id) => !this.checked().has(id)));
  readonly hasChanges = computed(() => this.toAdd().length > 0 || this.toRemove().length > 0);

  isChecked(gradeId: number): boolean {
    return this.checked().has(gradeId);
  }

  toggle(gradeId: number, on: boolean): void {
    const next = new Set(this.checked());
    if (on) next.add(gradeId);
    else next.delete(gradeId);
    this.checked.set(next);
    this.error.set(null);
  }

  cancel(): void {
    this.dialogRef.close(this.applied > 0);
  }

  async save(): Promise<void> {
    if (!this.hasChanges()) {
      this.dialogRef.close(false);
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    const failures: string[] = [];
    const { subjectId } = this.data;

    for (const gradeId of this.toAdd()) {
      try {
        const res = await firstValueFrom(this.admin.addSubjectGrade(subjectId, gradeId));
        if (res.success) {
          this.initial.add(gradeId);
          this.applied++;
        } else {
          failures.push(res.message);
        }
      } catch (err: unknown) {
        failures.push(this.extractMessage(err));
      }
    }
    for (const gradeId of this.toRemove()) {
      try {
        const res = await firstValueFrom(this.admin.removeSubjectGrade(subjectId, gradeId));
        if (res.success) {
          this.initial.delete(gradeId);
          this.applied++;
        } else {
          failures.push(res.message);
        }
      } catch (err: unknown) {
        failures.push(this.extractMessage(err));
      }
    }

    this.saving.set(false);
    if (failures.length) {
      // Tamamlananlar initial'a işlendi; kalan fark tekrar denenebilir.
      this.checked.set(new Set(this.checked()));
      this.error.set(failures.join(' · '));
      return;
    }
    this.dialogRef.close(true);
  }

  private extractMessage(err: unknown): string {
    const e = err as { error?: { message?: string } } | null;
    return e?.error?.message ?? 'İşlem başarısız';
  }
}

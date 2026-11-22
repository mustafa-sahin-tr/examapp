import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { SchoolAdminService, SchoolAdminStudent } from '../../services/school-admin.service';
import { SchoolSection, SchoolSectionAssignResult, SchoolSectionService } from '../../services/school-section.service';
import { backendMessage } from '../../shared/utils/admin-action-error.util';

/** Sunucunun tek istekte kabul ettiği en çok öğrenci (`SchoolSectionService.MaxAssignBatch`). */
export const ASSIGN_BATCH_MAX = 100;

/**
 * Issue #1279: şubeye öğrenci ekleme — şubenin sınıfındaki şubesiz öğrenciler (`school-admin/students?unassigned`).
 * Başka şubedekiler buradan değil, öğrenci satırındaki "Şubeyi değiştir" ile taşınır.
 */
@Component({
  selector: 'app-add-students-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatCheckboxModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope('school-sections')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'school-sections'">
      <h2 mat-dialog-title>{{ t('add.title', { section: data.gradeName + ' / ' + data.name }) }}</h2>
      <mat-dialog-content class="as">
        @if (loading()) {
          <div class="as__state"><mat-spinner diameter="28" /></div>
        } @else if (students().length === 0) {
          <p class="as__state">{{ t('add.empty') }}</p>
        } @else {
          <mat-checkbox [checked]="allSelected()" [indeterminate]="someSelected()" (change)="toggleAll($event.checked)" data-testid="select-all">
            {{ t('add.selectAll', { count: students().length }) }}
          </mat-checkbox>
          <ul class="as__list">
            @for (s of students(); track s.studentId) {
              <li>
                <mat-checkbox [checked]="selected().has(s.studentId)" (change)="toggle(s.studentId, $event.checked)">
                  {{ s.fullName || t('unknown') }} <span class="as__num">{{ s.studentNumber }}</span>
                </mat-checkbox>
              </li>
            }
          </ul>
          @if (total() > students().length) {
            <p class="as__hint">{{ t('add.more', { shown: students().length, total: total() }) }}</p>
          }
        }
        @if (error()) {
          <p class="as__error" role="alert">{{ error() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button mat-dialog-close type="button">{{ t('form.cancel') }}</button>
        <button mat-flat-button color="primary" type="button" [disabled]="selected().size === 0 || busy()" (click)="submit()" data-testid="add-save">
          {{ t('add.submit', { count: selected().size }) }}
        </button>
      </mat-dialog-actions>
    </ng-container>
  `,
  styles: `
    .as { min-width: min(420px, 80vw); }
    .as__list { list-style: none; margin: 0.5rem 0 0; padding: 0; max-height: 50vh; overflow-y: auto; }
    .as__num { opacity: 0.6; margin-left: 0.5rem; font-size: 0.85em; }
    .as__state { display: flex; justify-content: center; padding: 1.5rem 0; opacity: 0.8; }
    .as__hint { opacity: 0.7; font-size: 0.85rem; }
    .as__error { color: var(--ms-danger-background); }
  `,
})
export class AddStudentsDialogComponent implements OnInit {
  readonly data = inject<SchoolSection>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<AddStudentsDialogComponent, SchoolSectionAssignResult>>(MatDialogRef);
  private readonly schoolAdmin = inject(SchoolAdminService);
  private readonly sections = inject(SchoolSectionService);

  protected readonly students = signal<SchoolAdminStudent[]>([]);
  protected readonly total = signal(0);
  protected readonly selected = signal<Set<number>>(new Set());
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly allSelected = computed(() => this.students().length > 0 && this.selected().size === this.students().length);
  protected readonly someSelected = computed(() => this.selected().size > 0 && !this.allSelected());

  ngOnInit(): void {
    this.schoolAdmin.getStudents(1, ASSIGN_BATCH_MAX, this.data.gradeId, { unassigned: true }).subscribe({
      next: (page) => {
        this.students.set(page?.items ?? []);
        this.total.set(page?.totalCount ?? 0);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(backendMessage(err) ?? err.message);
      },
    });
  }

  protected toggle(id: number, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.selected.set(next);
  }

  protected toggleAll(checked: boolean): void {
    this.selected.set(checked ? new Set(this.students().map((s) => s.studentId)) : new Set());
  }

  protected submit(): void {
    if (this.selected().size === 0 || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.sections.assign(this.data.id, [...this.selected()]).subscribe({
      next: (result) => this.ref.close(result),
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(backendMessage(err) ?? err.message);
      },
    });
  }
}

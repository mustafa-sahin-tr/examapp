import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Grade } from '../../models/student';
import { SchoolSection, SchoolSectionService } from '../../services/school-section.service';
import { backendMessage } from '../../shared/utils/admin-action-error.util';

export const SECTION_NAME_MAX = 20;

export interface SectionFormDialogData {
  grades: Grade[];
  /** Verilirse yeniden adlandırma (sınıf değişmez). */
  section?: SchoolSection;
  /** Yeni şubede önceden seçili sınıf. */
  gradeId?: number | null;
}

/** Issue #1279: şube oluşturma / yeniden adlandırma. İstek dialog içinde atılır; 409 (aynı ad) dialogda gösterilir. */
@Component({
  selector: 'app-section-form-dialog',
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoDirective],
  providers: [provideTranslocoScope('school-sections')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'school-sections'">
      <h2 mat-dialog-title>{{ data.section ? t('form.renameTitle') : t('form.createTitle') }}</h2>
      <mat-dialog-content class="sf">
        @if (!data.section) {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ t('form.grade') }}</mat-label>
            <mat-select [(ngModel)]="gradeId" data-testid="section-grade">
              @for (g of data.grades; track g.id) {
                <mat-option [value]="g.id">{{ g.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        } @else {
          <p class="sf__grade">{{ t('form.gradeFixed', { grade: data.section.gradeName }) }}</p>
        }
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ t('form.name') }}</mat-label>
          <input
            matInput
            [(ngModel)]="name"
            [maxlength]="max"
            [placeholder]="t('form.namePlaceholder')"
            (keydown.enter)="submit()"
            data-testid="section-name"
            cdkFocusInitial
          />
          <mat-hint align="end">{{ name.length }} / {{ max }}</mat-hint>
        </mat-form-field>
        @if (error()) {
          <p class="sf__error" role="alert">{{ error() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button mat-dialog-close type="button">{{ t('form.cancel') }}</button>
        <button mat-flat-button color="primary" type="button" [disabled]="!canSubmit() || busy()" (click)="submit()" data-testid="section-save">
          {{ t('form.save') }}
        </button>
      </mat-dialog-actions>
    </ng-container>
  `,
  styles: `
    .sf { display: flex; flex-direction: column; gap: 1rem; min-width: min(360px, 80vw); padding-top: 0.5rem !important; }
    .sf__grade { margin: 0; opacity: 0.8; }
    .sf__error { margin: 0; color: var(--ms-danger-background); }
  `,
})
export class SectionFormDialogComponent {
  readonly data = inject<SectionFormDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<SectionFormDialogComponent, SchoolSection>>(MatDialogRef);
  private readonly service = inject(SchoolSectionService);

  protected readonly max = SECTION_NAME_MAX;
  protected name = this.data.section?.name ?? '';
  protected gradeId: number | null = this.data.gradeId ?? null;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected canSubmit(): boolean {
    const trimmed = this.name.trim();
    return trimmed.length > 0 && trimmed.length <= this.max && (!!this.data.section || this.gradeId != null);
  }

  protected submit(): void {
    if (!this.canSubmit() || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const name = this.name.trim();
    const request = this.data.section
      ? this.service.rename(this.data.section.id, name)
      : this.service.create(this.gradeId!, name);
    request.subscribe({
      next: (section) => this.ref.close(section),
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(backendMessage(err) ?? err.message);
      },
    });
  }
}

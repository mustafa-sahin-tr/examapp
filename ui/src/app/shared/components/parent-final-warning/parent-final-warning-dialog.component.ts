import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { PARENT_REQUIRED_URL } from '../../../models/parent-status.model';

export interface ParentFinalWarningDialogData {
  daysLeft: number;
}

/**
 * Issue #441: hoşgörünün son günlerinde kapatılamaz modal (`disableClose`; kapatma düğmesi yok). Tek eylem "Veli bilgisi ekle"
 * → `/parent-required`; o ekranda modal açılmaz (`ParentFinalWarningComponent`).
 */
@Component({
  selector: 'app-parent-final-warning-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'parentRequirement.finalWarning'">
      <h2 mat-dialog-title class="pfw__title">
        <mat-icon aria-hidden="true">family_restroom</mat-icon>
        {{ t('title') }}
      </h2>
      <mat-dialog-content>
        <p data-test="final-warning-body">{{ t('body', { days: data.daysLeft }) }}</p>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-flat-button type="button" data-test="final-warning-action" cdkFocusInitial (click)="goToForm()">
          {{ t('action') }}
        </button>
      </mat-dialog-actions>
    </ng-container>
  `,
  styleUrl: './parent-final-warning-dialog.component.scss',
})
export class ParentFinalWarningDialogComponent {
  protected readonly data = inject<ParentFinalWarningDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<ParentFinalWarningDialogComponent>);
  private readonly router = inject(Router);

  goToForm(): void {
    void this.router.navigateByUrl(PARENT_REQUIRED_URL).then(() => this.dialogRef.close());
  }
}

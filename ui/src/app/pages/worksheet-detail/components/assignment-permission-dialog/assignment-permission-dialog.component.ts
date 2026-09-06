import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { WorksheetAccessRequestService } from '../../../../services/worksheet-access-request.service';
import { ResponseBase } from '../../../../models/worksheet-access-request.model';

export interface AssignmentPermissionDialogData {
  worksheetId: number;
  worksheetName: string;
  ownerName: string | null;
}

export interface AssignmentPermissionDialogResult {
  submitted: boolean;
}

@Component({
  selector: 'app-assignment-permission-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './assignment-permission-dialog.component.html',
  styleUrl: './assignment-permission-dialog.component.scss',
})
export class AssignmentPermissionDialogComponent {
  protected readonly data = inject<AssignmentPermissionDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(
    MatDialogRef<AssignmentPermissionDialogComponent, AssignmentPermissionDialogResult | undefined>
  );
  private readonly service = inject(WorksheetAccessRequestService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly noteControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.maxLength(500)],
  });

  protected readonly submitting = signal(false);
  protected readonly conflict = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected noteLength(): number {
    return this.noteControl.value?.length ?? 0;
  }

  protected cancel(): void {
    this.dialogRef.close();
  }

  protected submit(): void {
    if (this.submitting() || this.conflict() || this.noteControl.invalid) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const note = this.noteControl.value.trim();
    this.service
      .createRequest({ worksheetId: this.data.worksheetId, note: note || null })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.dialogRef.close({ submitted: true });
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          const body = err.error as ResponseBase | null;
          if (err.status === 409 || body?.conflict) {
            this.conflict.set(true);
            return;
          }
          this.errorMessage.set(body?.message || 'Talep gönderilemedi. Lütfen tekrar deneyin.');
        },
      });
  }
}

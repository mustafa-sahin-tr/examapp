import { Component, inject } from '@angular/core';
import { AbstractControl, FormControl, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

export interface RejectTeacherApplicationDialogData {
  /** Listede gösterilen ad (fallback uygulanmış hâli). */
  displayName: string;
}

/** Backend TeacherRejectRequestDto.Reason MaxLength ile aynı. */
export const REJECT_REASON_MAX_LENGTH = 500;

/**
 * Issue #94 — red nedeni giriş dialog'u. HTTP çağrısı YAPMAZ; yalnızca doğrulanmış nedeni döner
 * (`string`), iptalde `undefined`. Böylece per-row yükleniyor durumu ve hata/snackbar akışı
 * onayla ile aynı yerde (liste bileşeninde) kalır.
 */
@Component({
  selector: 'app-reject-teacher-application-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule],
  templateUrl: './reject-teacher-application-dialog.component.html',
  styleUrls: ['./reject-teacher-application-dialog.component.scss'],
})
export class RejectTeacherApplicationDialogComponent {
  readonly data = inject<RejectTeacherApplicationDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<RejectTeacherApplicationDialogComponent, string | undefined>>(MatDialogRef);

  readonly maxLength = REJECT_REASON_MAX_LENGTH;

  readonly reasonControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, noWhitespaceValidator, Validators.maxLength(REJECT_REASON_MAX_LENGTH)],
  });

  reasonLength(): number {
    return this.reasonControl.value.length;
  }

  cancel(): void {
    this.dialogRef.close(undefined);
  }

  submit(): void {
    this.reasonControl.markAsTouched();
    if (this.reasonControl.invalid) {
      return;
    }
    this.dialogRef.close(this.reasonControl.value.trim());
  }
}

/** Yalnızca boşluktan oluşan neden `required`'ı geçer; backend `AllowEmptyStrings=false` ile reddeder. */
function noWhitespaceValidator(control: AbstractControl<string | null>): ValidationErrors | null {
  return (control.value ?? '').trim().length === 0 ? { whitespace: true } : null;
}

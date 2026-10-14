import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { CHILD_ACCOUNT_ERROR_CODES, isAcceptableChildPassword } from '../../../models/parent-child-account.model';
import { ParentChildAccountService } from '../../../services/parent-child-account.service';
import { ParentLinkService } from '../../../services/parent-link.service';
import { PARENT_LINKS_SCOPE } from '../../../shared/utils/parent-link-ui';

export interface ChildPasswordResetDialogData {
  studentId: number;
  studentName: string;
}

const newPwValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const pw: string = group.get('pw')?.value ?? '';
  const repeat: string = group.get('pwRepeat')?.value ?? '';
  const errors: ValidationErrors = {};
  if (pw && !isAcceptableChildPassword(pw)) errors['pwInvalid'] = true;
  if (repeat && pw !== repeat) errors['pwMismatch'] = true;
  return Object.keys(errors).length ? errors : null;
};

/**
 * Issue #438: veli, kendi açtığı çocuk hesabının şifresini belirler. Yeni şifre yalnız istek gövdesinde gider; diyalog kapanınca
 * form bellekten düşer, şifre hiçbir yerde tekrar gösterilmez. Başarıda `true` ile kapanır (üst sayfa bildirim gösterir).
 */
@Component({
  selector: 'app-child-password-reset-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(PARENT_LINKS_SCOPE)],
  template: `
    <ng-container *transloco="let t; prefix: 'parent-links'">
      <h2 mat-dialog-title>{{ t('resetPw.title', { name: data.studentName }) }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate data-test="reset-form">
        <mat-dialog-content class="cpr__content">
          <p class="cpr__hint">{{ t('resetPw.hint') }}</p>
          <mat-form-field appearance="outline" class="cpr__field">
            <mat-label>{{ t('resetPw.newPw') }}</mat-label>
            <input matInput type="password" formControlName="pw" autocomplete="new-password" maxlength="128" data-test="reset-pw" />
            <mat-hint>{{ t('createChild.pwHint') }}</mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline" class="cpr__field">
            <mat-label>{{ t('resetPw.repeat') }}</mat-label>
            <input matInput type="password" formControlName="pwRepeat" autocomplete="new-password" maxlength="128" data-test="reset-pw-repeat" />
          </mat-form-field>
          @if (form.hasError('pwInvalid') && form.controls.pw.touched) {
            <p class="cpr__error" role="alert" data-test="reset-pw-invalid">{{ t('createChild.pwInvalid') }}</p>
          }
          @if (form.hasError('pwMismatch') && form.controls.pwRepeat.touched) {
            <p class="cpr__error" role="alert" data-test="reset-pw-mismatch">{{ t('createChild.pwMismatch') }}</p>
          }
          @if (error()) {
            <p class="cpr__error" role="alert" data-test="reset-error">{{ error() }}</p>
          }
        </mat-dialog-content>
        <mat-dialog-actions align="end">
          <button mat-stroked-button type="button" mat-dialog-close data-test="reset-cancel">{{ t('resetPw.cancel') }}</button>
          <button mat-flat-button type="submit" [disabled]="submitting()" data-test="reset-submit">
            @if (submitting()) {
              <mat-spinner diameter="18" />
            }
            {{ t('resetPw.submit') }}
          </button>
        </mat-dialog-actions>
      </form>
    </ng-container>
  `,
  styles: [
    `
      .cpr__content {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;
        max-width: 420px;
        font-family: var(--paragraphFontFamily);
      }
      .cpr__hint {
        margin: 0 0 0.75rem;
        font-size: 0.8125rem;
        opacity: 0.8;
      }
      .cpr__field {
        width: 100%;
      }
      .cpr__error {
        margin: 0.25rem 0;
        color: var(--ms-danger-background);
        font-size: 0.8125rem;
      }
      mat-spinner {
        display: inline-block;
        margin-right: 0.5rem;
        vertical-align: middle;
      }
    `,
  ],
})
export class ChildPasswordResetDialogComponent {
  protected readonly data = inject<ChildPasswordResetDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<ChildPasswordResetDialogComponent, boolean>);
  private readonly service = inject(ParentChildAccountService);
  private readonly linkService = inject(ParentLinkService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group(
    {
      pw: ['', [Validators.required]],
      pwRepeat: ['', [Validators.required]],
    },
    { validators: newPwValidator }
  );

  protected submit(): void {
    this.error.set(null);
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    const pw = this.form.controls.pw.value;
    this.submitting.set(true);
    this.service
      .resetPassword(this.data.studentId, pw)
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.form.reset();
          this.dialogRef.close(true);
        },
        error: (err: HttpErrorResponse) => this.error.set(this.errorMessage(err)),
      });
  }

  /** 403 OnlyPrimaryParent: birincil olmayan veli — istemci metni (sunucu metni yoksa da anlaşılır kalsın). */
  private errorMessage(err: HttpErrorResponse): string {
    if (this.linkService.errorCode(err) === CHILD_ACCOUNT_ERROR_CODES.onlyPrimaryParent) {
      return this.transloco.translate<string>('parent-links.resetPw.onlyPrimary');
    }
    return this.linkService.extractError(err, this.transloco.translate<string>('parent-links.resetPw.error'));
  }
}

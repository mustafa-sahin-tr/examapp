import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

export interface TestFinishRetryDialogData {
  title: string;
  message: string;
  retryText: string;
}

/**
 * Issue #383: süre dolduktan sonra son kayıt ya da end-test başarısız olursa açılır. `disableClose` ile
 * açılır ve tek aksiyonu "Tekrar dene"dir — öğrenci süre bittikten sonra sayaçsız cevaplamaya devam edemez.
 * Kapanınca (`true`) çağıran bitirmeyi yeniden dener.
 */
@Component({
  selector: 'app-test-finish-retry-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title class="retry-dialog__title">
      <mat-icon aria-hidden="true">timer_off</mat-icon>
      {{ data.title }}
    </h2>
    <mat-dialog-content>
      <p class="retry-dialog__message" role="alert">{{ data.message }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-flat-button color="primary" [mat-dialog-close]="true" cdkFocusInitial>
        <mat-icon>refresh</mat-icon>
        {{ data.retryText }}
      </button>
    </mat-dialog-actions>
  `,
  styleUrl: './test-finish-retry-dialog.component.scss',
})
export class TestFinishRetryDialogComponent {
  protected readonly data = inject<TestFinishRetryDialogData>(MAT_DIALOG_DATA);
}

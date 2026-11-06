import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslocoDirective } from '@jsverse/transloco';

export interface TestItemAnswerDialogData {
  number: number;
  imageUrl: string | null;
  /** Şıklar sunucu sırasıyla; harf konumdan. */
  answerIds: number[];
  currentAnswerId: number | null;
}

/**
 * Issue #857: test sorusunun doğru cevabını seç — "Cevap yok" uyarısının çözümü. Kapanınca seçilen şık id'si döner
 * (değişmediyse ya da vazgeçilirse `undefined`); kaydetmeyi liste yapar (`PUT questions/{id}/correct-answer`).
 */
@Component({
  selector: 'app-test-item-answer-dialog',
  standalone: true,
  imports: [MatButtonModule, MatButtonToggleModule, MatDialogModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'worksheet-detail.activity.testItems'">
      <h2 mat-dialog-title>{{ t('answerTitle', { number: data.number }) }}</h2>
      <mat-dialog-content>
        @if (data.imageUrl) {
          <img class="q-img" [src]="data.imageUrl" alt="" />
        }
        <p class="pick-label" id="answer-label">{{ t('answerPick') }}</p>
        <mat-button-toggle-group aria-labelledby="answer-label" hideSingleSelectionIndicator
          [value]="selected()" (change)="selected.set($event.value)" data-testid="answer-toggle">
          @for (id of data.answerIds; track id; let i = $index) {
            <mat-button-toggle [value]="id" [attr.data-letter]="letter(i)">{{ letter(i) }}</mat-button-toggle>
          }
        </mat-button-toggle-group>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-stroked-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
        <button mat-flat-button type="button" data-testid="answer-save"
          [disabled]="selected() == null || selected() === data.currentAnswerId" [mat-dialog-close]="selected()">
          {{ t('answerSave') }}
        </button>
      </mat-dialog-actions>
    </ng-container>
  `,
  styles: `
    .q-img {
      display: block;
      max-width: 100%;
      max-height: 50vh;
      margin: 0 auto 12px;
      border-radius: 8px;
      background: #fff;
    }
    .pick-label {
      margin: 0 0 8px;
      color: var(--ms-text-medium);
    }
    mat-button-toggle {
      min-width: 52px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TestItemAnswerDialogComponent {
  readonly data = inject<TestItemAnswerDialogData>(MAT_DIALOG_DATA);
  readonly selected = signal<number | null>(this.data.currentAnswerId);

  letter(index: number): string {
    return String.fromCharCode(65 + index);
  }
}

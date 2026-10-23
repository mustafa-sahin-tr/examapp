import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { QuestionBankItem } from '../../../models/question-bank';

/** Panelle aynı scope adı (panel dosyasından import döngüsel olurdu). */
const QUESTION_BANK_SCOPE = 'question-bank';

/**
 * issue #471: soru bankası kartının büyük önizlemesi. MatDialog kök injector'la açtığı için panelin scope'u burada
 * görünmez; scope dialog'da ayrıca sağlanır.
 */
@Component({
  selector: 'app-question-bank-preview-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, TranslocoDirective],
  providers: [provideTranslocoScope(QUESTION_BANK_SCOPE)],
  template: `
    <ng-container *transloco="let t; prefix: 'question-bank'">
      <h2 mat-dialog-title>{{ t('preview.title', { id: item.id }) }}</h2>
      <mat-dialog-content class="qb-preview">
        @if (item.imageUrl) {
          <img [src]="item.imageUrl" [alt]="t('preview.imageAlt', { id: item.id })" />
        } @else {
          <p class="qb-preview__empty">{{ t('card.noImage') }}</p>
        }
        <p class="qb-preview__meta">
          {{ item.subjectName || t('card.noSubject') }}
          @if (item.topicName) {
            · {{ item.topicName }}
          }
          @for (st of item.subTopics; track st.id) {
            · {{ st.name }}
          }
        </p>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button mat-dialog-close>{{ t('preview.close') }}</button>
      </mat-dialog-actions>
    </ng-container>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .qb-preview img {
      display: block;
      max-width: 100%;
      max-height: 70vh;
      margin: 0 auto;
      background: var(--whiteColor);
    }
    .qb-preview__meta,
    .qb-preview__empty {
      color: var(--ms-text-medium);
      font-family: var(--paragraphFontFamily);
    }
  `,
})
export class QuestionBankPreviewDialogComponent {
  readonly item = inject<QuestionBankItem>(MAT_DIALOG_DATA);
}

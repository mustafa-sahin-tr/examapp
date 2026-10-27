import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { QuestionBankItem } from '../../../models/question-bank';

/**
 * Issue #580: çözüm onayı önizlemeden yapılır — `approve` ile açılan diyalog soruyu, çözümün kaynağını ve üreten talimat
 * sürümünü gösterir; "Onayla" diyaloğu `'approve'` ile kapatır.
 */
export interface QuestionBankPreviewData {
  item: QuestionBankItem;
  approve: true;
}

export type QuestionBankPreviewResult = 'approve' | undefined;

function isApproveData(data: QuestionBankItem | QuestionBankPreviewData): data is QuestionBankPreviewData {
  return (data as QuestionBankPreviewData).approve === true;
}

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
        @if (approve) {
          <p class="qb-preview__solution">
            {{ t('card.solutionPending') }}
            @if (item.solutionSource === 'Key' || item.solutionSource === 'AI') { · {{ t('card.solutionSources.' + item.solutionSource) }} }
            @if (item.solutionPromptVersion != null) {
              · <span class="qb-preview__prompt">{{ t('card.solutionPromptVersion', { version: item.solutionPromptVersion }) }}</span>
            }
          </p>
          <p class="qb-preview__hint">{{ t('preview.approveHint') }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button mat-dialog-close>{{ t('preview.close') }}</button>
        @if (approve) {
          <button mat-flat-button class="qb-preview__approve" [mat-dialog-close]="'approve'">{{ t('preview.approve') }}</button>
        }
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
    .qb-preview__empty,
    .qb-preview__solution,
    .qb-preview__hint {
      color: var(--ms-text-medium);
      font-family: var(--paragraphFontFamily);
    }
  `,
})
export class QuestionBankPreviewDialogComponent {
  private readonly data = inject<QuestionBankItem | QuestionBankPreviewData>(MAT_DIALOG_DATA);
  readonly item: QuestionBankItem = isApproveData(this.data) ? this.data.item : this.data;
  readonly approve = isApproveData(this.data);
}

import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { LinkBankQuestionsResult } from '../../../../models/question-bank';
import { QuestionService } from '../../../../services/question.service';
import { QuestionBankPanelComponent } from '../../../../shared/components/question-bank-panel/question-bank-panel.component';

export interface BankQuestionDialogData {
  testId: number;
}

/**
 * issue #472: test ekranında "Bankadan ekle" — soru bankası panelini seçim modunda açar; testte zaten olan sorular pasif.
 * Seçilenler testin sonuna bağlanır; dialog sunucu yanıtıyla kapanır (vazgeçince `undefined`).
 */
@Component({
  selector: 'app-bank-question-dialog',
  standalone: true,
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    QuestionBankPanelComponent,
  ],
  // MatDialog ile açılan komponent element injector'dan scope devralmaz; kendi provider'ını verir.
  providers: [provideTranslocoScope('worksheet-detail')],
  templateUrl: './bank-question-dialog.component.html',
  styleUrl: './bank-question-dialog.component.scss',
})
export class BankQuestionDialogComponent {
  protected readonly data = inject<BankQuestionDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<BankQuestionDialogComponent, LinkBankQuestionsResult | undefined>);
  private readonly questions = inject(QuestionService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  /** Testte zaten olan soru id'leri; yüklenene kadar panel gösterilmez (yanlışlıkla seçilmesinler). */
  protected readonly existingIds = signal<number[] | null>(null);
  protected readonly selectedIds = signal<number[]>([]);
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    this.questions
      .getAll(this.data.testId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => this.existingIds.set((list ?? []).map((q) => q.id).filter((id): id is number => !!id)),
        error: () => this.errorMessage.set(this.tr('bankDialog.loadFailed')),
      });
  }

  protected onSelectionChange(ids: number[]): void {
    this.selectedIds.set(ids);
    this.errorMessage.set(null);
  }

  protected cancel(): void {
    this.dialogRef.close();
  }

  protected submit(): void {
    const ids = this.selectedIds();
    if (this.submitting() || ids.length === 0) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.questions
      .linkBankQuestions(this.data.testId, ids)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.dialogRef.close(result);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.errorMessage.set(this.tr(err.status === 404 ? 'bankDialog.notFound' : 'bankDialog.submitFailed'));
        },
      });
  }

  private tr(key: string): string {
    return this.transloco.translate<string>(`worksheet-detail.${key}`) ?? '';
  }
}

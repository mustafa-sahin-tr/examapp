import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { LinkBankQuestionsResult, QuestionBankItem } from '../../../../models/question-bank';
import { QuestionService } from '../../../../services/question.service';
import { QuestionBankPanelComponent, difficultyBucket } from '../../../../shared/components/question-bank-panel/question-bank-panel.component';
import { ClassificationSelection } from '../../../../shared/components/classification-selector/classification-selector.component';

export interface BankQuestionDialogData {
  testId: number;
  /** Panel filtresinin açılıştaki seçimi (testin sınıf/ders/konu/alt konusu); yoksa filtresiz açılır. */
  initialClassification?: ClassificationSelection;
}

/**
 * issue #472: test ekranında "Havuzdan ekle" — soru bankası panelini seçim modunda açar; testte zaten olan sorular pasif.
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
  changeDetection: ChangeDetectionStrategy.Eager,
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
  /** Issue #863: seçilen soruların kendisi — seçim çubuğu ve zorluk özeti. */
  protected readonly selectedItems = signal<QuestionBankItem[]>([]);
  /** Issue #863: "2 Kolay, 1 Orta". */
  protected readonly difficultySummary = computed(() => {
    const counts = { easy: 0, medium: 0, hard: 0 };
    for (const item of this.selectedItems()) counts[difficultyBucket(item.difficultyLevel)]++;
    return (['easy', 'medium', 'hard'] as const)
      .filter((bucket) => counts[bucket] > 0)
      .map((bucket) => `${counts[bucket]} ${this.transloco.translate<string>(`question-bank.list.${bucket}`)}`)
      .join(', ');
  });
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

  private readonly panel = viewChild(QuestionBankPanelComponent);

  protected removeSelected(id: number): void {
    this.panel()?.removeSelection(id);
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

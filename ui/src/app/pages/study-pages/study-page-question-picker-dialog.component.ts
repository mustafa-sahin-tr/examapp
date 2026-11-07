import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { QuestionBankItem } from '../../models/question-bank';
import { QuestionBankService } from '../../services/question-bank.service';

export interface StudyPageQuestionPickerData {
  /** Sayfanın dersi; varsa arama o derse daraltılır. */
  subjectId: number | null;
}

const PAGE_SIZE = 24;

/**
 * Issue #933: soru bloğu için soru bankasından tek soru seçer. Görünürlük bankanın kuralıdır (`scope=mine|all`);
 * kayıtta sunucu aynı kuralı yeniden denetler.
 */
@Component({
  selector: 'app-study-page-question-picker-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatButtonToggleModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope('study-pages')],
  templateUrl: './study-page-question-picker-dialog.component.html',
  styleUrl: './study-page-question-picker-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StudyPageQuestionPickerDialogComponent implements OnInit {
  private readonly bank = inject(QuestionBankService);
  private readonly dialogRef = inject<MatDialogRef<StudyPageQuestionPickerDialogComponent, QuestionBankItem>>(MatDialogRef);
  private readonly destroyRef = inject(DestroyRef);
  readonly data = inject<StudyPageQuestionPickerData>(MAT_DIALOG_DATA);

  readonly scope = signal<'mine' | 'all'>('mine');
  readonly items = signal<QuestionBankItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);

  ngOnInit(): void {
    this.load(true);
  }

  setScope(scope: 'mine' | 'all'): void {
    if (scope === this.scope()) return;
    this.scope.set(scope);
    this.load(true);
  }

  load(reset: boolean): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.loadFailed.set(false);
    this.bank
      .search({
        scope: this.scope(),
        subjectId: this.data.subjectId,
        kind: 'question',
        skip: reset ? 0 : this.items().length,
        take: PAGE_SIZE,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.items.set(reset ? page.items : [...this.items(), ...page.items]);
          this.total.set(page.total);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.loadFailed.set(true);
        },
      });
  }

  pick(item: QuestionBankItem): void {
    this.dialogRef.close(item);
  }
}

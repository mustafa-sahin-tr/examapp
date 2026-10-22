import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { LabelingPrompt, LabelingPromptState } from '../../../models/labeling';
import { LabelingService } from '../../../services/labeling.service';

const MAX_INSTRUCTION = 4000;
const MAX_PADDING = 50;

/**
 * Claude ön-etiketleme prompt'u: iki aşamanın talimatı + kutu payları. Kayıt yeni sürüm oluşturur (geçmiş silinmez);
 * geçmişten ya da varsayılandan bir sürüm forma yüklenip düzenlenebilir. Koordinat kuralı ve JSON şeması sunucuda
 * sabittir, burada düzenlenmez. Kapanış sonucu: kaydedildiyse yeni durum, değilse `undefined`.
 */
@Component({
  selector: 'app-labeling-prompt-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DatePipe,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('labeling')],
  templateUrl: './labeling-prompt-dialog.component.html',
  styleUrls: ['./labeling-prompt-dialog.component.scss'],
})
export class LabelingPromptDialogComponent {
  private readonly api = inject(LabelingService);
  private readonly dialogRef = inject(MatDialogRef<LabelingPromptDialogComponent, LabelingPromptState>);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly maxInstruction = MAX_INSTRUCTION;
  readonly state = signal<LabelingPromptState | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadFailed = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly form = this.fb.group({
    questionInstruction: ['', [Validators.required, Validators.maxLength(MAX_INSTRUCTION)]],
    optionInstruction: ['', [Validators.required, Validators.maxLength(MAX_INSTRUCTION)]],
    questionPadding: [0, [Validators.required, Validators.min(0), Validators.max(MAX_PADDING)]],
    optionPadding: [0, [Validators.required, Validators.min(0), Validators.max(MAX_PADDING)]],
    expandQuestionToOptions: [true],
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.api
      .getPrompt()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (state) => {
          this.state.set(state);
          this.fill(state.active);
          this.loading.set(false);
        },
        error: (error) => {
          console.error('labeling prompt load failed', error);
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  /** Geçmişteki bir sürümü ya da varsayılanı forma yükler (kaydedilene kadar etkin olmaz). */
  fill(prompt: LabelingPrompt): void {
    this.form.setValue({
      questionInstruction: prompt.questionInstruction,
      optionInstruction: prompt.optionInstruction,
      questionPadding: prompt.questionPadding,
      optionPadding: prompt.optionPadding,
      expandQuestionToOptions: prompt.expandQuestionToOptions,
    });
    this.form.markAsDirty();
  }

  loadVersion(version: number): void {
    const prompt = this.state()?.history.find((p) => p.version === version);
    if (prompt) {
      this.fill(prompt);
    }
  }

  resetToDefaults(): void {
    const defaults = this.state()?.defaults;
    if (defaults) {
      this.fill(defaults);
    }
  }

  save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.api.savePrompt(this.form.getRawValue()).subscribe({
      next: (state) => {
        this.saving.set(false);
        this.dialogRef.close(state);
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.saveError.set((error.error as { message?: string } | null)?.message ?? '');
      },
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}

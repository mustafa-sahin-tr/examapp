import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { PlacementQuestion, PlacementReport, PlacementStatus } from '../../models/placement.model';
import { PlacementService } from '../../services/placement.service';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { PlacementReportComponent } from '../../shared/components/placement-report/placement-report.component';

type View = 'loading' | 'error' | 'unavailable' | 'intro' | 'questions' | 'report';

/**
 * Issue #1114: ücretsiz seviye tespiti. Plan kilidi (#1090) altında da açıktır. Sorular tek sayfada, cevaplar tek seferde gönderilir;
 * çözülmüşse doğrudan rapor gösterilir.
 */
@Component({
  selector: 'app-placement',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    TranslocoDirective,
    PageHeaderComponent,
    PlacementReportComponent,
  ],
  providers: [provideTranslocoScope('placement')],
  templateUrl: './placement.component.html',
  styleUrl: './placement.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlacementComponent {
  private readonly service = inject(PlacementService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);

  readonly view = signal<View>('loading');
  readonly status = signal<PlacementStatus | null>(null);
  readonly questions = signal<PlacementQuestion[]>([]);
  readonly report = signal<PlacementReport | null>(null);
  /** Soru id → seçilen şık id. */
  readonly answers = signal<Record<number, number>>({});
  readonly submitting = signal(false);
  readonly submitError = signal(false);

  readonly answeredCount = computed(() => Object.keys(this.answers()).length);

  constructor() {
    this.load();
  }

  load(): void {
    this.view.set('loading');
    this.service
      .status()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => {
          this.status.set(status);
          if (status.taken) this.loadReport();
          else this.view.set(status.available ? 'intro' : 'unavailable');
        },
        error: () => this.view.set('error'),
      });
  }

  private loadReport(): void {
    this.service
      .report()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.view.set('report');
        },
        error: () => this.view.set('error'),
      });
  }

  start(): void {
    this.view.set('loading');
    this.service
      .questions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (questions) => {
          this.questions.set(questions);
          this.answers.set({});
          this.view.set('questions');
        },
        // 409: başka sekmede çözüldü → rapor.
        error: (err: { status?: number }) => (err?.status === 409 ? this.loadReport() : this.view.set('error')),
      });
  }

  choose(questionId: number, answerId: number): void {
    this.answers.update((a) => ({ ...a, [questionId]: answerId }));
  }

  submit(): void {
    if (this.submitting()) return;
    const blank = this.questions().length - this.answeredCount();
    if (blank === 0) {
      this.send();
      return;
    }
    const data: ConfirmDialogData = {
      title: this.transloco.translate('placement.submit'),
      message: this.transloco.translate('placement.submitConfirm', { blank }),
      confirmText: this.transloco.translate('placement.submit'),
      icon: 'help_outline',
    };
    this.dialog
      .open(ConfirmDialogComponent, { data })
      .afterClosed()
      .subscribe((ok) => ok && this.send());
  }

  private send(): void {
    this.submitting.set(true);
    this.submitError.set(false);
    const answers = this.questions().map((q) => ({ questionId: q.id, answerId: this.answers()[q.id] ?? null }));
    this.service
      .submit(answers)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (report) => {
          this.submitting.set(false);
          this.report.set(report);
          this.view.set('report');
          window.scrollTo({ top: 0 });
        },
        error: (err: { status?: number }) => {
          this.submitting.set(false);
          if (err?.status === 409) this.loadReport();
          else this.submitError.set(true);
        },
      });
  }
}

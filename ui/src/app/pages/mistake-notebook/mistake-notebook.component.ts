import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { MISTAKE_REASONS, MistakeItem, MistakeReason } from '../../models/practice';
import { PracticeService } from '../../services/practice.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/** Issue #698: testler ve pratikteki yanlışlar tek listede; ders süzülür, süzülen yanlışlardan pratik açılır. */
@Component({
  selector: 'app-mistake-notebook',
  standalone: true,
  imports: [
    PageHeaderComponent,
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('practice')],
  templateUrl: './mistake-notebook.component.html',
  styleUrl: './mistake-notebook.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MistakeNotebookComponent implements OnInit {
  private readonly practiceService = inject(PracticeService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly reasons = MISTAKE_REASONS;
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly starting = signal(false);
  readonly startFailed = signal(false);
  readonly saveFailed = signal(false);
  readonly items = signal<MistakeItem[]>([]);
  readonly totalCount = signal(0);
  readonly subjectId = signal<number | null>(null);

  /** Listedeki dersler; yalnız birden çok ders varsa süzgeç gösterilir. */
  readonly subjects = computed(() => {
    const seen = new Map<number, string>();
    for (const item of this.items()) {
      if (item.subjectId !== null && item.subjectName) {
        seen.set(item.subjectId, item.subjectName);
      }
    }
    return [...seen].map(([id, name]) => ({ id, name }));
  });

  readonly visibleItems = computed(() => {
    const subjectId = this.subjectId();
    return subjectId === null ? this.items() : this.items().filter((item) => item.subjectId === subjectId);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.practiceService
      .getMistakes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (notebook) => {
          this.items.set(notebook.items);
          this.totalCount.set(notebook.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  startPractice(): void {
    const subjectId = this.subjectId();
    this.starting.set(true);
    this.startFailed.set(false);
    this.practiceService
      .startSession({ mistakes: true, subjectIds: subjectId === null ? [] : [subjectId] })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => void this.router.navigate(['/practice'], { queryParams: { session: session.id } }),
        error: () => {
          this.starting.set(false);
          this.startFailed.set(true);
        },
      });
  }

  setReason(item: MistakeItem, reason: MistakeReason | null): void {
    const previous = item.reason;
    this.updateReason(item.questionId, reason);
    this.saveFailed.set(false);
    this.practiceService
      .setMistakeReason(item.questionId, reason)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.updateReason(item.questionId, previous);
          this.saveFailed.set(true);
        },
      });
  }

  private updateReason(questionId: number, reason: MistakeReason | null): void {
    this.items.update((items) => items.map((i) => (i.questionId === questionId ? { ...i, reason } : i)));
  }
}

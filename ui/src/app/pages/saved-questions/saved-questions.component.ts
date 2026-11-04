import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { SavedQuestion } from '../../models/practice';
import { PracticeService } from '../../services/practice.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/** Issue #532: kaydedilen sorular; buradan pratik açılır, kayıt kaldırılır. */
@Component({
  selector: 'app-saved-questions',
  standalone: true,
  imports: [PageHeaderComponent, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope('practice')],
  templateUrl: './saved-questions.component.html',
  styleUrl: '../mistake-notebook/mistake-notebook.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SavedQuestionsComponent implements OnInit {
  private readonly practiceService = inject(PracticeService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly starting = signal(false);
  readonly actionError = signal<'start' | 'remove' | null>(null);
  readonly items = signal<SavedQuestion[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.practiceService
      .getSavedQuestions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => {
          this.items.set(saved.items);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  startPractice(): void {
    this.starting.set(true);
    this.actionError.set(null);
    this.practiceService
      .startSession({ saved: true })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => void this.router.navigate(['/practice'], { queryParams: { session: session.id } }),
        error: () => {
          this.starting.set(false);
          this.actionError.set('start');
        },
      });
  }

  remove(item: SavedQuestion): void {
    this.actionError.set(null);
    this.practiceService
      .unsaveQuestion(item.questionId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.items.update((items) => items.filter((i) => i.questionId !== item.questionId)),
        error: () => this.actionError.set('remove'),
      });
  }
}

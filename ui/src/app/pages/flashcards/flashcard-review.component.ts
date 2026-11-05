import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { FlashcardDueCard, FlashcardRecall } from '../../models/flashcard.model';
import { FlashcardService } from '../../services/flashcard.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/**
 * Issue #699: günü gelen kartlar tek tek. `Forgot` denen kart aynı oturumda sona eklenir; sunucu onu yarına yazdığı
 * için ikinci değerlendirme 409 `NotDue` döner ve sessizce geçilir.
 */
@Component({
  selector: 'app-flashcard-review',
  standalone: true,
  imports: [PageHeaderComponent, RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope('flashcards')],
  templateUrl: './flashcard-review.component.html',
  styleUrl: './flashcard-review.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlashcardReviewComponent implements OnInit {
  private readonly flashcards = inject(FlashcardService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly recalls: readonly FlashcardRecall[] = ['Forgot', 'Hard', 'Good'];
  private readonly deckId = this.parseDeckId();

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly saveFailed = signal(false);
  readonly queue = signal<FlashcardDueCard[]>([]);
  readonly revealed = signal(false);
  /** Oturumda en az bir kez değerlendirilen farklı kartlar. */
  readonly reviewedIds = signal<ReadonlySet<number>>(new Set());

  readonly current = computed(() => this.queue()[0] ?? null);
  readonly reviewedCount = computed(() => this.reviewedIds().size);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.flashcards
      .getDue(this.deckId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (cards) => {
          this.queue.set(cards);
          this.revealed.set(false);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  rate(recall: FlashcardRecall): void {
    const card = this.current();
    if (!card || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.saveFailed.set(false);
    this.flashcards
      .review(card.id, recall)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.advance(card, recall),
        error: (error: unknown) => {
          // 409: bugün zaten değerlendirildi. 404: atama kalktı ya da kart silindi; ikisinde de sıradaki karta geçilir.
          if (error instanceof HttpErrorResponse && (error.status === 409 || error.status === 404)) {
            this.advance(card, recall);
            return;
          }
          this.saving.set(false);
          this.saveFailed.set(true);
        },
      });
  }

  private advance(card: FlashcardDueCard, recall: FlashcardRecall): void {
    this.saving.set(false);
    this.revealed.set(false);
    this.reviewedIds.update((ids) => new Set(ids).add(card.id));
    const rest = this.queue().slice(1);
    this.queue.set(recall === 'Forgot' ? [...rest, card] : rest);
    // Liste sayfalıdır (en çok 50): kuyruk bitince kalan kart var mı diye yeniden sorulur.
    if (this.queue().length === 0) {
      this.load();
    }
  }

  private parseDeckId(): number | null {
    const deck = Number(this.route.snapshot.queryParamMap.get('deck'));
    return Number.isInteger(deck) && deck > 0 ? deck : null;
  }
}

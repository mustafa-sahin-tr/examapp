import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { FlashcardDeckSummary, FlashcardMode, SaveFlashcardDeck } from '../../models/flashcard.model';
import { FlashcardService, flashcardErrorCode } from '../../services/flashcard.service';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { FlashcardDeckFormComponent } from './flashcard-deck-form.component';

/** Issue #699: öğrencide atanan desteler + kendi destesi, öğretmende kendi desteleri. Rol rotanın `mode` verisinden gelir. */
@Component({
  selector: 'app-flashcard-decks',
  standalone: true,
  imports: [
    PageHeaderComponent,
    FlashcardDeckFormComponent,
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('flashcards')],
  templateUrl: './flashcard-decks.component.html',
  styleUrl: './flashcard-decks.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlashcardDecksComponent implements OnInit {
  private readonly flashcards = inject(FlashcardService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  readonly mode: FlashcardMode = this.route.snapshot.data['mode'] === 'teacher' ? 'teacher' : 'student';
  readonly isStudent = this.mode === 'student';

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly decks = signal<FlashcardDeckSummary[]>([]);
  readonly creating = signal(false);
  readonly saving = signal(false);
  /** `flashcards.errors` altındaki anahtar. */
  readonly errorKey = signal<string | null>(null);

  readonly assignedDecks = computed(() => this.decks().filter((deck) => !deck.isOwner));
  readonly ownDecks = computed(() => this.decks().filter((deck) => deck.isOwner));
  readonly totalDue = computed(() => this.decks().reduce((sum, deck) => sum + (deck.dueCount ?? 0), 0));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.flashcards
      .listDecks(this.mode)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (decks) => {
          this.decks.set(decks);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  deckRoute(deck: FlashcardDeckSummary): (string | number)[] {
    return this.isStudent ? ['/flashcards', deck.id] : ['/teacher/flashcards', deck.id];
  }

  create(deck: SaveFlashcardDeck): void {
    this.saving.set(true);
    this.errorKey.set(null);
    this.flashcards
      .createDeck(this.mode, deck)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => void this.router.navigate(this.deckRoute(created)),
        error: (error: unknown) => {
          this.saving.set(false);
          this.errorKey.set(flashcardErrorCode(error) ?? 'generic');
        },
      });
  }

  confirmDelete(deck: FlashcardDeckSummary): void {
    const scope = 'flashcards.decks';
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          title: this.transloco.translate(`${scope}.deleteTitle`),
          message: this.transloco.translate(`${scope}.deleteText`, { title: deck.title }),
          confirmText: this.transloco.translate(`${scope}.delete`),
          icon: 'delete',
          confirmColor: 'warn',
        },
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((confirmed) => {
        if (confirmed) {
          this.delete(deck);
        }
      });
  }

  private delete(deck: FlashcardDeckSummary): void {
    this.errorKey.set(null);
    this.flashcards
      .deleteDeck(this.mode, deck.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.decks.update((decks) => decks.filter((d) => d.id !== deck.id)),
        error: () => this.errorKey.set('generic'),
      });
  }
}

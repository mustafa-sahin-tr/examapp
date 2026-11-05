import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  FLASHCARD_MAX_IMAGE_BYTES,
  FLASHCARD_MAX_TEXT_LENGTH,
  Flashcard,
  FlashcardDeckDetail,
  FlashcardMode,
  SaveFlashcard,
  SaveFlashcardDeck,
} from '../../models/flashcard.model';
import { FlashcardService, encodeFlashcardText, flashcardErrorCode } from '../../services/flashcard.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { FlashcardAssignmentsComponent } from './flashcard-assignments.component';
import { FlashcardDeckFormComponent } from './flashcard-deck-form.component';

/** Issue #699: destenin kartları (ekle, düzelt, sil); öğretmende atamalar da burada. */
@Component({
  selector: 'app-flashcard-deck-detail',
  standalone: true,
  imports: [
    PageHeaderComponent,
    FlashcardAssignmentsComponent,
    FlashcardDeckFormComponent,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('flashcards')],
  templateUrl: './flashcard-deck-detail.component.html',
  styleUrl: './flashcard-deck-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlashcardDeckDetailComponent implements OnInit {
  private readonly flashcards = inject(FlashcardService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly mode: FlashcardMode = this.route.snapshot.data['mode'] === 'teacher' ? 'teacher' : 'student';
  readonly isTeacher = this.mode === 'teacher';
  readonly backRoute = this.isTeacher ? '/teacher/flashcards' : '/flashcards';
  private readonly deckId = Number(this.route.snapshot.paramMap.get('id'));
  protected readonly maxTextLength = FLASHCARD_MAX_TEXT_LENGTH;

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly deck = signal<FlashcardDeckDetail | null>(null);
  readonly editingDeck = signal(false);
  readonly saving = signal(false);
  /** `flashcards.errors` altındaki anahtar. */
  readonly errorKey = signal<string | null>(null);

  // Kart formu; `editingCardId` boşken yeni kart eklenir.
  readonly editingCardId = signal<number | null>(null);
  readonly front = signal('');
  readonly back = signal('');
  readonly image = signal<string | null>(null);
  readonly existingImageUrl = signal<string | null>(null);
  readonly removeImage = signal(false);

  readonly imagePreview = computed(() => this.image() ?? (this.removeImage() ? null : this.existingImageUrl()));
  readonly canSaveCard = computed(
    () => !this.saving() && this.back().trim().length > 0 && (this.front().trim().length > 0 || this.imagePreview() !== null),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.flashcards
      .getDeck(this.mode, this.deckId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (deck) => {
          this.deck.set(deck);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  saveDeck(request: SaveFlashcardDeck): void {
    this.saving.set(true);
    this.errorKey.set(null);
    this.flashcards
      .updateDeck(this.mode, this.deckId, request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (summary) => {
          this.deck.update((deck) => (deck ? { ...deck, ...summary, cards: deck.cards } : deck));
          this.saving.set(false);
          this.editingDeck.set(false);
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  editCard(card: Flashcard): void {
    this.errorKey.set(null);
    this.editingCardId.set(card.id);
    this.front.set(card.front);
    this.back.set(card.back);
    this.image.set(null);
    this.existingImageUrl.set(card.imageUrl);
    this.removeImage.set(false);
  }

  resetCardForm(): void {
    this.editingCardId.set(null);
    this.front.set('');
    this.back.set('');
    this.image.set(null);
    this.existingImageUrl.set(null);
    this.removeImage.set(false);
  }

  onImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    this.errorKey.set(null);
    if (!file.type.startsWith('image/')) {
      this.errorKey.set('InvalidImage');
      return;
    }
    if (file.size > FLASHCARD_MAX_IMAGE_BYTES) {
      this.errorKey.set('ImageTooLarge');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      this.image.set(reader.result as string);
      this.removeImage.set(false);
    };
    reader.onerror = () => this.errorKey.set('InvalidImage');
    reader.readAsDataURL(file);
  }

  clearImage(): void {
    this.image.set(null);
    this.removeImage.set(this.existingImageUrl() !== null);
  }

  saveCard(): void {
    if (!this.canSaveCard()) {
      return;
    }
    const request: SaveFlashcard = {
      front: this.front().trim(),
      back: this.back().trim(),
      image: this.image(),
      removeImage: this.removeImage(),
    };
    // Sınır sunucuda kodlanmış metne uygulanır: `<` ve `&` kodlanınca uzar.
    if (
      encodeFlashcardText(request.front).length > FLASHCARD_MAX_TEXT_LENGTH ||
      encodeFlashcardText(request.back).length > FLASHCARD_MAX_TEXT_LENGTH
    ) {
      this.errorKey.set('CardTooLong');
      return;
    }

    const cardId = this.editingCardId();
    this.saving.set(true);
    this.errorKey.set(null);
    const save$ =
      cardId === null
        ? this.flashcards.addCard(this.mode, this.deckId, request)
        : this.flashcards.updateCard(this.mode, cardId, request);
    save$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (card) => {
        this.deck.update((deck) => {
          if (!deck) {
            return deck;
          }
          const cards = cardId === null ? [...deck.cards, card] : deck.cards.map((c) => (c.id === cardId ? card : c));
          return { ...deck, cards, cardCount: cards.length };
        });
        this.saving.set(false);
        this.resetCardForm();
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  deleteCard(card: Flashcard): void {
    this.errorKey.set(null);
    this.flashcards
      .deleteCard(this.mode, card.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deck.update((deck) => {
            if (!deck) {
              return deck;
            }
            const cards = deck.cards.filter((c) => c.id !== card.id);
            return { ...deck, cards, cardCount: cards.length };
          });
          if (this.editingCardId() === card.id) {
            this.resetCardForm();
          }
        },
        error: () => this.errorKey.set('generic'),
      });
  }

  private fail(error: unknown): void {
    this.saving.set(false);
    this.errorKey.set(flashcardErrorCode(error) ?? 'generic');
  }
}

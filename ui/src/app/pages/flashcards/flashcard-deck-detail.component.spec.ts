import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import flashcardsTr from '../../../../public/i18n/flashcards/tr.json';
import { Flashcard, FlashcardDeckDetail, FlashcardMode } from '../../models/flashcard.model';
import { AuthService } from '../../services/auth.service';
import { FlashcardService } from '../../services/flashcard.service';
import { GradesService } from '../../services/grades.service';
import { StudentService } from '../../services/student.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { FlashcardDeckDetailComponent } from './flashcard-deck-detail.component';

const card = (id: number, imageUrl: string | null = null): Flashcard => ({ id, front: `f${id}`, back: `b${id}`, imageUrl, order: id });

const detail = (cards: Flashcard[]): FlashcardDeckDetail => ({
  id: 4,
  title: 'Deck',
  gradeId: null,
  gradeName: null,
  subjectId: null,
  subjectName: null,
  topicId: null,
  topicName: null,
  cardCount: cards.length,
  dueCount: null,
  isOwner: true,
  cards,
});

describe('FlashcardDeckDetailComponent (issue #699)', () => {
  let service: Record<'getDeck' | 'addCard' | 'updateCard' | 'deleteCard' | 'listAssignments', ReturnType<typeof vi.fn>>;

  function create(cards: Flashcard[], mode: FlashcardMode = 'student'): ComponentFixture<FlashcardDeckDetailComponent> {
    service = {
      getDeck: vi.fn().mockReturnValue(of(detail(cards))),
      addCard: vi.fn().mockReturnValue(of(card(50))),
      updateCard: vi.fn().mockImplementation((_m, id: number) => of({ ...card(id), front: 'changed' })),
      deleteCard: vi.fn().mockReturnValue(of(undefined)),
      listAssignments: vi.fn().mockReturnValue(of([])),
    };
    TestBed.configureTestingModule({
      imports: [FlashcardDeckDetailComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'flashcards/tr': flashcardsTr } })],
      providers: [
        provideRouter([]),
        { provide: FlashcardService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { data: { mode }, paramMap: convertToParamMap({ id: '4' }) } } },
        { provide: GradesService, useValue: { getGrades: () => of([]) } },
        { provide: SubjectService, useValue: {} },
        { provide: StudentService, useValue: { getLookup: () => of([]) } },
        { provide: AuthService, useValue: { user: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(FlashcardDeckDetailComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('student_SeesNoAssignments', () => {
    const fixture = create([]);
    expect(fixture.nativeElement.querySelector('[data-test="cards-empty"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-flashcard-assignments')).toBeNull();
  });

  it('teacher_SeesAssignments', () => {
    const fixture = create([], 'teacher');
    expect(service.getDeck).toHaveBeenCalledWith('teacher', 4);
    expect(fixture.nativeElement.querySelector('app-flashcard-assignments')).not.toBeNull();
  });

  it('canSave_NeedsBack_AndFrontOrImage', () => {
    const c = create([]).componentInstance;
    c.back.set('answer');
    expect(c.canSaveCard()).toBe(false);
    c.image.set('data:image/png;base64,AA==');
    expect(c.canSaveCard()).toBe(true);
    c.image.set(null);
    c.front.set('question');
    expect(c.canSaveCard()).toBe(true);
    c.back.set('   ');
    expect(c.canSaveCard()).toBe(false);
  });

  it('addCard_AppendsAndClearsTheForm', () => {
    const c = create([card(1)]).componentInstance;
    c.front.set(' q ');
    c.back.set(' a ');
    c.saveCard();
    expect(service.addCard).toHaveBeenCalledWith('student', 4, { front: 'q', back: 'a', image: null, removeImage: false });
    expect(c.deck()?.cards.map((x) => x.id)).toEqual([1, 50]);
    expect(c.deck()?.cardCount).toBe(2);
    expect(c.front()).toBe('');
  });

  it('editCard_UpdatesInPlace', () => {
    const c = create([card(1), card(2)]).componentInstance;
    c.editCard(card(1));
    c.saveCard();
    expect(service.updateCard).toHaveBeenCalledWith('student', 1, expect.objectContaining({ front: 'f1', back: 'b1' }));
    expect(c.deck()?.cards[0].front).toBe('changed');
    expect(c.editingCardId()).toBeNull();
  });

  it('clearImage_OnExistingImage_AsksToRemove', () => {
    const c = create([card(1, 'https://img/1')]).componentInstance;
    c.editCard(card(1, 'https://img/1'));
    expect(c.imagePreview()).toBe('https://img/1');
    c.clearImage();
    expect(c.imagePreview()).toBeNull();
    c.back.set('b');
    c.saveCard();
    expect(service.updateCard).toHaveBeenCalledWith('student', 1, expect.objectContaining({ removeImage: true, image: null }));
  });

  it('tooBigImage_IsRejectedOnTheClient', () => {
    const c = create([]).componentInstance;
    const file = new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'big.png', { type: 'image/png' });
    c.onImageSelected({ target: { files: [file], value: 'x' } } as unknown as Event);
    expect(c.errorKey()).toBe('ImageTooLarge');
    expect(c.image()).toBeNull();
  });

  it('textThatGrowsPastTheLimit_IsNotSent', () => {
    const c = create([]).componentInstance;
    c.front.set('<'.repeat(1000));
    c.back.set('a');
    c.saveCard();
    expect(c.errorKey()).toBe('CardTooLong');
    expect(service.addCard).not.toHaveBeenCalled();
  });

  it('ruleError_ShowsItsMessage_AndKeepsTheForm', () => {
    const c = create([]).componentInstance;
    service.addCard.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'CardLimitReached' } })));
    c.front.set('q');
    c.back.set('a');
    c.saveCard();
    expect(c.errorKey()).toBe('CardLimitReached');
    expect(c.front()).toBe('q');
    expect(c.saving()).toBe(false);
  });

  it('deleteCard_RemovesIt', () => {
    const c = create([card(1), card(2)]).componentInstance;
    c.deleteCard(card(1));
    expect(c.deck()?.cards.map((x) => x.id)).toEqual([2]);
    expect(c.deck()?.cardCount).toBe(1);
  });
});

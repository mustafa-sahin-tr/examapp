import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { StudyPage, StudyPageBlockType, StudyPageContentType } from '../../models/study-page';
import { AuthService } from '../../services/auth.service';
import { PracticeService } from '../../services/practice.service';
import { StudyPageService } from '../../services/study-page.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { StudyPageViewComponent } from './study-page-view.component';
import studyPagesTr from '../../../../public/i18n/study-pages/tr.json';

const PAGE = {
  id: 9,
  title: 'Kesirler',
  description: 'Önce oku',
  isPublished: true,
  contentType: StudyPageContentType.Image,
  images: [],
  imageCount: 0,
  blocks: [
    { id: 1, sortOrder: 1, type: StudyPageBlockType.Text, text: 'Payda nedir?' },
    { id: 2, sortOrder: 2, type: StudyPageBlockType.Link, url: 'javascript:alert(1)', text: 'Kötü' },
    { id: 3, sortOrder: 3, type: StudyPageBlockType.Question, questionId: 11, questionText: 'Soru 1' },
    { id: 4, sortOrder: 4, type: StudyPageBlockType.Question, questionId: 12, questionText: 'Soru 2' },
    { id: 5, sortOrder: 5, type: StudyPageBlockType.Flashcard, text: 'Ön', backText: 'Arka' },
  ],
} as unknown as StudyPage;

function setup(isStudent = true, page: StudyPage = PAGE) {
  const studyPages = { getById: vi.fn().mockReturnValue(of(page)) };
  const practice = { startSession: vi.fn() };
  const router = { navigate: vi.fn() };
  const snackBar = { open: vi.fn() };
  TestBed.configureTestingModule({
    imports: [
      StudyPageViewComponent,
      translocoTestingModule({ langs: { 'study-pages/tr': studyPagesTr }, translocoConfig: { scopes: { keepCasing: true } } }),
    ],
    providers: [
      { provide: StudyPageService, useValue: studyPages },
      { provide: PracticeService, useValue: practice },
      { provide: AuthService, useValue: { hasRealmRole: (r: string) => isStudent && r === 'Student' } },
      { provide: Router, useValue: router },
      { provide: MatSnackBar, useValue: snackBar },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '9' }) } } },
    ],
  });
  const fixture = TestBed.createComponent(StudyPageViewComponent);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement, practice, router, snackBar };
}

describe('StudyPageViewComponent (#933)', () => {
  it('rendersBlocksInOrder_UnsafeLinkIsNotRendered', () => {
    const { el } = setup();
    const kinds = Array.from(el.querySelectorAll('[data-testid^="block-"]:not([data-testid="block-question-solve"])')).map((n) => n.getAttribute('data-testid'));
    expect(kinds).toEqual(['block-text', 'block-question', 'block-question', 'block-flashcard']);
  });

  it('flashcard_FlipsOnClick', () => {
    const { fixture, el } = setup();
    const card = el.querySelector<HTMLButtonElement>('[data-testid="block-flashcard"]')!;
    expect(card.textContent).toContain('Ön');
    card.click();
    fixture.detectChanges();
    expect(card.textContent).toContain('Arka');
  });

  it('solve_OpensAPracticeSessionForThatQuestion_AndNavigates', () => {
    const { el, practice, router } = setup();
    practice.startSession.mockReturnValue(of({ id: 77 }));
    el.querySelectorAll<HTMLButtonElement>('[data-testid="block-question-solve"]')[1].click();

    expect(practice.startSession).toHaveBeenCalledWith({ studyItemId: 9, studyQuestionId: 12 });
    expect(router.navigate).toHaveBeenCalledWith(['/practice'], { queryParams: { session: 77 } });
  });

  it('solveAll_SendsOnlyThePage_FailureShowsMessage', () => {
    const { el, practice, snackBar } = setup();
    practice.startSession.mockReturnValue(throwError(() => new Error('x')));
    el.querySelector<HTMLButtonElement>('[data-testid="study-page-solve-all"]')!.click();

    expect(practice.startSession).toHaveBeenCalledWith({ studyItemId: 9 });
    expect(snackBar.open).toHaveBeenCalled();
  });

  it('teacher_SeesQuestionsButCannotStartASession', () => {
    const { el } = setup(false);
    const solve = el.querySelector<HTMLButtonElement>('[data-testid="block-question-solve"]')!;
    expect(solve.disabled).toBe(true);
    expect(el.querySelector('[data-testid="study-page-solve-all"]')).toBeNull();
  });
});

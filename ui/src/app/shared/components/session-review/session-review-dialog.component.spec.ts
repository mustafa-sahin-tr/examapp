import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';
import { SessionReview, SessionReviewQuestion } from '../../../models/session-review';
import { TestService } from '../../../services/test.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import sessionReviewTr from '../../../../../public/i18n/session-review/tr.json';
import { SessionReviewDialogComponent } from './session-review-dialog.component';
import { maxStrokeY } from './session-question-view.component';

/** Issue #1310: öğretmenin, öğrencinin bitmiş testini soru soru ve karalamalarıyla görmesi. */
describe('SessionReviewDialogComponent (#1310)', () => {
  let fixture: ComponentFixture<SessionReviewDialogComponent>;
  let service: MockedObject<TestService>;

  const q = (n: number, outcome: SessionReviewQuestion['outcome'], extra: Partial<SessionReviewQuestion> = {}): SessionReviewQuestion => ({
    instanceQuestionId: 100 + n, number: n, imageUrl: '/img/q.png', interactionType: null, selectedLetter: 'A',
    correctLetter: 'A', outcome, timeTakenSeconds: 110, ink: [], strokes: [], text: null, ...extra,
  });
  const review: SessionReview = {
    testInstanceId: 9, title: 'Atasözleri', startedAt: '2026-11-23T10:00:00Z', finishedAt: '2026-11-23T10:14:00Z',
    questions: [
      q(1, 'correct'),
      q(2, 'wrong', { selectedLetter: 'B', correctLetter: 'C', ink: ['red:M10 10L20 300'], text: 'kara gün?' }),
      q(3, 'blank', { selectedLetter: null }),
      q(4, 'correct', { strokes: ['M1 2L3 4'] }),
    ],
  };

  async function setup(response = of(review)): Promise<HTMLElement> {
    service = { getSessionReview: vi.fn().mockReturnValue(response) } as unknown as MockedObject<TestService>;
    TestBed.configureTestingModule({
      imports: [SessionReviewDialogComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'session-review/tr': sessionReviewTr } })],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { worksheetId: 7, testInstanceId: 9, studentName: 'Ali' } },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: TestService, useValue: service },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang('tr');
    fixture = TestBed.createComponent(SessionReviewDialogComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const nums = (el: HTMLElement) => Array.from(el.querySelectorAll('[data-testid="srd-num"]')).map(text);

  it('opensOnQuestionsWithDrawings_AndShowsAnswers', async () => {
    const el = await setup();
    expect(service.getSessionReview).toHaveBeenCalledWith(7, 9);
    expect(text(el.querySelector('[data-testid="srd-summary"]'))).toBe('2 doğru · 1 yanlış · 1 boş · 2 soruda karalama');
    expect(nums(el)).toEqual(['2✎', '4✎']);
    expect(text(el.querySelector('[data-testid="srd-meta"]'))).toBe('Cevabı: B · Doğrusu: C · 1 dk 50 sn');
    expect(el.querySelector('[data-testid="sqv-ink"] path')?.getAttribute('d')).toBe('M10 10L20 300');
    expect(text(el.querySelector('[data-testid="sqv-note"]'))).toBe('kara gün?');
  });

  it('filters_AllQuestionsOrOnlyWrong', async () => {
    const el = await setup();
    fixture.componentInstance['onlyDrawings'].set(false);
    fixture.detectChanges();
    expect(nums(el)).toEqual(['1', '2✎', '3', '4✎']);
    fixture.componentInstance['onlyWrong'].set(true);
    fixture.detectChanges();
    expect(nums(el)).toEqual(['2✎']);
  });

  it('notFound_ShowsMessage', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 404 })) as never);
    expect(text(el)).toContain(sessionReviewTr.notFound);
  });

  it('maxStrokeY_ReadsYCoordinates', () => {
    expect(maxStrokeY(['red:M1 20L3 400', 'M5 6'])).toBe(400);
  });
});

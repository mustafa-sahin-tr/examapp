import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject, of, throwError } from 'rxjs';

import { AskTeacherQueueComponent } from './ask-teacher-queue.component';
import { AskTeacherQuestion } from '../../models/ask-teacher.model';
import { AskTeacherService } from '../../services/ask-teacher.service';
import { LocaleService } from '../../services/locale.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import queueTr from '../../../../public/i18n/ask-teacher-queue/tr.json';

/** Issue #1111: öğretmenin soru kuyruğu — bekleyenler, yazı / görselle cevap, hata, puan, bildirim linki. */
describe('AskTeacherQueueComponent (issue #1111)', () => {
  let fixture: ComponentFixture<AskTeacherQueueComponent>;
  let service: Record<string, ReturnType<typeof vi.fn>>;

  function question(id: number, overrides: Partial<AskTeacherQuestion> = {}): AskTeacherQuestion {
    return {
      id,
      status: 'Open',
      teacherId: 4,
      teacherName: '',
      studentName: `Öğrenci ${id}`,
      subjectId: 1,
      subjectName: 'Matematik',
      note: `Not ${id}`,
      createdAt: '2030-10-01T08:00:00Z',
      dueAt: new Date(Date.now() + 5.5 * 3_600_000).toISOString(),
      answeredAt: null,
      answerText: null,
      hasAnswerImage: false,
      rating: null,
      ...overrides,
    };
  }

  async function setup(byStatus: Record<string, AskTeacherQuestion[]>, queryQ: string | null = null) {
    service = {
      queue: vi.fn((status: string | null) => of(byStatus[status ?? 'all'] ?? [])),
      answer: vi.fn(),
      image: vi.fn().mockReturnValue(of(new Blob(['x'], { type: 'image/jpeg' }))),
    };
    TestBed.configureTestingModule({
      imports: [AskTeacherQueueComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'ask-teacher-queue/tr': queueTr } })],
      providers: [
        provideRouter([]),
        { provide: AskTeacherService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
        { provide: ActivatedRoute, useValue: { queryParamMap: new BehaviorSubject(convertToParamMap(queryQ ? { q: queryQ } : {})) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang('tr');
    fixture = TestBed.createComponent(AskTeacherQueueComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const q = (el: ParentNode, id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  it('lists waiting questions with the time left and answers one with text', async () => {
    const el = await setup({ Open: [question(1), question(2)] });
    expect(service['queue']).toHaveBeenCalledWith('Open');
    const items = el.querySelectorAll<HTMLElement>('[data-testid="queue-question"]');
    expect(items).toHaveLength(2);
    expect(q(items[0], 'queue-due')!.textContent).toContain('5 saat');
    expect((q(items[0], 'queue-answer-send') as HTMLButtonElement).disabled).toBe(true);

    const component = fixture.componentInstance;
    component.setText(1, 'Paydaları eşitle.');
    service['answer'].mockReturnValue(of(question(1, { status: 'Answered', answerText: 'Paydaları eşitle.' })));
    fixture.detectChanges();
    (q(items[0], 'queue-answer-send') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service['answer']).toHaveBeenCalledWith(1, 'Paydaları eşitle.', null);
    expect(component.questions().map((x) => x.id)).toEqual([2]);
  });

  it('answers with only an image and shows server errors inline', async () => {
    const el = await setup({ Open: [question(1)] });
    const component = fixture.componentInstance;
    const file = new File(['x'], 'a.jpg', { type: 'image/jpeg' });
    component.onImagePicked(1, { target: { files: [file], value: '' } } as unknown as Event);
    expect(component.canAnswer(1)).toBe(true);

    service['answer'].mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'askTeacher.invalidState' } })),
    );
    component.answer(component.questions()[0]);
    fixture.detectChanges();

    expect(service['answer']).toHaveBeenCalledWith(1, '', file);
    expect(q(el, 'queue-answer-error')!.textContent).toContain('artık cevaplanamaz');
  });

  it('a linked question that is no longer waiting is found under all, with the rating shown', async () => {
    const el = await setup(
      { Open: [question(1)], all: [question(1), question(7, { status: 'Answered', answerText: 'Cevap', rating: 4 })] },
      '7',
    );

    expect(fixture.componentInstance.filter()).toBe('all');
    const items = el.querySelectorAll<HTMLElement>('[data-testid="queue-question"]');
    expect(items[1].classList).toContain('aq__item--highlight');
    expect(q(items[1], 'queue-rating')!.textContent).toContain('4 / 5');
  });

  it('shows the empty waiting state', async () => {
    const el = await setup({ Open: [] });
    expect(q(el, 'queue-empty')!.textContent).toContain('Cevap bekleyen soru yok');
  });
});

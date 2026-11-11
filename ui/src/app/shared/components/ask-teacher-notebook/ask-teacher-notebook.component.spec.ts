import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import { AskTeacherNotebookComponent } from './ask-teacher-notebook.component';
import { AskTeacherQuestion } from '../../../models/ask-teacher.model';
import { AskTeacherService } from '../../../services/ask-teacher.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import askTr from '../../../../../public/i18n/ask-teacher/tr.json';

/** Issue #1111: yanlışlar defterinin "Öğretmene sorduklarım" bölümü. */
describe('AskTeacherNotebookComponent (issue #1111)', () => {
  const saved = {
    id: 5, status: 'Answered', teacherId: 4, teacherName: 'Elif Öğretmen', studentName: '', subjectId: 1, subjectName: 'Matematik',
    note: null, createdAt: '2030-10-01T08:00:00Z', dueAt: '2030-10-03T08:00:00Z', answeredAt: '2030-10-02T08:00:00Z',
    answerText: 'Paydaları eşitle.', hasAnswerImage: false, rating: null, inNotebook: true,
  } satisfies AskTeacherQuestion;

  function render(notebook: ReturnType<typeof vi.fn>) {
    const service = {
      notebook,
      setNotebook: vi.fn().mockReturnValue(of(undefined)),
      image: vi.fn().mockReturnValue(of(new Blob(['x'], { type: 'image/jpeg' }))),
    };
    TestBed.configureTestingModule({
      imports: [AskTeacherNotebookComponent, translocoTestingModule({ langs: { 'ask-teacher/tr': askTr } })],
      providers: [provideRouter([]), { provide: AskTeacherService, useValue: service }],
    });
    TestBed.inject(TranslocoService).setActiveLang('tr');
    const fixture = TestBed.createComponent(AskTeacherNotebookComponent);
    fixture.detectChanges();
    return { fixture, service, el: fixture.nativeElement as HTMLElement };
  }

  it('lists saved questions with the answer and removes one', () => {
    const { fixture, service, el } = render(vi.fn().mockReturnValue(of([saved])));

    expect(el.textContent).toContain('Öğretmene sorduklarım');
    expect(el.textContent).toContain('Paydaları eşitle.');
    expect(el.querySelector('.atn__actions a')!.getAttribute('href')).toBe('/ask-teacher?q=5');

    (el.querySelector('[data-testid="ask-notebook-remove"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service.setNotebook).toHaveBeenCalledWith(5, false);
    expect(el.querySelector('[data-testid="ask-notebook"]')).toBeNull();
  });

  it('renders nothing when empty or failing', () => {
    expect(render(vi.fn().mockReturnValue(throwError(() => new Error('x')))).el.querySelector('[data-testid="ask-notebook"]')).toBeNull();
  });
});

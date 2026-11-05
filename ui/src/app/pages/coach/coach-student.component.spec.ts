import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import coachTr from '../../../../public/i18n/coach/tr.json';
import { CoachGoal, CoachNote, CoachOverview } from '../../models/coach.model';
import { CoachService } from '../../services/coach.service';
import { LocaleService } from '../../services/locale.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { CoachStudentComponent, addDays } from './coach-student.component';

const goal = (id: number, title: string, weekStart = '2026-11-02'): CoachGoal => ({ id, title, weekStart, updatedAt: '2026-11-05T10:00:00Z' });
const note = (id: number, body: string): CoachNote => ({ id, body, createdAt: '2026-11-05T10:00:00Z', updatedAt: '2026-11-05T10:00:00Z' });

function overview(overrides: Partial<CoachOverview> = {}): CoachOverview {
  return {
    studentId: 7,
    fullName: 'Ada Yılmaz',
    plan: { weekStart: '2026-11-02', planned: 4, completed: 1, includesProgram: false },
    recentMockExams: [
      { id: 9, name: 'LGS Deneme 1', templateName: 'LGS', wrongAnswersPerCorrect: 3, complete: true, sections: [], correct: 60, wrong: 9, blank: 21, net: 57 },
    ],
    weakTopics: [
      {
        subjectId: 1, subjectName: 'Matematik', topicId: 4, topicName: 'Kesirler', subTopicId: null, subTopicName: null,
        correct: 3, wrong: 5, blank: 0, total: 8, accuracyPct: 37.5,
      } as CoachOverview['weakTopics'][number],
    ],
    activeMistakeCount: 12,
    goals: [goal(1, 'Kesirler testini bitir')],
    ...overrides,
  };
}

describe('CoachStudentComponent (issue #700)', () => {
  let service: Record<string, ReturnType<typeof vi.fn>>;
  let dialogResult: boolean;

  function create(response = of(overview())): ComponentFixture<CoachStudentComponent> {
    dialogResult = true;
    service = {
      getOverview: vi.fn().mockReturnValue(response),
      listGoals: vi.fn().mockReturnValue(of([])),
      addGoal: vi.fn(),
      updateGoal: vi.fn(),
      deleteGoal: vi.fn().mockReturnValue(of(undefined)),
      listNotes: vi.fn().mockReturnValue(of([note(2, 'Kesirlerde payda karışıyor')])),
      addNote: vi.fn(),
      updateNote: vi.fn(),
      deleteNote: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [CoachStudentComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'coach/tr': coachTr } })],
      providers: [
        provideRouter([]),
        { provide: CoachService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['studentId', '7']]) } } },
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(dialogResult) }) } },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
      ],
    });
    const fixture = TestBed.createComponent(CoachStudentComponent);
    fixture.detectChanges();
    return fixture;
  }

  const q = (fixture: ComponentFixture<CoachStudentComponent>, test: string): HTMLElement | null =>
    fixture.nativeElement.querySelector(`[data-test="${test}"]`);
  const all = (fixture: ComponentFixture<CoachStudentComponent>, test: string): HTMLElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll(`[data-test="${test}"]`));
  const text = (el: Element | null | undefined) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  function type(fixture: ComponentFixture<CoachStudentComponent>, test: string, value: string): void {
    const input = q(fixture, test) as HTMLInputElement | HTMLTextAreaElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('addDays_StaysOnTheCalendarDate', () => {
    expect(addDays('2026-10-26', 7)).toBe('2026-11-02');
  });

  it('overview_ShowsPlanMocksWeakTopicsMistakes_AndOutcomesLink', () => {
    const fixture = create();

    expect(text(q(fixture, 'coach-student-title'))).toBe('Ada Yılmaz');
    expect(text(q(fixture, 'coach-plan-value'))).toBe('4 işten 1 tamamlandı');
    expect(q(fixture, 'coach-plan-only-tests')).not.toBeNull();
    expect(text(q(fixture, 'coach-mock-net'))).toBe('57 net');
    expect(text(q(fixture, 'coach-weak-topic'))).toContain('Kesirler');
    expect(text(q(fixture, 'coach-weak-topic'))).toContain('%37,5 doğru · 8 soru');
    expect(text(q(fixture, 'coach-mistakes'))).toContain('12 çözülmemiş yanlış');
    expect(q(fixture, 'coach-outcomes')?.getAttribute('href')).toBe('/teacher/students/7/outcomes');
    expect(all(fixture, 'coach-goal-title').map(text)).toEqual(['Kesirler testini bitir']);
    expect(all(fixture, 'coach-note-body').map(text)).toEqual(['Kesirlerde payda karışıyor']);
  });

  it('notFound_ShowsOnlyTheMessage', () => {
    const fixture = create(throwError(() => new HttpErrorResponse({ status: 404 })));

    expect(q(fixture, 'coach-student-not-found')).not.toBeNull();
    expect(q(fixture, 'coach-goals')).toBeNull();
    expect(service['listNotes']).not.toHaveBeenCalled();
  });

  it('weeks_StartFromTheServersWeek_AndLoadTheChosenWeek', () => {
    const fixture = create();
    const component = fixture.componentInstance;

    expect(component.weeks().map((w) => w.value)).toEqual(['2026-11-02', '2026-11-09', '2026-11-16', '2026-11-23', '2026-11-30']);

    service['listGoals'].mockReturnValue(of([goal(5, 'Paragraf çalış', '2026-11-16')]));
    component.selectWeek('2026-11-16');
    fixture.detectChanges();

    expect(service['listGoals']).toHaveBeenCalledWith(7, '2026-11-16');
    expect(all(fixture, 'coach-goal-title').map(text)).toEqual(['Paragraf çalış']);
  });

  it('addGoal_SendsTheSelectedWeek_AndAppends', () => {
    const fixture = create();
    service['addGoal'].mockReturnValue(of(goal(3, 'Günde 20 soru')));

    type(fixture, 'coach-goal-input', '  Günde 20 soru ');
    (q(fixture, 'coach-goal-add') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service['addGoal']).toHaveBeenCalledWith(7, { title: 'Günde 20 soru', weekStart: '2026-11-02' });
    expect(all(fixture, 'coach-goal-title').map(text)).toEqual(['Kesirler testini bitir', 'Günde 20 soru']);
    expect((q(fixture, 'coach-goal-input') as HTMLInputElement).value).toBe('');
  });

  it('addGoal_RuleError_ShowsItsMessage', () => {
    const fixture = create();
    service['addGoal'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'MarkupNotAllowed' } })));

    type(fixture, 'coach-goal-input', '<a href="x">y</a>');
    (q(fixture, 'coach-goal-add') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(text(q(fixture, 'coach-goal-error'))).toBe('Hedefte bağlantı ya da görsel kullanılamaz.');
  });

  it('unknownErrorCode_FallsBackToGeneric', () => {
    const fixture = create();
    service['addGoal'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'Something' } })));

    type(fixture, 'coach-goal-input', 'x');
    (q(fixture, 'coach-goal-add') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(text(q(fixture, 'coach-goal-error'))).toBe('İşlem tamamlanamadı. Tekrar dene.');
  });

  it('goalLimit_HidesTheAddForm', () => {
    const goals = Array.from({ length: 10 }, (_, i) => goal(i + 1, `Hedef ${i + 1}`));
    const fixture = create(of(overview({ goals })));

    expect(q(fixture, 'coach-goal-input')).toBeNull();
    expect(text(q(fixture, 'coach-goal-limit'))).toBe('Bir haftaya en çok 10 hedef yazılabilir.');
  });

  it('editAndDeleteGoal_UpdateTheList', () => {
    const fixture = create();
    service['updateGoal'].mockReturnValue(of(goal(1, 'Kesirler testini iki kez çöz')));

    (q(fixture, 'coach-goal-edit') as HTMLButtonElement).click();
    fixture.detectChanges();
    type(fixture, 'coach-goal-edit-input', 'Kesirler testini iki kez çöz');
    (q(fixture, 'coach-goal-save') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service['updateGoal']).toHaveBeenCalledWith(7, 1, 'Kesirler testini iki kez çöz');
    expect(all(fixture, 'coach-goal-title').map(text)).toEqual(['Kesirler testini iki kez çöz']);

    (q(fixture, 'coach-goal-delete') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service['deleteGoal']).toHaveBeenCalledWith(7, 1);
    expect(q(fixture, 'coach-goals-empty')).not.toBeNull();
  });

  it('addNote_PrependsNewest', () => {
    const fixture = create();
    service['addNote'].mockReturnValue(of(note(4, 'Sonraki derste oran')));

    type(fixture, 'coach-note-input', 'Sonraki derste oran');
    (q(fixture, 'coach-note-add') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service['addNote']).toHaveBeenCalledWith(7, 'Sonraki derste oran');
    expect(all(fixture, 'coach-note-body').map(text)).toEqual(['Sonraki derste oran', 'Kesirlerde payda karışıyor']);
  });

  it('deleteNote_AsksFirst', () => {
    const fixture = create();

    dialogResult = false;
    (q(fixture, 'coach-note-delete') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service['deleteNote']).not.toHaveBeenCalled();

    dialogResult = true;
    (q(fixture, 'coach-note-delete') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service['deleteNote']).toHaveBeenCalledWith(7, 2);
    expect(q(fixture, 'coach-notes-empty')).not.toBeNull();
  });
});

import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ExamTemplatesComponent } from './exam-templates.component';
import { MockExamBuilderComponent } from './mock-exam-builder.component';
import { MockExamsComponent } from './mock-exams.component';
import { MockExamResultComponent, StudentMockExamsComponent } from './student-mock-exams.component';
import { GradesService } from '../../services/grades.service';
import { ExamTemplate, MockExamDetail, MockExamResult, MockExamService, StudentMockExam } from '../../services/mock-exam.service';
import { TestService } from '../../services/test.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import en from '../../../../public/i18n/mock-exams/en.json';
import tr from '../../../../public/i18n/mock-exams/tr.json';

const TEMPLATE: ExamTemplate = {
  id: 1, code: 'lgs', name: 'LGS', wrongAnswersPerCorrect: 3, sourceReference: 'MEB', inUse: false,
  sessions: [{ id: 1, name: 'Sözel', durationSeconds: 4500, sections: [{ id: 1, name: 'Türkçe', questionCount: 20 }] }],
};

function detail(overrides: Partial<MockExamDetail> = {}): MockExamDetail {
  return {
    id: 5, name: 'Deneme', examTemplateId: 1, templateName: 'LGS', gradeId: 8, gradeName: '8. Sınıf', publishedAt: null,
    sessions: [{ id: 1, name: 'Sözel', durationSeconds: 4500, worksheetId: 30, sections: [
      { sectionId: 1, groupName: null, name: 'Türkçe', requiredCount: 2, questionIds: [101, 102] },
      { sectionId: 2, groupName: 'Sosyal', name: 'Tarih', requiredCount: 1, questionIds: [103] },
    ] }],
    problems: [],
    ...overrides,
  };
}

describe('mock exam screens', () => {
  let service: MockedObject<MockExamService>;
  let tests: { startTest: ReturnType<typeof vi.fn> };
  let dialog: { open: ReturnType<typeof vi.fn> };
  let navigate: ReturnType<typeof vi.spyOn>;

  function setup(): void {
    service = {
      listTemplates: vi.fn().mockReturnValue(of([TEMPLATE])),
      saveTemplate: vi.fn().mockReturnValue(of(TEMPLATE)),
      listMine: vi.fn().mockReturnValue(of([{ id: 5, name: 'Deneme', templateName: 'LGS', gradeName: '8. Sınıf', publishedAt: null }])),
      create: vi.fn().mockReturnValue(of(detail())),
      get: vi.fn().mockReturnValue(of(detail())),
      setSectionQuestions: vi.fn().mockReturnValue(of(detail())),
      publish: vi.fn().mockReturnValue(of(detail({ publishedAt: '2026-11-04' }))),
      deleteDraft: vi.fn().mockReturnValue(of(undefined)),
      listForStudent: vi.fn().mockReturnValue(of([])),
      getResult: vi.fn(),
    } as unknown as MockedObject<MockExamService>;
    tests = { startTest: vi.fn().mockReturnValue(of({ instanceId: 77 })) };
    dialog = { open: vi.fn() };

    TestBed.configureTestingModule({
      imports: [NoopAnimationsModule, translocoTestingModule({ langs: { 'mock-exams/tr': tr } })],
      providers: [
        provideRouter([]),
        { provide: MockExamService, useValue: service },
        { provide: TestService, useValue: tests },
        { provide: GradesService, useValue: { getGrades: () => of([{ id: 8, name: '8. Sınıf' }]) } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    // Uygulamada rota girdileri bileşene bağlanmaz: kimlik gerçekteki gibi rotadan okunmalı.
    TestBed.inject(ActivatedRoute).snapshot.params = { id: '5' };
  }

  function builder(exam = detail()) {
    service.get.mockReturnValue(of(exam));
    const fixture = TestBed.createComponent(MockExamBuilderComponent);
    fixture.detectChanges();
    // Bileşen MatDialogModule'ü kendi içe aktarır: sahte, bileşenin gördüğü örneğin üzerine kurulur.
    vi.spyOn(fixture.debugElement.injector.get(MatDialog), 'open').mockImplementation(dialog.open as never);
    return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  }

  beforeEach(setup);

  it('translations_HaveTheSameKeysInBothLanguages', () => {
    const keys = (node: object, prefix = ''): string[] => Object.entries(node).flatMap(([k, v]) =>
      typeof v === 'object' ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`]);

    expect(keys(en).sort()).toEqual(keys(tr).sort());
  });

  describe('list', () => {
    it('create_NeedsTemplateGradeAndName_ThenOpensTheBuilder', () => {
      const fixture = TestBed.createComponent(MockExamsComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.canCreate()).toBe(false);
      component.create();
      expect(service.create).not.toHaveBeenCalled();

      component.templateId.set(1);
      component.gradeId.set(8);
      component.name.set('  Deneme 1 ');
      component.create();

      expect(service.create).toHaveBeenCalledWith(1, 'Deneme 1', 8);
      expect(navigate).toHaveBeenCalledWith(['/mock-exams', 5]);
    });

    it('create_Failure_SaysSoAndAllowsAnotherTry', () => {
      const fixture = TestBed.createComponent(MockExamsComponent);
      const component = fixture.componentInstance;
      service.create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
      component.templateId.set(1);
      component.gradeId.set(8);
      component.name.set('x');

      component.create();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain(tr.list.createFailed);
      expect(component.creating()).toBe(false);
    });

    it('showsDraftAndPublishedState', () => {
      const fixture = TestBed.createComponent(MockExamsComponent);
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain(tr.draft);
    });
  });

  describe('builder', () => {
    it('audience_StartsClosedBecauseOpenMeansAllSchools', () => {
      const { component } = builder();

      component.publish();

      expect(service.publish).toHaveBeenCalledWith(5, false);
    });

    it('published_OffersAssigningEachSession', () => {
      const { el } = builder(detail({ publishedAt: '2026-11-04' }));

      expect(el.querySelector('.me-assign')?.getAttribute('href')).toBe('/test/30');
    });

    it('complete_AllowsPublishingWithTheChosenAudience', () => {
      const { fixture, component, el } = builder();
      expect((el.querySelector('.me-publish') as HTMLButtonElement).disabled).toBe(false);

      component.openToGrade.set(true);
      component.publish();
      fixture.detectChanges();

      expect(service.publish).toHaveBeenCalledWith(5, true);
      expect(el.querySelector('.me-publish')).toBeNull();
      expect(el.textContent).toContain(tr.published);
      expect(service.get).toHaveBeenCalledWith(5);
    });

    it('wrongCount_BlocksPublishingAndNamesTheSubject', () => {
      const { component, el } = builder(detail({ problems: [{ code: 'WrongCount', section: 'Türkçe', required: 20, actual: 5 }] }));

      expect((el.querySelector('.me-publish') as HTMLButtonElement).disabled).toBe(true);
      component.publish();
      expect(service.publish).not.toHaveBeenCalled();
      expect(el.textContent).toContain('Türkçe');
      expect(el.textContent).toContain('20');
    });

    it('publishRefusedByServer_ShowsTheCurrentProblems', () => {
      const { fixture, component, el } = builder();
      service.publish.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { problems: [{ code: 'Unpublished', section: 'Tarih', questionId: 103 }] } })));

      component.publish();
      fixture.detectChanges();

      expect(el.textContent).toContain('#103');
      expect(component.busy()).toBe(false);
    });

    it('fullSection_CannotTakeMoreQuestions', () => {
      const { el } = builder();

      el.querySelectorAll<HTMLButtonElement>('.me-add').forEach((b) => expect(b.disabled).toBe(true));
    });

    it('addQuestions_SendsExistingAndNewAndBlocksUsedOnes', () => {
      const exam = detail();
      exam.sessions[0].sections[0].questionIds = [101];
      const { component } = builder(exam);
      dialog.open.mockReturnValue({ afterClosed: () => of([200]) });

      component.addQuestions(exam.sessions[0].sections[0]);

      expect(dialog.open.mock.calls[0][1].data.usedIds).toEqual([101, 103]);
      expect(service.setSectionQuestions).toHaveBeenCalledWith(5, 1, [101, 200]);
    });

    it('addQuestions_Cancelled_SendsNothing', () => {
      const exam = detail();
      const { component } = builder(exam);
      dialog.open.mockReturnValue({ afterClosed: () => of(undefined) });

      component.addQuestions(exam.sessions[0].sections[0]);

      expect(service.setSectionQuestions).not.toHaveBeenCalled();
    });

    it('removeQuestion_SendsTheRest', () => {
      const exam = detail();
      const { component } = builder(exam);

      component.removeQuestion(exam.sessions[0].sections[0], 101);

      expect(service.setSectionQuestions).toHaveBeenCalledWith(5, 1, [102]);
    });

    it.each([['TooManyQuestions'], ['QuestionUsedTwice'], ['QuestionNotUsable']])('serverRefusal_%s_ShowsTheReason', (code) => {
      const exam = detail();
      const { fixture, component, el } = builder(exam);
      service.setSectionQuestions.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: code } })));

      component.removeQuestion(exam.sessions[0].sections[0], 101);
      fixture.detectChanges();

      expect(el.textContent).toContain(tr.errors[code as keyof typeof tr.errors]);
      expect(component.busy()).toBe(false);
    });

    it('published_HidesEditing', () => {
      const { el } = builder(detail({ publishedAt: '2026-11-04' }));

      expect(el.querySelector('.me-add')).toBeNull();
      expect(el.querySelector('.me-delete')).toBeNull();
      expect(el.querySelector('.me-publish')).toBeNull();
    });

    it('deleteDraft_GoesBackToTheList', () => {
      const { component } = builder();

      component.deleteDraft();

      expect(service.deleteDraft).toHaveBeenCalledWith(5);
      expect(navigate).toHaveBeenCalledWith(['/mock-exams']);
    });

    it('loadFailure_SaysSo', () => {
      service.get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
      const fixture = TestBed.createComponent(MockExamBuilderComponent);
        fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain(tr.builder.loadFailed);
    });
  });

  describe('student', () => {
    const exam: StudentMockExam = {
      id: 5, name: 'Deneme', templateName: 'LGS', sessions: [
        { name: 'Sözel', worksheetId: 30, durationSeconds: 4500, questionCount: 50, status: 'Finished', instanceId: 9, canStart: true },
        { name: 'Sayısal', worksheetId: 31, durationSeconds: 4800, questionCount: 40, status: 'NotStarted', instanceId: null, canStart: true },
      ],
    };

    function list(exams: StudentMockExam[] = [exam]) {
      service.listForStudent.mockReturnValue(of(exams));
      const fixture = TestBed.createComponent(StudentMockExamsComponent);
      fixture.detectChanges();
      return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
    }

    it('notStarted_StartsTheSessionAndOpensThePlayer', () => {
      const { component } = list();

      component.start(exam.sessions[1]);

      expect(tests.startTest).toHaveBeenCalledWith(31);
      expect(navigate).toHaveBeenCalledWith(['/testsolve', 77]);
    });

    it('inProgress_OpensItWithoutStartingAgain', () => {
      const { component } = list();

      component.start({ ...exam.sessions[1], status: 'InProgress', instanceId: 12 });

      expect(tests.startTest).not.toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(['/testsolve', 12]);
    });

    it('startFailure_SaysSo', () => {
      const { fixture, component, el } = list();
      tests.startTest.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 })));

      component.start(exam.sessions[1]);
      fixture.detectChanges();

      expect(el.textContent).toContain(tr.student.startFailed);
    });

    it('finishedSession_HasNoStartButtonAndOffersTheResult', () => {
      const { el } = list();

      expect(el.querySelectorAll('.me-start').length).toBe(1);
      expect(el.querySelector('.me-result-link')).not.toBeNull();
    });

    it('nothingFinished_OffersNoResult', () => {
      const { el } = list([{ ...exam, sessions: [exam.sessions[1]] }]);

      expect(el.querySelector('.me-result-link')).toBeNull();
    });

    it('sessionNotAssigned_CannotBeStarted', () => {
      const { el } = list([{ ...exam, sessions: [{ ...exam.sessions[1], canStart: false }] }]);

      expect(el.querySelector('.me-start')).toBeNull();
      expect(el.textContent).toContain(tr.student.notAssigned);
    });

    it('noExams_SaysSo', () => {
      const { el } = list([]);

      expect(el.textContent).toContain(tr.student.empty);
    });
  });

  describe('result', () => {
    const result: MockExamResult = {
      id: 5, name: 'Deneme', templateName: 'LGS', wrongAnswersPerCorrect: 3, complete: false, correct: 15, wrong: 3, blank: 2, net: 14,
      sections: [
        { groupName: null, name: 'Türkçe', questionCount: 20, correct: 15, wrong: 3, blank: 2, net: 14, pending: false },
        { groupName: null, name: 'Matematik', questionCount: 20, correct: 0, wrong: 0, blank: 0, net: 0, pending: true },
      ],
    };

    function view() {
      service.getResult.mockReturnValue(of(result));
      const fixture = TestBed.createComponent(MockExamResultComponent);
        fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('showsCorrectWrongBlankNetPerSubjectAndTotal', () => {
      const el = view();
      const turkish = Array.from(el.querySelectorAll('tbody tr')).find((r) => r.textContent?.includes('Türkçe'))!;

      expect(Array.from(turkish.querySelectorAll('td')).map((c) => c.textContent?.trim())).toEqual(['15', '3', '2', '14']);
      expect(el.querySelector('.me-total')?.textContent).toContain('14');
    });

    it('unfinishedSession_ShowsNoNumbersAndWarns', () => {
      const el = view();
      const math = Array.from(el.querySelectorAll('tbody tr')).find((r) => r.textContent?.includes('Matematik'))!;

      expect(math.textContent).toContain(tr.result.pending);
      expect(el.textContent).toContain(tr.result.incomplete);
    });

    it('showsTheWrongAnswerRule', () => {
      expect(view().textContent).toContain(tr.result.rule.replace('{{count}}', '3'));
    });
  });

  describe('templates', () => {
    function view() {
      const fixture = TestBed.createComponent(ExamTemplatesComponent);
      fixture.detectChanges();
      return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
    }

    it('newTemplate_CannotBeSavedUntilComplete', () => {
      const { component } = view();

      component.draft.set(component.newTemplate());
      expect(component.canSave()).toBe(false);
      component.save();
      expect(service.saveTemplate).not.toHaveBeenCalled();

      component.change({ code: 'x', name: 'X' });
      component.changeSession(0, { name: 'Tek' });
      component.changeSection(0, 0, { name: 'Ders', questionCount: 5 });
      expect(component.canSave()).toBe(true);

      component.save();
      expect(service.saveTemplate.mock.calls[0][0].sessions[0].sections[0]).toEqual({ name: 'Ders', questionCount: 5 });
      expect(component.draft()).toBeNull();
    });

    it('addSessionAndSubject_ExtendTheForm', () => {
      const { component } = view();
      component.draft.set(component.newTemplate());

      component.addSection(0);
      component.addSession();

      expect(component.draft()!.sessions.length).toBe(2);
      expect(component.draft()!.sessions[0].sections.length).toBe(2);
    });

    it('editingACopy_DoesNotChangeTheList', () => {
      const { component } = view();

      component.edit(TEMPLATE);
      component.change({ name: 'Başka' });

      expect(component.templates()[0].name).toBe('LGS');
    });

    it('templateInUse_CannotBeEdited', () => {
      service.listTemplates.mockReturnValue(of([{ ...TEMPLATE, inUse: true }]));
      const { el } = view();

      expect(el.querySelector('.me-edit')).toBeNull();
      expect(el.textContent).toContain(tr.templates.inUse);
    });

    it('saveRefused_ShowsTheReasonAndKeepsTheForm', () => {
      const { fixture, component, el } = view();
      service.saveTemplate.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'TemplateCodeTaken' } })));
      component.edit(TEMPLATE);

      component.save();
      fixture.detectChanges();

      expect(el.textContent).toContain(tr.errors.TemplateCodeTaken);
      expect(component.draft()).not.toBeNull();
    });
  });
});

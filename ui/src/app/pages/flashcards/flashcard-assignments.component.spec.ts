import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import flashcardsTr from '../../../../public/i18n/flashcards/tr.json';
import { FlashcardDeckAssignment } from '../../models/flashcard.model';
import { AuthService } from '../../services/auth.service';
import { FlashcardService } from '../../services/flashcard.service';
import { GradesService } from '../../services/grades.service';
import { StudentService } from '../../services/student.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { FlashcardAssignmentsComponent } from './flashcard-assignments.component';

const row = (id: number): FlashcardDeckAssignment => ({ id, studentId: id, studentName: `Student ${id}`, gradeId: null, gradeName: null });

describe('FlashcardAssignmentsComponent (issue #699)', () => {
  let service: Record<'listAssignments' | 'assign' | 'unassign', ReturnType<typeof vi.fn>>;

  function create(rows: FlashcardDeckAssignment[], schoolId: number | null = 3): ComponentFixture<FlashcardAssignmentsComponent> {
    vi.spyOn(AuthService, 'schoolIdOf').mockReturnValue(schoolId);
    service = {
      listAssignments: vi.fn().mockReturnValue(of(rows)),
      assign: vi.fn().mockReturnValue(of(row(8))),
      unassign: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [FlashcardAssignmentsComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'flashcards/tr': flashcardsTr } })],
      providers: [
        { provide: FlashcardService, useValue: service },
        { provide: GradesService, useValue: { getGrades: () => of([{ id: 1, name: '5' }]) } },
        { provide: StudentService, useValue: { getLookup: () => of([]) } },
        { provide: AuthService, useValue: { user: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(FlashcardAssignmentsComponent);
    fixture.componentRef.setInput('deckId', 4);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => vi.restoreAllMocks());

  it('independentTeacher_CannotChooseAGrade', () => {
    expect(create([], null).nativeElement.querySelector('[data-test="assign-grade"]')).toBeNull();
  });

  it('schoolTeacher_CanChooseAGrade', () => {
    expect(create([]).nativeElement.querySelector('[data-test="assign-grade"]')).not.toBeNull();
  });

  it('assignStudent_AddsTheRow_AndClearsTheChoice', () => {
    const c = create([]).componentInstance;
    c.studentId.set(8);
    c.assignStudent();
    expect(service.assign).toHaveBeenCalledWith(4, { studentId: 8 });
    expect(c.assignments().map((a) => a.id)).toEqual([8]);
    expect(c.studentId()).toBeNull();
  });

  it('sameAssignmentAgain_DoesNotAddASecondRow', () => {
    const c = create([row(8)]).componentInstance;
    c.studentId.set(8);
    c.assignStudent();
    expect(c.assignments().map((a) => a.id)).toEqual([8]);
  });

  it('assignGrade_SendsOnlyTheGrade', () => {
    const c = create([]).componentInstance;
    c.gradeId.set(1);
    c.assignGrade();
    expect(service.assign).toHaveBeenCalledWith(4, { gradeId: 1 });
  });

  it('ruleError_ShowsItsMessage', () => {
    const c = create([]).componentInstance;
    service.assign.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'AlreadyAssigned' } })));
    c.gradeId.set(1);
    c.assignGrade();
    expect(c.errorKey()).toBe('AlreadyAssigned');
    expect(c.gradeId()).toBe(1);
  });

  it('remove_DropsTheRow', () => {
    const c = create([row(1), row(2)]).componentInstance;
    c.remove(row(1));
    expect(service.unassign).toHaveBeenCalledWith(4, 1);
    expect(c.assignments().map((a) => a.id)).toEqual([2]);
  });
});

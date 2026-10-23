import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatSelectChange } from '@angular/material/select';
import { of, throwError } from 'rxjs';

import { DailyScopePickerComponent } from './daily-scope-picker.component';
import { DailyStartScope } from '../../../models/practice';
import { StudentProfile } from '../../../models/student-profile';
import { StudentService } from '../../../services/student.service';
import { SubjectService } from '../../../services/subject.service';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('DailyScopePickerComponent (issue #66)', () => {
    let fixture: ComponentFixture<DailyScopePickerComponent>;
    let component: DailyScopePickerComponent;
    let studentService: MockedObject<StudentService>;
    let subjectService: MockedObject<SubjectService>;
    let emitted: Array<DailyStartScope | null>;

    const select = (value: number | null) => ({ value }) as MatSelectChange;
    const field = (name: string) => fixture.debugElement.query(By.css(`[data-field="${name}"]`));

    function create(gradeId = 7): void {
        studentService.getProfile.mockReturnValue(of({ gradeId } as Partial<StudentProfile> as StudentProfile));
        fixture = TestBed.createComponent(DailyScopePickerComponent);
        component = fixture.componentInstance;
        emitted = [];
        component.scopeChange.subscribe((s) => emitted.push(s));
        fixture.detectChanges();
    }

    beforeEach(() => {
        studentService = {
            getProfile: vi.fn().mockName("StudentService.getProfile")
        } as unknown as MockedObject<StudentService>;
        subjectService = {
            getSubjectsByGrade: vi.fn().mockName("SubjectService.getSubjectsByGrade"),
            getTopicsBySubjectAndGrade: vi.fn().mockName("SubjectService.getTopicsBySubjectAndGrade"),
            getSubTopicsByTopic: vi.fn().mockName("SubjectService.getSubTopicsByTopic")
        } as unknown as MockedObject<SubjectService>;
        subjectService.getSubjectsByGrade.mockReturnValue(of([{ id: 3, name: 'Matematik' }, { id: 4, name: 'Türkçe' }]));
        subjectService.getTopicsBySubjectAndGrade.mockReturnValue(of([{ id: 30, name: 'Kesirler', subjectId: 3, gradeId: 7 }]));
        subjectService.getSubTopicsByTopic.mockReturnValue(of([{ id: 300, name: 'Sadeleştirme', topicId: 30 }]));

        TestBed.configureTestingModule({
            imports: [DailyScopePickerComponent, translocoTestingModule()],
            providers: [
                provideNoopAnimations(),
                { provide: StudentService, useValue: studentService },
                { provide: SubjectService, useValue: subjectService },
            ],
        });
    });

    it('init_LoadsSubjectsForStudentGrade_ShowsOnlySubjectField', () => {
        create();

        expect(subjectService.getSubjectsByGrade).toHaveBeenCalledTimes(1);

        expect(subjectService.getSubjectsByGrade).toHaveBeenCalledWith(7);
        expect(component.subjects().length).toBe(2);
        expect(field('subject')).toBeTruthy();
        expect(field('topic')).toBeNull();
        expect(field('subTopic')).toBeNull();
    });

    it('init_NoGrade_DoesNotRequestSubjects', () => {
        create(0);

        expect(subjectService.getSubjectsByGrade).not.toHaveBeenCalled();
        expect(component.subjects()).toEqual([]);
    });

    it('init_ProfileError_ShowsErrorState', () => {
        studentService.getProfile.mockReturnValue(throwError(() => new Error('x')));
        fixture = TestBed.createComponent(DailyScopePickerComponent);
        fixture.detectChanges();

        expect(fixture.componentInstance.error()).toBe(true);
        expect(fixture.debugElement.query(By.css('[role="alert"]'))).toBeTruthy();
    });

    it('subjectThenTopicThenSubTopic_LoadsOptionsAndEmitsNarrowingScope', () => {
        create();

        component.onSubjectChange(select(3));
        fixture.detectChanges();
        expect(subjectService.getTopicsBySubjectAndGrade).toHaveBeenCalledTimes(1);
        expect(subjectService.getTopicsBySubjectAndGrade).toHaveBeenCalledWith(3, 7);
        expect(field('topic')).toBeTruthy();

        component.onTopicChange(select(30));
        fixture.detectChanges();
        expect(subjectService.getSubTopicsByTopic).toHaveBeenCalledTimes(1);
        expect(subjectService.getSubTopicsByTopic).toHaveBeenCalledWith(30);
        expect(field('subTopic')).toBeTruthy();

        component.onSubTopicChange(select(300));

        expect(emitted).toEqual([
            { subjectId: 3, topicId: null, subTopicId: null },
            { subjectId: 3, topicId: 30, subTopicId: null },
            { subjectId: 3, topicId: 30, subTopicId: 300 },
        ]);
    });

    it('changingSubject_ResetsTopicAndSubTopic', () => {
        create();
        component.onSubjectChange(select(3));
        component.onTopicChange(select(30));
        component.onSubTopicChange(select(300));

        component.onSubjectChange(select(4));

        expect(component.topicId()).toBeNull();
        expect(component.subTopicId()).toBeNull();
        expect(emitted[emitted.length - 1]).toEqual({ subjectId: 4, topicId: null, subTopicId: null });
    });

    it('clearingSubject_EmitsNull', () => {
        create();
        component.onSubjectChange(select(3));

        component.onSubjectChange(select(null));
        fixture.detectChanges();

        expect(emitted[emitted.length - 1]).toBeNull();
        expect(field('topic')).toBeNull();
    });
});

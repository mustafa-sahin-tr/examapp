import type { MockedObject } from "vitest";
// #545 — Angular 21 control-flow migrasyonu (@for grade/subject/topic/subtopic, @if seviye ve AI ikonu) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { ClassificationSelection, ClassificationSelectorComponent } from './classification-selector.component';
import { StudentService } from '../../../services/student.service';
import { SubjectService } from '../../../services/subject.service';
import { ClassificationSource } from '../../../models/draws';
import { Grade } from '../../../models/student';
import { Subject } from '../../../models/subject';
import { Topic } from '../../../models/topic';
import { SubTopic } from '../../../models/subtopic';
import { translocoTestingModule } from '../../testing/transloco-testing';

const GRADES = [
    { id: 1, name: '5. Sınıf' },
    { id: 2, name: '6. Sınıf' },
];
const SUBJECTS = [{ id: 10, name: 'Matematik' }];
const TOPICS = [{ id: 100, name: 'Kesirler', gradeId: 1 }];
const SUBTOPICS = [
    { id: 1000, name: 'Denk kesirler' },
    { id: 1001, name: 'Kesirleri sadeleştirme' },
];

describe('ClassificationSelectorComponent (#545)', () => {
    let fixture: ComponentFixture<ClassificationSelectorComponent>;
    let students: MockedObject<StudentService>;
    let subjects: MockedObject<SubjectService>;

    const el = () => fixture.nativeElement as HTMLElement;
    const levelTitles = () => Array.from(el().querySelectorAll('.level-title')).map((h) => h.textContent?.trim());
    const chips = () => Array.from(el().querySelectorAll('mat-chip-option'));
    const applyButton = () => el().querySelector('.apply-actions button') as HTMLButtonElement | null;

    function setup(options: {
        subjects?: unknown[];
        topics?: unknown[];
        subtopics?: unknown[];
    } = {}): void {
        students = {
            loadGrades: vi.fn().mockName("StudentService.loadGrades")
        } as unknown as MockedObject<StudentService>;
        students.loadGrades.mockReturnValue(of(GRADES as unknown as Grade[]));
        subjects = {
            getSubjectsByGrade: vi.fn().mockName("SubjectService.getSubjectsByGrade"),
            getTopicsBySubjectAndGrade: vi.fn().mockName("SubjectService.getTopicsBySubjectAndGrade"),
            getSubTopicsByTopic: vi.fn().mockName("SubjectService.getSubTopicsByTopic")
        } as unknown as MockedObject<SubjectService>;
        subjects.getSubjectsByGrade.mockReturnValue(of((options.subjects ?? SUBJECTS) as unknown as Subject[]));
        subjects.getTopicsBySubjectAndGrade.mockReturnValue(of((options.topics ?? TOPICS) as unknown as Topic[]));
        subjects.getSubTopicsByTopic.mockReturnValue(of((options.subtopics ?? SUBTOPICS) as unknown as SubTopic[]));

        TestBed.configureTestingModule({
            imports: [ClassificationSelectorComponent, NoopAnimationsModule, translocoTestingModule()],
            providers: [
                { provide: StudentService, useValue: students },
                { provide: SubjectService, useValue: subjects },
            ],
        });
        fixture = TestBed.createComponent(ClassificationSelectorComponent);
    }

    /** Effect'ler ve senkron servis yanıtları birkaç tur içinde şablona yansır. */
    function render(): void {
        fixture.detectChanges();
        fixture.detectChanges();
    }

    const FULL: ClassificationSelection = {
        gradeId: 1,
        subjectId: 10,
        topicId: 100,
        subtopicIds: [1000],
        classificationSource: ClassificationSource.Human,
    };

    it('template_NoInitialSelection_ShowsOnlyGradeLevel', () => {
        setup();
        render();

        expect(levelTitles()).toEqual(['Sınıf']);
        expect(chips().map((c) => c.textContent?.trim())).toEqual(['5. Sınıf', '6. Sınıf']);
        expect(applyButton()).toBeNull();
    });

    it('template_FullInitialSelection_ShowsEveryLevelAndSelectedSubtopic', () => {
        setup();
        fixture.componentRef.setInput('initialSelection', FULL);
        render();

        expect(levelTitles()).toEqual(['Sınıf', 'Ders', 'Konu', 'Alt Konular (Çoklu Seçim)']);
        const subtopic = chips().find((c) => c.textContent?.includes('Denk kesirler'));
        const other = chips().find((c) => c.textContent?.includes('Kesirleri sadeleştirme'));
        expect(subtopic?.querySelector('[role="option"]')?.getAttribute('aria-selected')).toBe('true');
        expect(other?.querySelector('[role="option"]')?.getAttribute('aria-selected')).toBe('false');
    });

    it('template_SubjectListEmpty_HidesSubjectAndLowerLevels', () => {
        setup({ subjects: [] });
        fixture.componentRef.setInput('initialSelection', { ...FULL, subjectId: null, topicId: null, subtopicIds: [] });
        render();

        expect(levelTitles()).toEqual(['Sınıf']);
    });

    it('template_AiSource_ShowsSparkleIconOnChips', () => {
        setup();
        fixture.componentRef.setInput('initialSelection', { ...FULL, classificationSource: ClassificationSource.AI });
        render();

        const icons = el().querySelectorAll('mat-icon');
        const sparkles = Array.from(icons).filter((i) => i.textContent?.trim() === 'auto_awesome');
        expect(sparkles.length).toBeGreaterThan(0);
    });

    it('template_HumanSource_HasNoSparkleIcons', () => {
        setup();
        fixture.componentRef.setInput('initialSelection', FULL);
        render();

        const sparkles = Array.from(el().querySelectorAll('mat-icon')).filter((i) => i.textContent?.trim() === 'auto_awesome');
        expect(sparkles.length).toBe(0);
    });

    it('template_ShowApplyButtonFalse_HidesApplyButton', () => {
        setup();
        fixture.componentRef.setInput('initialSelection', FULL);
        render();

        expect(applyButton()).toBeNull();
    });

    it('template_ApplyButtonWithoutTopic_IsDisabled', () => {
        setup();
        fixture.componentRef.setInput('showApplyButton', true);
        fixture.componentRef.setInput('initialSelection', { ...FULL, topicId: null, subtopicIds: [] });
        render();

        expect(applyButton()).not.toBeNull();
        expect(applyButton()?.disabled).toBe(true);
    });

    it('onApply_ApplyClickedWithFullSelection_EmitsSelection', () => {
        setup();
        fixture.componentRef.setInput('showApplyButton', true);
        fixture.componentRef.setInput('initialSelection', FULL);
        render();
        const emitted: ClassificationSelection[] = [];
        fixture.componentInstance.apply.subscribe((s: ClassificationSelection) => emitted.push(s));

        expect(applyButton()?.disabled).toBe(false);
        applyButton()?.click();

        expect(emitted).toEqual([
            {
                gradeId: 1,
                subjectId: 10,
                topicId: 100,
                subtopicIds: [1000],
                classificationSource: ClassificationSource.Human,
            },
        ]);
    });

    it('toggleSubtopic_SelectedTwice_AddsThenRemovesSubtopic', () => {
        setup();
        fixture.componentInstance.toggleSubtopic(1001);
        expect(fixture.componentInstance.isSubtopicSelected(1001)).toBe(true);

        fixture.componentInstance.toggleSubtopic(1001);
        expect(fixture.componentInstance.isSubtopicSelected(1001)).toBe(false);
    });
});

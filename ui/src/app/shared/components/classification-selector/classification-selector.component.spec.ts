import type { MockedObject } from "vitest";
// #545 — Angular 21 control-flow migrasyonu (@for grade/subject/topic/subtopic, @if seviye ve AI ikonu) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Subject as RxSubject, of } from 'rxjs';
import { By } from '@angular/platform-browser';
import { MatSelect } from '@angular/material/select';

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

    it('template_BookSource_ShowsBookIconWithLabel_NoSparkle', () => {
        setup();
        fixture.componentRef.setInput('initialSelection', { ...FULL, classificationSource: ClassificationSource.Book });
        render();

        const icons = Array.from(el().querySelectorAll('mat-icon'));
        const books = icons.filter((i) => i.textContent?.trim() === 'menu_book');
        expect(books.length).toBeGreaterThan(0);
        expect(books[0].getAttribute('aria-label')).toBe('Kitap');
        expect(icons.some((i) => i.textContent?.trim() === 'auto_awesome')).toBe(false);
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
    describe('layout="selects"', () => {
        const selectByTestId = (id: string) =>
            fixture.debugElement.query(By.css(`[data-testid="${id}"]`))?.injector.get(MatSelect);

        /** CDK 21+ native popover: panel tetikleyicinin yanında açılır, `document` üzerinden aranır. */
        function open(id: string): HTMLElement[] {
            selectByTestId(id)!.open();
            fixture.detectChanges();
            return Array.from(document.querySelectorAll('mat-option'));
        }

        afterEach(() => {
            for (const select of fixture.debugElement.queryAll(By.directive(MatSelect))) {
                select.componentInstance.close();
            }
            fixture.detectChanges();
        });

        it('template_SelectsLayout_RendersFourSelectsWithoutChips', () => {
            setup();
            fixture.componentRef.setInput('layout', 'selects');
            render();

            expect(fixture.debugElement.queryAll(By.directive(MatSelect)).length).toBe(4);
            expect(chips().length).toBe(0);
            expect(levelTitles()).toEqual([]);
            expect(selectByTestId('grade-select')!.disabled).toBe(false);
            expect(selectByTestId('subject-select')!.disabled).toBe(true);
            expect(selectByTestId('topic-select')!.disabled).toBe(true);
            expect(selectByTestId('subtopic-select')!.disabled).toBe(true);
        });

        it('selectGrade_GradeOptionPicked_EnablesAndFillsSubjectSelect', () => {
            setup();
            fixture.componentRef.setInput('layout', 'selects');
            render();

            const gradeOptions = open('grade-select');
            expect(gradeOptions.map((o) => o.textContent?.trim())).toEqual(['Tümü', '5. Sınıf', '6. Sınıf']);
            gradeOptions[1].click();
            render();

            expect(fixture.componentInstance.selectedGrade()).toBe(1);
            expect(subjects.getSubjectsByGrade).toHaveBeenCalledWith(1);
            expect(selectByTestId('subject-select')!.disabled).toBe(false);
            expect(selectByTestId('topic-select')!.disabled).toBe(true);

            const subjectOptions = open('subject-select');
            expect(subjectOptions.map((o) => o.textContent?.trim())).toEqual(['Tümü', 'Matematik']);
        });

        it('selectGrade_AnyOptionPicked_ClearsLowerLevels', () => {
            setup();
            fixture.componentRef.setInput('layout', 'selects');
            fixture.componentRef.setInput('initialSelection', FULL);
            render();

            open('grade-select')[0].click();
            render();

            expect(fixture.componentInstance.selectedGrade()).toBeNull();
            expect(fixture.componentInstance.selectedSubject()).toBeNull();
            expect(fixture.componentInstance.selectedTopic()).toBeNull();
            expect(fixture.componentInstance.selectedSubtopics()).toEqual([]);
            expect(selectByTestId('subject-select')!.disabled).toBe(true);
        });

        it('initialSelection_SubtopicsArriveAsync_SelectsShowSelectedValues', async () => {
            setup();
            const subtopics$ = new RxSubject<SubTopic[]>();
            subjects.getSubTopicsByTopic.mockReturnValue(subtopics$);
            fixture.componentRef.setInput('layout', 'selects');
            fixture.componentRef.setInput('initialSelection', { ...FULL, subtopicIds: [1001] });
            render();

            expect(selectByTestId('grade-select')!.value).toBe(1);
            expect(selectByTestId('subject-select')!.value).toBe(10);
            expect(selectByTestId('topic-select')!.value).toBe(100);
            expect(selectByTestId('subtopic-select')!.disabled).toBe(true); // liste henüz gelmedi

            subtopics$.next(SUBTOPICS as unknown as SubTopic[]);
            render();
            await fixture.whenStable(); // MatSelect seçenekler değişince seçimi mikro görevde yeniden kurar
            fixture.detectChanges();

            const subtopicSelect = selectByTestId('subtopic-select')!;
            expect(subtopicSelect.disabled).toBe(false);
            expect(subtopicSelect.value).toEqual([1001]);
            const selected = subtopicSelect.selected as { value: number }[];
            expect(selected.map((o) => o.value)).toEqual([1001]);
            const trigger = el().querySelector('[data-testid="subtopic-select"] .mat-mdc-select-value-text');
            expect(trigger?.textContent?.trim()).toBe('Kesirleri sadeleştirme');
            expect(el().querySelector('[data-testid="topic-select"] .mat-mdc-select-value-text')?.textContent?.trim()).toBe('Kesirler');
        });

        it('selectSubtopics_MultipleOptionsPicked_EmitsSelectionChange', async () => {
            setup();
            fixture.componentRef.setInput('layout', 'selects');
            fixture.componentRef.setInput('initialSelection', { ...FULL, subtopicIds: [] });
            render();
            await Promise.resolve(); // initialSelection'ın emit bastırması mikro görevde kalkar
            const emitted: ClassificationSelection[] = [];
            fixture.componentInstance.selectionChange.subscribe((s: ClassificationSelection) => emitted.push(s));

            expect(selectByTestId('subtopic-select')!.disabled).toBe(false);
            const options = open('subtopic-select');
            expect(options.map((o) => o.textContent?.trim())).toEqual(['Denk kesirler', 'Kesirleri sadeleştirme']);
            options[0].click();
            fixture.detectChanges();
            options[1].click();
            fixture.detectChanges();

            expect(fixture.componentInstance.selectedSubtopics()).toEqual([1000, 1001]);
            expect(emitted.at(-1)).toEqual({
                gradeId: 1,
                subjectId: 10,
                topicId: 100,
                subtopicIds: [1000, 1001],
                classificationSource: ClassificationSource.Human,
            });
        });
    });
});

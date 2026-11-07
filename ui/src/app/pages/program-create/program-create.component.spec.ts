import type { MockedObject } from 'vitest';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { PROGRAM_STEPS, ProgramCreateComponent } from './program-create.component';
import { ProgramService } from '../../services/program.service';
import { LocaleService } from '../../services/locale.service';
import { CreateProgramRequest, UserProgram } from '../../models/program.interfaces';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import programCreateTr from '../../../../public/i18n/program-create/tr.json';
import programCreateEn from '../../../../public/i18n/program-create/en.json';

const SUBJECTS = [
    { id: 1, name: 'Fen Bilimleri' },
    { id: 2, name: 'Matematik' },
    { id: 3, name: 'Türkçe' },
];

describe('ProgramCreateComponent (#885)', () => {
    let fixture: ComponentFixture<ProgramCreateComponent>;
    let el: HTMLElement;
    let service: MockedObject<ProgramService>;
    let snackBar: { open: ReturnType<typeof vi.fn> };

    function setup(subjects = of(SUBJECTS)): void {
        service = {
            getProgramSubjects: vi.fn().mockReturnValue(subjects),
            createProgram: vi.fn().mockReturnValue(of({ id: 7 } as UserProgram)),
        } as unknown as MockedObject<ProgramService>;
        snackBar = { open: vi.fn() };
        TestBed.configureTestingModule({
            imports: [ProgramCreateComponent, translocoTestingModule({ langs: { 'program-create/tr': programCreateTr } })],
            providers: [
                provideRouter([]),
                provideNoopAnimations(),
                provideNativeDateAdapter(),
                { provide: ProgramService, useValue: service },
                { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
            ],
        });
        TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
        fixture = TestBed.createComponent(ProgramCreateComponent);
        fixture.detectChanges();
        el = fixture.nativeElement as HTMLElement;
    }

    const q = <T extends HTMLElement = HTMLElement>(sel: string) => el.querySelector<T>(sel);
    const text = (sel: string) => q(sel)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
    function click(sel: string): void {
        q(sel)!.click();
        fixture.detectChanges();
    }
    const cmp = () => fixture.componentInstance;

    it('hasFourSteps_AndStartsWithHowMuch', () => {
        setup();
        expect(PROGRAM_STEPS.length).toBe(4);
        expect(el.querySelectorAll('.pc-steps li').length).toBe(4);
        expect(text('#pc-question')).toBe(programCreateTr.amount.question);
        // Süre ve günde kaç ders aynı ekranda.
        expect(q('[data-test="durations"]')).not.toBeNull();
        expect(q('[data-test="per-day"]')).not.toBeNull();
    });

    it('questionMode_SwapsDurationForQuestionCount', () => {
        setup();
        click('[data-test="type-question"]');
        expect(q('[data-test="durations"]')).toBeNull();
        expect(q('[data-test="questions"]')).not.toBeNull();
    });

    it('preview_UsesTheGradeSubjects_AndUpdatesWithEveryChoice', () => {
        setup();
        const preview = () => text('[data-test="preview"]');
        expect(preview()).toContain('Fen Bilimleri');
        expect(preview()).toContain('25 dk');

        cmp().duration.set('50-10');
        fixture.detectChanges();
        expect(preview()).toContain('50 dk');

        cmp().studyDays.set([]);
        fixture.detectChanges();
        expect(el.querySelectorAll('.pc-preview__rest').length).toBe(7);
    });

    it('days_NeedAtLeastOne_BeforeContinuing', () => {
        setup();
        click('[data-test="next"]');
        expect(text('#pc-question')).toBe(programCreateTr.days.question);
        const dayButtons = el.querySelectorAll<HTMLButtonElement>('[data-test="days"] button');
        expect(dayButtons.length).toBe(7);
        expect(dayButtons[0].textContent?.trim()).toBe('Pzt');
        dayButtons.forEach((b) => b.click());
        fixture.detectChanges();
        expect(text('[data-test="blocker"]')).toBe(programCreateTr.days.needOne);
        expect(q<HTMLButtonElement>('[data-test="next"]')!.disabled).toBe(true);
    });

    it('subjectsStep_ListsGradeSubjects_SelectionMarksThemHard', () => {
        setup();
        cmp().step.set(2);
        fixture.detectChanges();
        const chips = el.querySelectorAll('[data-test="subjects"] mat-chip-option');
        expect([...chips].map((c) => c.textContent?.trim())).toEqual(['Fen Bilimleri', 'Matematik', 'Türkçe']);
        el.querySelectorAll<HTMLElement>('[data-test="subjects"] mat-chip-option .mdc-evolution-chip__action')[1].click();
        fixture.detectChanges();
        expect(cmp().difficultIds()).toEqual([2]);
        expect(el.querySelector('.pc-preview__item--hard')?.textContent).toContain('Matematik');
    });

    it('subjectsLoadError_ShowsRetry', () => {
        setup(throwError(() => new Error('x')));
        cmp().step.set(2);
        fixture.detectChanges();
        expect(text('.pc-alert')).toContain(programCreateTr.subjects.loadError);
        service.getProgramSubjects.mockReturnValue(of(SUBJECTS));
        click('.pc-alert button');
        expect(el.querySelectorAll('[data-test="subjects"] mat-chip-option').length).toBe(3);
    });

    it('create_SendsServerSelectionsHardSubjectsAndDefaultName_ThenGoesToPrograms', () => {
        setup();
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        cmp().studyDays.set([1, 2, 3, 4, 5]);
        cmp()['setDifficult'](2, true);
        cmp().step.set(3);
        fixture.detectChanges();
        expect(text('#pc-question')).toBe(programCreateTr.until.question);
        click('[data-test="next"]');

        const request = service.createProgram.mock.lastCall![0] as CreateProgramRequest;
        expect(request.userSelections).toEqual([
            { stepId: 1, selectedValues: ['time'] },
            { stepId: 2, selectedValues: ['25-5'] },
            { stepId: 5, selectedValues: ['2'] },
            { stepId: 6, selectedValues: ['6', '7'] },
        ]);
        expect(request.difficultSubjectIds).toEqual([2]);
        // Ad kendiliğinden: "<Ay> programı".
        expect(request.programName).toMatch(/^[A-ZÇĞİÖŞÜ][a-zçğıöşü]+ programı$/);
        expect(request.startDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
        expect(navigate).toHaveBeenCalledWith(['/programs']);
        expect(snackBar.open).toHaveBeenCalled();
    });

    it('createError_KeepsChoicesAndShowsBand', () => {
        setup();
        service.createProgram.mockReturnValue(throwError(() => new Error('x')));
        cmp().step.set(3);
        fixture.detectChanges();
        click('[data-test="next"]');
        expect(text('[data-test="submit-error"]')).toBe(programCreateTr.createFailed);
        expect(cmp().step()).toBe(3);
    });

    it('lengthChips_SetTheEndDate', () => {
        setup();
        cmp().step.set(3);
        fixture.detectChanges();
        cmp()['setLength'](14);
        fixture.detectChanges();
        const days = Math.round((cmp().endDate()!.getTime() - cmp().startDate().getTime()) / 86_400_000);
        expect(days).toBe(14);
    });

    it('doneSteps_CanBeRevisitedFromTheStepList', () => {
        setup();
        click('[data-test="next"]');
        click('[data-test="step-amount"]');
        expect(cmp().step()).toBe(0);
        // İleri adıma listeden atlanmaz.
        expect(q<HTMLButtonElement>('[data-test="step-until"]')!.disabled).toBe(true);
    });

    it('dictionaries_HaveSameKeys', () => {
        const keys = (o: object, prefix = ''): string[] =>
            Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`]));
        expect(keys(programCreateEn).sort()).toEqual(keys(programCreateTr).sort());
    });
});

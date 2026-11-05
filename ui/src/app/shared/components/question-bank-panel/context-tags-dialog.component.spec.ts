import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Translation } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import { ContextTagsDialogComponent } from './context-tags-dialog.component';
import { MAX_QUESTION_SKILLS, Skill, SkillCategory } from '../../../models/skill';
import { SkillService } from '../../../services/skill.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import qbTr from '../../../../../public/i18n/question-bank/tr.json';
import qbEn from '../../../../../public/i18n/question-bank/en.json';

/** Seed ile aynı biçim: KB2.1–KB2.20 (1–20), KB3.1–KB3.3 (101–103). */
const SKILLS: Skill[] = [
    ...Array.from({ length: 20 }, (_, i) => ({ id: i + 1, code: `KB2.${i + 1}`, category: SkillCategory.Integrated, name: `B${i + 1}` })),
    ...Array.from({ length: 3 }, (_, i) => ({ id: 101 + i, code: `KB3.${i + 1}`, category: SkillCategory.HigherOrder, name: `U${i + 1}` })),
];

function stringValues(dictionary: Translation): string[] {
    return Object.values(dictionary).flatMap((value) => typeof value === 'string' ? [value] : value && typeof value === 'object' ? stringValues(value as Translation) : []);
}

describe('ContextTagsDialogComponent (issue #703)', () => {
    let fixture: ComponentFixture<ContextTagsDialogComponent>;
    let getForQuestion: Mock;
    let setForQuestion: Mock;
    let close: Mock;

    function setup(): void {
        TestBed.configureTestingModule({
            imports: [ContextTagsDialogComponent, translocoTestingModule({ langs: { 'question-bank/tr': qbTr, 'question-bank/en': qbEn } })],
            providers: [
                provideNoopAnimations(),
                { provide: MAT_DIALOG_DATA, useValue: { questionId: 42 } },
                { provide: MatDialogRef, useValue: { close } },
                { provide: SkillService, useValue: { list: () => of(SKILLS), getForQuestion, setForQuestion } },
            ],
        });
        fixture = TestBed.createComponent(ContextTagsDialogComponent);
        fixture.detectChanges();
    }

    const el = (selector: string) => fixture.nativeElement.querySelector(selector) as HTMLElement | null;
    const skillInput = (code: string) => el(`[data-testid="skill-${code}"] input`) as HTMLInputElement;

    beforeEach(() => {
        getForQuestion = vi.fn().mockReturnValue(of({ isContextBased: true, skills: [SKILLS[7]] }));
        setForQuestion = vi.fn().mockReturnValue(of({ isContextBased: false, skills: [SKILLS[7], SKILLS[21]] }));
        close = vi.fn();
    });

    it('load_ShowsCurrentTagsGroupedByCategory', () => {
        setup();

        expect((el('[data-testid="context-based-toggle"] input') as HTMLInputElement).checked).toBe(true);
        expect(skillInput('KB2.8').checked).toBe(true);
        expect(skillInput('KB3.2').checked).toBe(false);
        const groups = fixture.nativeElement.querySelectorAll('section h3') as NodeListOf<HTMLElement>;
        expect([...groups].map((h) => h.textContent?.trim())).toEqual([qbTr.contextTags.category['2'], qbTr.contextTags.category['3']]);
        expect(el('[data-testid="skill-KB2.8"]')?.textContent).toContain(qbTr.skills.KB2['8']);
    });

    it('save_SendsFlagAndSelectedIdsAndClosesWithResult', () => {
        setup();

        (el('[data-testid="context-based-toggle"] input') as HTMLInputElement).click();
        skillInput('KB3.2').click();
        fixture.detectChanges();
        (el('[data-testid="context-tags-save"]') as HTMLButtonElement).click();

        expect(setForQuestion).toHaveBeenCalledWith(42, false, [8, 102]);
        expect(close).toHaveBeenCalledWith({ isContextBased: false, skills: [SKILLS[7], SKILLS[21]] });
    });

    it('limit_DisablesUnselectedSkillsAtMax', () => {
        getForQuestion.mockReturnValue(of({ isContextBased: false, skills: SKILLS.slice(0, MAX_QUESTION_SKILLS) }));
        setup();

        expect(skillInput('KB2.11').disabled).toBe(true);
        expect(skillInput('KB2.1').disabled).toBe(false);

        skillInput('KB2.1').click();
        fixture.detectChanges();
        expect(skillInput('KB2.11').disabled).toBe(false);
    });

    it('loadError_ShowsErrorAndKeepsSaveDisabled', () => {
        getForQuestion.mockReturnValue(throwError(() => new Error('x')));
        setup();

        expect(el('[data-testid="context-tags-error"]')?.textContent).toContain(qbTr.contextTags.loadError);
        expect((el('[data-testid="context-tags-save"]') as HTMLButtonElement).disabled).toBe(true);
    });

    it('saveError_ShowsErrorAndStaysOpen', () => {
        setForQuestion.mockReturnValue(throwError(() => new Error('x')));
        setup();

        (el('[data-testid="context-tags-save"]') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(el('[data-testid="context-tags-error"]')?.textContent).toContain(qbTr.contextTags.saveError);
        expect(close).not.toHaveBeenCalled();
    });

    it('translations_CoverEverySeededSkillInBothLanguages', () => {
        for (const dictionary of [qbTr, qbEn]) {
            for (const skill of SKILLS) {
                const [layer, n] = skill.code.split('.');
                expect((dictionary.skills as Record<string, Record<string, string>>)[layer][n], skill.code).toBeTruthy();
            }
        }
    });

    // Issue #703: etiket öğretmenin beyanıdır; ürün metni müfredata uygunluk iddia etmez.
    it('translations_DoNotClaimCurriculumCompliance', () => {
        for (const [name, dictionary] of Object.entries({ 'question-bank/tr': qbTr, 'question-bank/en': qbEn })) {
            for (const value of stringValues(dictionary as Translation)) {
                expect(value, name).not.toMatch(/maarif|uygun|uyumlu|compatible|compliant|aligned/i);
            }
        }
    });
});

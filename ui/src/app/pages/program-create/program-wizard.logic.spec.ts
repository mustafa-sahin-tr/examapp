import { WizardChoices, buildPreview, isEndAfterStart, pickDailySubjects, toLocalDateString, toUserSelections } from './program-wizard.logic';

const base: WizardChoices = { studyType: 'time', duration: '40-10', questionsPerDay: 24, subjectsPerDay: 2, studyDays: [1, 2, 3, 4, 5, 6, 7] };
const subjects = [
    { id: 1, name: 'Fen' },
    { id: 2, name: 'Matematik' },
    { id: 3, name: 'Türkçe' },
];
// 2026-11-09 Pazartesi.
const monday = new Date(2026, 10, 9);

describe('program wizard logic (#885)', () => {
    it('toUserSelections_UsesTheServerStepIds', () => {
        expect(toUserSelections({ ...base, studyDays: [1, 2, 3, 4, 5] })).toEqual([
            { stepId: 1, selectedValues: ['time'] },
            { stepId: 2, selectedValues: ['40-10'] },
            { stepId: 5, selectedValues: ['2'] },
            { stepId: 6, selectedValues: ['6', '7'] },
        ]);
        // Soru sayılı ve dinlenme günü yok → "8".
        expect(toUserSelections({ ...base, studyType: 'question' })).toEqual([
            { stepId: 1, selectedValues: ['question'] },
            { stepId: 3, selectedValues: ['24'] },
            { stepId: 5, selectedValues: ['2'] },
            { stepId: 6, selectedValues: ['8'] },
        ]);
    });

    it('pickDailySubjects_MatchesTheServerRule', () => {
        // Eşitlikte ağırlığı büyük olan, sonra sıra.
        expect(pickDailySubjects([0, 0, 0], [1, 2, 1], 1)).toEqual([1]);
        expect(pickDailySubjects([1, 1, 0], [1, 2, 1], 2)).toEqual([1, 2]);
    });

    it('buildPreview_SpreadsSubjectsAndSkipsRestDays', () => {
        const days = buildPreview({ ...base, studyDays: [1, 2, 3, 4, 5] }, subjects, new Set(), monday);
        expect(days.map((d) => d.weekday)).toEqual([1, 2, 3, 4, 5, 6, 7]);
        expect(days[0].items).toEqual([{ subject: 'Fen', amount: 40 }, { subject: 'Matematik', amount: 40 }]);
        expect(days[1].items.map((i) => i.subject)).toEqual(['Fen', 'Türkçe']); // ders sırasıyla
        expect(days[5].items).toEqual([]); // Cumartesi dinlenme
        expect(days[6].items).toEqual([]);
    });

    it('buildPreview_HardSubjectComesTwiceAsOften_AndQuestionsAreSplit', () => {
        const days = buildPreview({ ...base, studyType: 'question', subjectsPerDay: 1 }, subjects, new Set([2]), monday);
        const math = days.filter((d) => d.items[0]?.subject === 'Matematik').length;
        expect(math).toBeGreaterThanOrEqual(3);
        expect(days[0].items[0]).toEqual({ subject: 'Matematik', amount: 24 });
    });

    it('dates_AreComparedByCalendarDay', () => {
        expect(isEndAfterStart(new Date(2026, 10, 9, 15), new Date(2026, 10, 9))).toBe(false);
        expect(isEndAfterStart(new Date(2026, 10, 9, 15), new Date(2026, 10, 10))).toBe(true);
        expect(toLocalDateString(new Date(2026, 0, 5))).toBe('2026-01-05');
    });
});

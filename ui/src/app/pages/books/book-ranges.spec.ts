import { BookDraft, formatRanges, offsetPreview, printedPageOf, validateBookDraft } from './book-ranges';

function draft(partial: Partial<BookDraft> = {}): BookDraft {
    return { name: 'TT MatLab', offsets: [], answerKeyRanges: [], toc: [], ...partial };
}

const toc = (fileFrom: number | null, fileTo: number | null, extra: Partial<BookDraft['toc'][number]> = {}) => ({
    fileFrom, fileTo, gradeId: 4, subjectId: 3, topicId: null, subTopicId: null, ...extra,
});

describe('book-ranges (#577)', () => {
    describe('validateBookDraft', () => {
        it('validDraftWithFascicles_NoErrors', () => {
            // TT MatLab: fasikül başına ayrı ofset, basılı numaralar tekrar eder (dosya 1→1, dosya 101→1).
            const errors = validateBookDraft(draft({
                offsets: [{ fileFrom: 1, fileTo: 100, offset: 0 }, { fileFrom: 101, fileTo: 200, offset: -100 }],
                answerKeyRanges: [{ fileFrom: 201, fileTo: 210 }],
                toc: [toc(1, 50), toc(51, 100, { topicId: 9, subTopicId: 12 })],
            }));
            expect(errors).toEqual([]);
        });

        it('blankName_NameRequired', () => {
            expect(validateBookDraft(draft({ name: '   ' })).map((e) => e.key)).toEqual(['nameRequired']);
        });

        it('missingFields_ReportsEachMissingPart', () => {
            const errors = validateBookDraft(draft({ offsets: [{ fileFrom: null, fileTo: null, offset: null }] }));
            expect(errors.map((e) => e.key)).toEqual(['fromRequired', 'toRequired', 'offsetRequired']);
            expect(errors.every((e) => e.section === 'offsets' && e.index === 0 && e.params.row === 1)).toBe(true);
        });

        it('fromAfterTo_AndFromBelowOne_Reported', () => {
            const errors = validateBookDraft(draft({ answerKeyRanges: [{ fileFrom: 20, fileTo: 10 }, { fileFrom: 0, fileTo: 3 }] }));
            expect(errors.map((e) => [e.key, e.index])).toEqual([['fromAfterTo', 0], ['fromMin', 1]]);
        });

        it('overlapWithinSection_ReportedOnLaterRowWithOtherRow_TouchingEndsOverlap', () => {
            const errors = validateBookDraft(draft({
                offsets: [{ fileFrom: 1, fileTo: 20, offset: 1 }, { fileFrom: 30, fileTo: 40, offset: 2 }, { fileFrom: 20, fileTo: 25, offset: 3 }],
            }));
            expect(errors).toEqual([{ section: 'offsets', index: 2, key: 'overlap', params: { row: 3, other: 1 } }]);
        });

        it('overlapAcrossSections_Allowed', () => {
            const errors = validateBookDraft(draft({
                offsets: [{ fileFrom: 1, fileTo: 300, offset: 2 }],
                answerKeyRanges: [{ fileFrom: 290, fileTo: 300 }],
                toc: [toc(1, 300)],
            }));
            expect(errors).toEqual([]);
        });

        it('negativeOffset_FirstPrintedBelowOne_PrintedMin', () => {
            const ok = validateBookDraft(draft({ offsets: [{ fileFrom: 3, fileTo: 10, offset: -2 }] }));
            const bad = validateBookDraft(draft({ offsets: [{ fileFrom: 2, fileTo: 10, offset: -2 }] }));
            expect(ok).toEqual([]);
            expect(bad.map((e) => e.key)).toEqual(['printedMin']);
        });

        it('tocWithoutGradeOrSubject_OrSubTopicWithoutTopic_Reported', () => {
            const errors = validateBookDraft(draft({
                toc: [toc(1, 5, { gradeId: null, subjectId: null }), toc(6, 9, { subTopicId: 4 })],
            }));
            expect(errors.map((e) => [e.key, e.index])).toEqual([
                ['gradeRequired', 0], ['subjectRequired', 0], ['subTopicNeedsTopic', 1],
            ]);
        });
    });

    it('validateBookDraft_AboveMaxFilePage_ToMaxAndPrintedMax', () => {
        const errors = validateBookDraft(draft({
            answerKeyRanges: [{ fileFrom: 99_990, fileTo: 100_001 }],
            offsets: [{ fileFrom: 99_000, fileTo: 100_000, offset: 1 }, { fileFrom: 1, fileTo: 10, offset: 5 }],
        }));
        expect(errors.map((e) => [e.section, e.key, e.index])).toEqual([
            ['offsets', 'printedMax', 0], ['answerKeyRanges', 'toMax', 0],
        ]);
        expect(errors[0].params.max).toBe(100_000);
    });

    it('offsetPreview_CompleteRow_PrintedEnds_IncompleteRow_Null', () => {
        expect(offsetPreview({ fileFrom: 40, fileTo: 60, offset: -1 })).toEqual({ fileFrom: 40, printedFrom: 39, fileTo: 60, printedTo: 59 });
        expect(offsetPreview({ fileFrom: 40, fileTo: null, offset: -1 })).toBeNull();
    });

    it('printedPageOf_UsesMatchingRange_NoRange_Null', () => {
        const offsets = [{ fileFrom: 1, fileTo: 100, offset: 1 }, { fileFrom: 101, fileTo: 200, offset: -99 }];
        expect(printedPageOf(offsets, 40)).toBe(41);
        expect(printedPageOf(offsets, 101)).toBe(2);
        expect(printedPageOf(offsets, 250)).toBeNull();
    });

    it('formatRanges_SingleAndMulti', () => {
        expect(formatRanges([{ fileFrom: 5, fileTo: 5 }, { fileFrom: 120, fileTo: 130 }])).toBe('5, 120–130');
    });
});

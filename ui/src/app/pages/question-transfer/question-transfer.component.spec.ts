import type { MockedObject } from "vitest";
import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, Subject as RxSubject, throwError } from 'rxjs';

import { QuestionTransferComponent } from './question-transfer.component';
import { GradesService } from '../../services/grades.service';
import { LabelingBookService } from '../../services/labeling-book.service';
import {
    CatalogImportResult,
    QuestionTransferImportPreview,
    QuestionTransferImportResult,
    QuestionTransferJob,
    QuestionTransferService,
    QuestionTransferWorksheetListItem,
    QuestionTransferWorksheetResult,
} from '../../services/question-transfer.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import qtEn from '../../../../public/i18n/question-transfer/en.json';
import qtTr from '../../../../public/i18n/question-transfer/tr.json';

const T = qtTr;

function job(overrides: Partial<QuestionTransferJob> = {}): QuestionTransferJob {
    return {
        id: 'job-1',
        kind: 'Export',
        status: 'Completed',
        sourceKey: 'transfer-1',
        totalItems: 10,
        processedItems: 10,
        fileUrl: '/img/b/question-transfer/exports/transfer-1/index.json',
        message: null,
        ...overrides,
    };
}

function preview(overrides: Partial<QuestionTransferImportPreview> = {}): QuestionTransferImportPreview {
    return {
        sourceKey: 'transfer-1',
        questionCount: 12,
        alreadyImportedCount: 1,
        newCount: 7,
        matchedCount: 4,
        conflictCount: 0,
        missingTaxonomyCount: 0,
        missingImageCount: 0,
        unpublishCount: 0,
        targetUnhashedCount: 0,
        conflictSamples: [],
        ...overrides,
    };
}

function catalogCounts(): CatalogImportResult {
    const counts = { created: 1, updated: 2, unchanged: 3 };
    return { dryRun: true, grades: counts, subjects: counts, gradeSubjects: counts, topics: counts, subTopics: counts, learningOutcomes: counts, errors: [] };
}

function worksheetRows(): QuestionTransferWorksheetListItem[] {
    return [
        { id: 31, name: 'Kesirler testi', kind: 'Test', gradeName: '5. Sınıf', subjectName: 'Matematik', questionCount: 10 },
        { id: 32, name: 'Boyama etkinliği', kind: 'Activity', gradeName: '5. Sınıf', subjectName: null, questionCount: 3 },
    ];
}

function worksheetCounts(overrides: Partial<QuestionTransferWorksheetResult> = {}): QuestionTransferWorksheetResult {
    return { imported: 2, alreadyImported: 1, blocked: 1, missingTaxonomy: 1, blockedSamples: ['Çakışan test: a question conflicts with one here'], warnings: ['Kopyalı test: kept once'], ...overrides };
}

const fileEvent = (file: File) => ({ target: { files: { item: () => file } } }) as unknown as Event;
const zip = () => new File(['zip'], 'paket.zip', { type: 'application/zip' });

describe('QuestionTransferComponent', () => {
    let fixture: ComponentFixture<QuestionTransferComponent>;
    let component: QuestionTransferComponent;
    let transfer: MockedObject<QuestionTransferService>;

    const el = () => fixture.nativeElement as HTMLElement;
    const text = () => el().textContent ?? '';
    const jobRows = () => Array.from(el().querySelectorAll('.qt-job'));
    const button = (label: string) => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;

    function setup(jobs: QuestionTransferJob[] = []): void {
        transfer = {
            listJobs: vi.fn().mockReturnValue(of(jobs)),
            previewImport: vi.fn().mockReturnValue(of(preview())),
            startImport: vi.fn().mockReturnValue(of(job({ kind: 'Import' }))),
            startExport: vi.fn().mockReturnValue(of(job())),
            listWorksheets: vi.fn().mockReturnValue(of(worksheetRows())),
            downloadPackage: vi.fn(),
            importCatalog: vi.fn().mockReturnValue(of(catalogCounts())),
            downloadCatalog: vi.fn(),
            hangfireLogin: vi.fn(),
            hangfireUrl: vi.fn(),
        } as unknown as MockedObject<QuestionTransferService>;

        TestBed.configureTestingModule({
            imports: [QuestionTransferComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'question-transfer/tr': qtTr } })],
            providers: [
                { provide: QuestionTransferService, useValue: transfer },
                { provide: GradesService, useValue: { getGrades: () => of([{ id: 5, name: '5. Sınıf' }]) } },
                {
                    provide: SubjectService,
                    useValue: {
                        loadCategories: () => of([{ id: 7, name: 'Matematik' }]),
                        getSubjectsByGrade: () => of([{ id: 8, name: 'Fen' }]),
                        getTopicsBySubject: vi.fn().mockReturnValue(of([{ id: 9, name: 'Sayılar', subjectId: 7, gradeId: 5 }])),
                        getTopicsBySubjectAndGrade: vi.fn().mockReturnValue(of([])),
                    },
                },
                { provide: LabelingBookService, useValue: { list: () => of([]) } },
            ],
        });
        fixture = TestBed.createComponent(QuestionTransferComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    }

    it('translations_HaveTheSameKeysInBothLanguages', () => {
        const keys = (node: object, prefix = ''): string[] => Object.entries(node).flatMap(([k, v]) =>
            typeof v === 'object' ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`]);

        expect(keys(qtEn).sort()).toEqual(keys(qtTr).sort());
    });

    it('page_DoesNotShowInternalTerms', () => {
        setup();

        for (const term of ['Bundle', 'Paket Kodu', 'Index', 'Export Başlat']) {
            expect(text()).not.toContain(term);
        }
    });

    // ---- test ve etkinlik seçimi

    describe('worksheet selection', () => {
        function openWorksheets(): void {
            setup();
            component.setExportMode('worksheets');
            fixture.detectChanges();
        }

        it('questionsMode_DoesNotLoadTheList', () => {
            setup();

            expect(transfer.listWorksheets).not.toHaveBeenCalled();
        });

        it('opening_LoadsAndShowsTheRecordsWithTheirKindAndWithoutAnEmptySubject', () => {
            openWorksheets();

            expect(transfer.listWorksheets).toHaveBeenCalledTimes(1);
            expect(text()).toContain('Kesirler testi');
            expect(text()).toContain(T.export.kind.Activity);
            expect(el().querySelectorAll('.qt-ws-row')[1].textContent).not.toContain('null');
        });

        it('startButton_NothingSelected_IsDisabledAndSendsNothing', () => {
            openWorksheets();

            expect((el().querySelector('.qt-export-start') as HTMLButtonElement).disabled).toBe(true);
            component.startExport();
            expect(transfer.startExport).not.toHaveBeenCalled();
        });

        it('startExport_SendsTheSelectionWithoutScopeAndClearsIt', () => {
            openWorksheets();
            component.onGradeChange(5);
            component.toggleWorksheet(31, true);
            component.toggleWorksheet(32, true);
            component.toggleWorksheet(32, false);

            component.startExport();

            expect(transfer.startExport).toHaveBeenCalledWith({ questionIds: [], worksheetIds: [31] });
            expect(component.selectedWorksheetIds().size).toBe(0);
        });

        it('startExport_Failure_KeepsTheSelection', () => {
            openWorksheets();
            transfer.startExport.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
            component.toggleWorksheet(31, true);

            component.startExport();

            expect(component.exportFailed()).toBe(true);
            expect(component.isWorksheetSelected(31)).toBe(true);
        });

        it('questionsMode_IgnoresTheSelection', () => {
            openWorksheets();
            component.toggleWorksheet(31, true);
            component.setExportMode('questions');

            component.startExport();

            expect(transfer.startExport.mock.calls[0][0].worksheetIds).toBeUndefined();
        });

        it('filterChange_ReloadsWithTheFilterAndKeepsTheSelection', () => {
            openWorksheets();
            component.toggleWorksheet(31, true);

            component.onWorksheetKindChange('Activity');
            component.onSubjectChange(7);

            expect(transfer.listWorksheets).toHaveBeenLastCalledWith(expect.objectContaining({ kind: 'Activity', subjectId: 7, take: 200 }));
            expect(component.isWorksheetSelected(31)).toBe(true);
        });

        it('search_WaitsForTypingToStop', async () => {
            vi.useFakeTimers();
            try {
                openWorksheets();
                transfer.listWorksheets.mockClear();

                component.onWorksheetSearchChange('kes');
                component.onWorksheetSearchChange('kesir ');
                await vi.advanceTimersByTimeAsync(299);
                expect(transfer.listWorksheets).not.toHaveBeenCalled();

                await vi.advanceTimersByTimeAsync(1);
                expect(transfer.listWorksheets).toHaveBeenCalledTimes(1);
                expect(transfer.listWorksheets).toHaveBeenCalledWith(expect.objectContaining({ search: 'kesir' }));
            } finally {
                vi.useRealTimers();
            }
        });

        it('lateResponseOfAnOldFilter_DoesNotOverwriteTheList', () => {
            setup();
            const slow = new RxSubject<QuestionTransferWorksheetListItem[]>();
            transfer.listWorksheets.mockReturnValueOnce(slow);
            component.setExportMode('worksheets');
            transfer.listWorksheets.mockReturnValueOnce(of([worksheetRows()[1]]));

            component.onWorksheetKindChange('Activity');
            slow.next(worksheetRows());

            expect(component.worksheets().map((w) => w.id)).toEqual([32]);
        });

        it('loadFailure_ShowsRetryWhichLoadsAgain', () => {
            setup();
            transfer.listWorksheets.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
            component.setExportMode('worksheets');
            fixture.detectChanges();

            expect(text()).toContain(T.export.worksheetsFailed);
            button(T.jobs.retry)!.click();
            fixture.detectChanges();

            expect(text()).not.toContain(T.export.worksheetsFailed);
            expect(text()).toContain('Kesirler testi');
        });

        it('emptyList_SaysSo', () => {
            setup();
            transfer.listWorksheets.mockReturnValue(of([]));
            component.setExportMode('worksheets');
            fixture.detectChanges();

            expect(text()).toContain(T.export.worksheetsEmpty);
        });

        it('fullPage_TellsToNarrowTheList', () => {
            setup();
            transfer.listWorksheets.mockReturnValue(of(Array.from({ length: 200 }, (_, i) => ({ ...worksheetRows()[0], id: i + 1 }))));
            component.setExportMode('worksheets');
            fixture.detectChanges();

            expect(el().textContent).toContain(T.export.worksheetsTruncated.split('{{count}}')[1].trim());
        });
    });

    describe('worksheet counts', () => {
        const result = (worksheets: QuestionTransferWorksheetResult): QuestionTransferImportResult => ({
            worksheets, imported: 0, matched: 0, alreadyImported: 0, conflicts: 0, failed: 0, missingTaxonomy: 0, unpublished: 0,
            unpublishedQuestionIds: [], conflictSamples: [], failedSamples: [], warnings: [],
        });

        it('preview_ShowsWhatWillHappenAndWhyARecordIsBlocked', () => {
            setup();
            transfer.previewImport.mockReturnValue(of(preview({ worksheets: worksheetCounts() })));

            component.onImportFileSelected(fileEvent(zip()));
            fixture.detectChanges();

            expect(el().querySelector('.qt-ws-preview')).not.toBeNull();
            expect(text()).toContain(T.worksheets.blocked);
            expect(text()).toContain(T.worksheets.closedNote);
            expect(text()).toContain('Çakışan test');
        });

        it('preview_PackageWithoutRecords_HidesTheSection', () => {
            setup();
            transfer.previewImport.mockReturnValue(of(preview({ worksheets: worksheetCounts({ imported: 0, alreadyImported: 0, blocked: 0 }) })));

            component.onImportFileSelected(fileEvent(zip()));
            fixture.detectChanges();

            expect(el().querySelector('.qt-ws-preview')).toBeNull();
        });

        it('preview_NothingBlocked_HidesTheBlockedNote', () => {
            setup();
            transfer.previewImport.mockReturnValue(of(preview({ worksheets: worksheetCounts({ blocked: 0, blockedSamples: [] }) })));

            component.onImportFileSelected(fileEvent(zip()));
            fixture.detectChanges();

            expect(el().querySelector('.qt-ws-preview')).not.toBeNull();
            expect(text()).not.toContain(T.worksheets.blockedNote);
        });

        it('jobResult_ShowsCountsSamplesAndWarnings', () => {
            setup([job({ kind: 'Import', result: result(worksheetCounts()) })]);

            expect(text()).toContain(T.worksheets.notImported);
            expect(text()).toContain('Çakışan test');
            expect(text()).toContain('Kopyalı test');
        });

        it('jobResult_WithoutWorksheetField_StillRenders', () => {
            const { worksheets: _, ...old } = result(worksheetCounts());
            setup([job({ kind: 'Import', result: old })]);

            expect(jobRows().length).toBe(1);
            expect(el().querySelector('.qt-ws-preview')).toBeNull();
        });
    });

    // ---- dışa aktarma

    it('exportButton_NoSelection_SaysEverythingWillBeExported', () => {
        setup();

        expect(button(T.export.startAll)).toBeDefined();
    });

    it('startExport_NoSelection_SendsNoSourceNameSoServerGeneratesIt', () => {
        setup();

        component.startExport();

        const request = transfer.startExport.mock.calls[0][0];
        expect(request).toEqual({ questionIds: [], gradeId: null, subjectId: null, topicId: null, bookId: null });
    });

    it('startExport_ScopeSelected_SendsScopeAndIgnoresTypedNumbers', () => {
        setup();
        component.questionIdsText.set('1, 2');
        component.onSubjectChange(7);
        fixture.detectChanges();

        expect(button(T.export.startScope)).toBeDefined();
        component.startExport();

        expect(transfer.startExport.mock.calls[0][0]).toMatchObject({ questionIds: [], subjectId: 7 });
    });

    it('questionNumbers_RangeAndList_AreExpandedWithoutDuplicates', () => {
        setup();
        component.questionIdsText.set('12, 15 100-102; 12');
        fixture.detectChanges();

        expect(component.questionIds()).toEqual([12, 15, 100, 101, 102]);
        expect(button(T.export.startIds.replace('{{count}}', '5'))).toBeDefined();
    });

    it('questionNumbers_HugeRange_StopsAtTheLimit', () => {
        setup();
        component.questionIdsText.set('1-9999999');

        expect(component.questionIds().length).toBe(5000);
    });

    it('gradeChange_SubjectNotInGrade_ClearsSubjectAndTopic', () => {
        setup();
        component.onSubjectChange(7);
        component.topicId.set(9);

        component.onGradeChange(5);

        expect(component.subjectId()).toBeNull();
        expect(component.topicId()).toBeNull();
        expect(component.subjects().map((s) => s.id)).toEqual([8]);
    });

    it('gradeChange_LateAnswerOfThePreviousGrade_DoesNotOverwriteTheList', () => {
        setup();
        const subjects = TestBed.inject(SubjectService);
        const firstGrade = new RxSubject<{ id: number; name: string }[]>();
        const secondGrade = new RxSubject<{ id: number; name: string }[]>();
        subjects.getSubjectsByGrade = vi.fn().mockReturnValueOnce(firstGrade).mockReturnValueOnce(secondGrade);

        component.onGradeChange(1);
        component.onGradeChange(2);
        secondGrade.next([{ id: 2, name: 'İkinci' }]);
        firstGrade.next([{ id: 1, name: 'Birinci' }]);

        expect(component.subjects().map((s) => s.id)).toEqual([2]);
    });

    it('gradeChange_SubjectListFails_ClearsTheHiddenSubjectFilter', () => {
        setup();
        component.onSubjectChange(7);
        TestBed.inject(SubjectService).getSubjectsByGrade = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

        component.onGradeChange(1);

        expect(component.subjectId()).toBeNull();
        expect(component.hasScope()).toBe(true);
        expect(component.gradeId()).toBe(1);
    });

    it('startExport_RequestFails_ShowsErrorAndEnablesTheButtonAgain', () => {
        setup();
        transfer.startExport.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

        component.startExport();
        fixture.detectChanges();

        expect(text()).toContain(T.export.failed);
        expect(component.exportBusy()).toBe(false);
    });

    // ---- içe alma

    it('importButton_NoPreviewYet_IsDisabled', () => {
        setup();
        const start = el().querySelector('.qt-import-start') as HTMLButtonElement;
        expect(start.disabled).toBe(true);

        component.onImportFileSelected(fileEvent(zip()));
        fixture.detectChanges();

        expect(start.disabled).toBe(false);
        expect(text()).toContain('paket.zip');
    });

    it('preview_ShowsNewMatchedAndAlreadyCounts', () => {
        setup();

        component.onImportFileSelected(fileEvent(zip()));
        fixture.detectChanges();

        const counts = el().querySelector('.qt-preview')?.textContent ?? '';
        expect(counts).toContain(T.import.total.replace('{{count}}', '12'));
        expect(counts).toContain(T.import.new);
        expect(counts).toContain('7');
        expect(counts).toContain(T.import.matched);
        expect(counts).not.toContain(T.import.conflictsNote);
    });

    it('preview_WithConflictsAndMissingTaxonomy_ExplainsThem', () => {
        setup();
        transfer.previewImport.mockReturnValue(of(preview({ conflictCount: 2, missingTaxonomyCount: 3, targetUnhashedCount: 4 })));

        component.onImportFileSelected(fileEvent(zip()));
        fixture.detectChanges();

        expect(text()).toContain(T.import.conflictsNote);
        expect(text()).toContain(T.import.missingTaxonomyNote);
        expect(text()).toContain(T.import.unhashedNote.replace('{{count}}', '4'));
    });

    it('preview_WithQuestionsToUnpublish_WarnsBeforeImport', () => {
        setup();
        transfer.previewImport.mockReturnValue(of(preview({ unpublishCount: 6 })));

        component.onImportFileSelected(fileEvent(zip()));
        fixture.detectChanges();

        expect(text()).toContain(T.import.unpublishNote.replace('{{count}}', '6'));
    });

    it('preview_SecondFileChosen_IgnoresTheLateAnswerOfTheFirst', () => {
        setup();
        const first = new RxSubject<QuestionTransferImportPreview>();
        const second = new RxSubject<QuestionTransferImportPreview>();
        transfer.previewImport.mockReturnValueOnce(first).mockReturnValueOnce(second);

        component.onImportFileSelected(fileEvent(new File(['a'], 'a.zip')));
        component.onImportFileSelected(fileEvent(new File(['b'], 'b.zip')));
        first.next(preview({ newCount: 111 }));

        expect(component.importPreview()).toBeNull();
        expect(component.previewLoading()).toBe(true);

        second.next(preview({ newCount: 222 }));

        expect(component.importPreview()?.newCount).toBe(222);
        expect(component.previewLoading()).toBe(false);
    });

    it('fileInputs_AreClearedSoTheSameFileCanBeChosenAgain', async () => {
        setup();
        const importEvent = fileEvent(zip());
        const catalogEvent = fileEvent(new File(['{}'], 'catalog.json'));

        component.onImportFileSelected(importEvent);
        await component.onCatalogFileSelected(catalogEvent);

        expect((importEvent.target as HTMLInputElement).value).toBe('');
        expect((catalogEvent.target as HTMLInputElement).value).toBe('');
    });

    it('preview_Fails_ShowsErrorAndKeepsImportDisabled', () => {
        setup();
        transfer.previewImport.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

        component.onImportFileSelected(fileEvent(zip()));
        fixture.detectChanges();

        expect(text()).toContain(T.import.previewFailed);
        expect((el().querySelector('.qt-import-start') as HTMLButtonElement).disabled).toBe(true);
    });

    it('startImport_Succeeds_ClearsTheFileAndRefreshesJobs', () => {
        setup();
        component.onImportFileSelected(fileEvent(zip()));
        transfer.listJobs.mockClear();

        component.startImport();

        expect(component.importFile()).toBeNull();
        expect(component.importPreview()).toBeNull();
        expect(transfer.listJobs).toHaveBeenCalled();
    });

    // ---- katalog

    it('catalogFile_NotJson_ShowsErrorWithoutCallingTheServer', async () => {
        setup();

        await component.onCatalogFileSelected(fileEvent(new File(['{bozuk'], 'catalog.json')));
        fixture.detectChanges();

        expect(text()).toContain(T.catalog.invalidFile);
        expect(transfer.importCatalog).not.toHaveBeenCalled();
    });

    it('catalogFile_Valid_PreviewsFirstAndWritesOnlyAfterConfirmation', async () => {
        setup();

        await component.onCatalogFileSelected(fileEvent(new File(['{"grades":[]}'], 'catalog.json')));
        fixture.detectChanges();

        expect(transfer.importCatalog).toHaveBeenCalledTimes(1);
        expect(transfer.importCatalog).toHaveBeenLastCalledWith({ grades: [] }, true);
        expect(text()).toContain(T.catalog.previewCaption);

        button(T.catalog.apply)!.click();
        fixture.detectChanges();

        expect(transfer.importCatalog).toHaveBeenLastCalledWith({ grades: [] }, false);
        expect(text()).toContain(T.catalog.applied);
        expect(button(T.catalog.apply)).toBeUndefined();
    });

    it('catalogFile_ServerRejects_ShowsItsErrorsAndNoApplyButton', async () => {
        setup();
        transfer.importCatalog.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errors: ['Konu adı başka kodla var'] } })));

        await component.onCatalogFileSelected(fileEvent(new File(['{}'], 'catalog.json')));
        fixture.detectChanges();

        expect(text()).toContain('Konu adı başka kodla var');
        expect(button(T.catalog.apply)).toBeUndefined();
    });

    it('catalogApply_ServerFails_KeepsThePreviewSoItCanBeRetried', async () => {
        setup();
        await component.onCatalogFileSelected(fileEvent(new File(['{}'], 'catalog.json')));
        transfer.importCatalog.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

        component.applyCatalog();
        fixture.detectChanges();

        expect(text()).toContain(T.catalog.failed);
        expect(button(T.catalog.apply)).toBeDefined();
    });

    // ---- işler

    it('jobs_None_ShowsEmptyMessage', () => {
        setup([]);

        expect(text()).toContain(T.jobs.empty);
        expect(jobRows().length).toBe(0);
    });

    it('jobs_LoadFails_ShowsErrorWithRetry', () => {
        setup([]);
        transfer.listJobs.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

        component.refreshJobs();
        fixture.detectChanges();

        expect(text()).toContain(T.jobs.loadFailed);
        expect(button(T.jobs.retry)).toBeDefined();
        expect(text()).not.toContain(T.jobs.empty);
    });

    it('completedExport_OffersThePackageNotTheIndexFile', () => {
        setup([job()]);

        expect(jobRows()[0].textContent).toContain(T.jobs.downloadPackage);
        expect(jobRows()[0].textContent).toContain(T.jobs.status.Completed);
    });

    it('downloadPackage_UsesTheSourceNameOfThatJob', () => {
        setup([job({ sourceKey: 'transfer-abc' })]);
        const body = new Blob(['zip']);
        transfer.downloadPackage.mockReturnValue(of(new HttpResponse({ body, headers: new HttpHeaders() })));
        const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

        button(T.jobs.downloadPackage)!.click();

        expect(transfer.downloadPackage).toHaveBeenCalledWith('transfer-abc');
        expect(click).toHaveBeenCalled();
        click.mockRestore();
    });

    it('exportWithNoMatchingQuestions_ShowsTheServerMessageAndNoDownload', () => {
        setup([job({ fileUrl: null, message: 'No questions match the selected scope.' })]);

        expect(jobRows()[0].textContent).toContain('No questions match');
        expect(jobRows()[0].textContent).not.toContain(T.jobs.downloadPackage);
    });

    it('runningJob_ShowsProgressAndNoDownload', () => {
        setup([job({ kind: 'Import', status: 'Running', fileUrl: null, processedItems: 5, totalItems: 10 })]);

        expect(jobRows()[0].textContent).toContain('5/10');
        expect(jobRows()[0].textContent).toContain('50%');
        expect(jobRows()[0].textContent).not.toContain(T.jobs.downloadPackage);
    });

    it('failedJob_ShowsWhy', () => {
        setup([job({ kind: 'Import', status: 'Failed', fileUrl: null, message: 'storage could not be read' })]);

        expect(jobRows()[0].textContent).toContain('storage could not be read');
        expect(jobRows()[0].textContent).toContain(T.jobs.status.Failed);
    });

    it('finishedImport_ShowsResultCountsAndDetails', () => {
        setup([job({
            kind: 'Import',
            fileUrl: 'x.zip',
            result: {
                imported: 3, matched: 2, alreadyImported: 0, conflicts: 1, failed: 1, missingTaxonomy: 0,
                unpublished: 2, unpublishedQuestionIds: [11, 12],
                conflictSamples: ['question:9: same image'], failedSamples: ['question:8: image missing'], warnings: ['question:7: unpublished'],
            },
        })]);

        const row = jobRows()[0].textContent ?? '';
        expect(row).toContain(T.jobs.failedCount);
        expect(row).toContain('question:9: same image');
        expect(row).toContain('question:8: image missing');
        expect(row).toContain('question:7: unpublished');
        expect(row).toContain(T.jobs.unpublishedIds.replace('{{ids}}', '11, 12'));
        expect(row).not.toContain(T.jobs.downloadPackage);
    });
});

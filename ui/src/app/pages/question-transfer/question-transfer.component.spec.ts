import type { MockedObject } from "vitest";
// #545 — Angular 21 control-flow migrasyonu (@if/@else jobs, @for iş/paket listeleri, @if önizleme ve indirme) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { QuestionTransferComponent } from './question-transfer.component';
import { QuestionTransferExportBundle, QuestionTransferImportPreview, QuestionTransferJob, QuestionTransferService, } from '../../services/question-transfer.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import qtTr from '../../../../public/i18n/question-transfer/tr.json';

const T = qtTr;

function job(overrides: Partial<QuestionTransferJob> = {}): QuestionTransferJob {
    return {
        id: 'job-1',
        kind: 'Export',
        status: 'Completed',
        sourceKey: 'default',
        totalItems: 10,
        processedItems: 10,
        fileUrl: 'https://files.example.test/package.zip',
        message: null,
        ...overrides,
    };
}

describe('QuestionTransferComponent (#545)', () => {
    let fixture: ComponentFixture<QuestionTransferComponent>;
    let transfer: MockedObject<QuestionTransferService>;

    const el = () => fixture.nativeElement as HTMLElement;
    const text = () => el().textContent ?? '';
    const jobRows = () => Array.from(el().querySelectorAll('.qt-job'));
    const bundleRows = () => Array.from(el().querySelectorAll('.qt-bundle'));
    const importButton = () => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.includes(T.import.start)) as HTMLButtonElement;

    function setup(jobs: QuestionTransferJob[] = [], bundles: QuestionTransferExportBundle[] = []): void {
        transfer = {
            listJobs: vi.fn().mockName("QuestionTransferService.listJobs"),
            listExportBundles: vi.fn().mockName("QuestionTransferService.listExportBundles"),
            listSourceKeys: vi.fn().mockName("QuestionTransferService.listSourceKeys"),
            previewImport: vi.fn().mockName("QuestionTransferService.previewImport")
        } as unknown as MockedObject<QuestionTransferService>;
        transfer.listJobs.mockReturnValue(of(jobs));
        transfer.listExportBundles.mockReturnValue(of(bundles));
        transfer.listSourceKeys.mockReturnValue(of([]));

        TestBed.configureTestingModule({
            imports: [QuestionTransferComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'question-transfer/tr': qtTr } })],
            providers: [{ provide: QuestionTransferService, useValue: transfer }],
        });
        fixture = TestBed.createComponent(QuestionTransferComponent);
        fixture.detectChanges();
    }

    it('template_NoJobs_ShowsEmptyJobsMessageAndNoJobGrid', () => {
        setup([]);

        expect(text()).toContain(T.jobs.empty);
        expect(el().querySelector('.qt-jobs-grid')).toBeNull();
    });

    it('template_OnlyExportJobs_ShowsEmptyImportColumn', () => {
        setup([job({ kind: 'Export' })]);

        expect(el().querySelector('.qt-jobs-grid')).not.toBeNull();
        expect(text()).toContain(T.jobs.importEmpty);
        expect(text()).not.toContain(T.jobs.exportEmpty);
        expect(jobRows().length).toBe(1);
    });

    it('template_CompletedZipJob_ShowsZipDownloadButton', () => {
        setup([job({ kind: 'Export', status: 'Completed', fileUrl: 'https://files.example.test/package.zip' })]);

        const download = Array.from(el().querySelectorAll('.qt-job button')).find((b) => b.textContent?.includes(T.jobs.downloadZip));
        expect(download).toBeDefined();
    });

    it('template_RunningJob_HidesDownloadButtonAndShowsProgress', () => {
        setup([job({ kind: 'Import', status: 'Running', fileUrl: null, processedItems: 5, totalItems: 10 })]);

        expect(el().querySelector('.qt-job button')).toBeNull();
        expect(text()).toContain('5/10');
        expect(text()).toContain('50%');
    });

    it('template_CompletedJobWithoutFile_HidesDownloadButton', () => {
        setup([job({ kind: 'Export', status: 'Completed', fileUrl: null })]);

        expect(el().querySelector('.qt-job button')).toBeNull();
    });

    it('template_NoBundles_ShowsEmptyBundleMessage', () => {
        setup([], []);

        expect(text()).toContain(T.bundles.empty);
        expect(bundleRows().length).toBe(0);
    });

    it('template_BundlesPresent_RendersOneRowPerBundle', () => {
        setup([], [
            { sourceKey: 'default', bundleNo: 1, questionCount: 25 },
            { sourceKey: 'default', bundleNo: 2, questionCount: 7 },
        ]);

        expect(text()).not.toContain(T.bundles.empty);
        expect(bundleRows().length).toBe(2);
        expect(bundleRows()[0].textContent).toContain(T.bundles.bundleTitle.replace('{{no}}', '1'));
        expect(bundleRows()[1].textContent).toContain(T.bundles.questionCount.replace('{{count}}', '7'));
    });

    it('template_NoImportPreview_HidesPreviewChips', () => {
        setup();

        expect(el().querySelector('.qt-preview')).toBeNull();
    });

    it('template_ImportPreviewLoaded_ShowsSourceQuestionAndAlreadyChips', () => {
        setup();
        const preview: QuestionTransferImportPreview = { sourceKey: 'kitap-7', questionCount: 12, alreadyImportedCount: 3 };
        fixture.componentInstance.importPreview.set(preview);
        fixture.detectChanges();

        const chips = el().querySelector('.qt-preview')?.textContent ?? '';
        expect(chips).toContain(T.import.sourceChip.replace('{{sourceKey}}', 'kitap-7'));
        expect(chips).toContain(T.import.questionChip.replace('{{count}}', '12'));
        expect(chips).toContain(T.import.alreadyChip.replace('{{count}}', '3'));
    });

    it('template_NoIdsEntered_ShowsAllSelectedHint', () => {
        setup();

        expect(text()).toContain(T.export.allSelected);
    });

    it('template_IdsEntered_ShowsIdCountHint', () => {
        setup();
        fixture.componentInstance.exportQuestionIds.setValue('12, 15');
        fixture.detectChanges();

        expect(text()).toContain(T.export.idCount.replace('{{count}}', '2'));
        expect(text()).not.toContain(T.export.allSelected);
    });

    it('template_NoImportFile_DisablesImportButtonUntilFileChosen', () => {
        setup();
        expect(importButton().disabled).toBe(true);

        fixture.componentInstance.importFile.set(new File(['zip'], 'paket.zip', { type: 'application/zip' }));
        fixture.detectChanges();

        expect(importButton().disabled).toBe(false);
        expect(text()).toContain('paket.zip');
    });
});

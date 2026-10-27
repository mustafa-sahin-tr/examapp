import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, of, throwError } from 'rxjs';

import { BooksComponent } from './books.component';
import { LabelingBook } from '../../models/labeling-book';
import { Grade } from '../../models/student';
import { GradesService } from '../../services/grades.service';
import { LabelingBookService } from '../../services/labeling-book.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import booksTr from '../../../../public/i18n/books/tr.json';

const GRADES: Grade[] = [{ id: 4, name: '4. Sınıf' }, { id: 5, name: '5. Sınıf' }];

/** TT MatLab: iki fasikül, basılı numaralar tekrar ediyor. */
const MATLAB: LabelingBook = {
    id: 7,
    name: 'TT MatLab',
    canonicalBookId: null,
    canonicalBookName: null,
    notes: 'Fasiküllü',
    offsets: [{ fileFrom: 1, fileTo: 100, offset: -1 }, { fileFrom: 101, fileTo: 200, offset: -100 }],
    answerKeyRanges: [{ fileFrom: 201, fileTo: 210 }],
    toc: [{
        fileFrom: 30, fileTo: 50, printedFrom: 29, printedTo: 49, gradeId: 4, subjectId: 3, topicId: 11, subTopicId: null,
        gradeName: '4. Sınıf', subjectName: 'Fen Bilimleri', topicName: 'Maddenin Hâlleri', subTopicName: null,
    }],
};
const COMBO: LabelingBook = {
    id: 8, name: 'Güneş Koleji Türkçe', canonicalBookId: 9, canonicalBookName: 'Süper Combo Türkçe', notes: null,
    offsets: [], answerKeyRanges: [], toc: [],
};

describe('BooksComponent (#577)', () => {
    let fixture: ComponentFixture<BooksComponent>;
    let component: BooksComponent;
    let api: MockedObject<LabelingBookService>;
    let subjects: MockedObject<SubjectService>;
    let listResponse: Observable<LabelingBook[]>;

    const el = () => fixture.nativeElement as HTMLElement;
    const text = () => el().textContent ?? '';

    function setup(): void {
        api = {
            list: vi.fn().mockName('list').mockReturnValue(listResponse),
            create: vi.fn().mockName('create'),
            update: vi.fn().mockName('update'),
            delete: vi.fn().mockName('delete'),
            getPage: vi.fn().mockName('getPage'),
            getFilePages: vi.fn().mockName('getFilePages'),
        } as unknown as MockedObject<LabelingBookService>;
        subjects = {
            getSubjectsByGrade: vi.fn().mockName('getSubjectsByGrade').mockReturnValue(of([{ id: 3, name: 'Fen Bilimleri' }])),
            getTopicsBySubjectAndGrade: vi.fn().mockName('getTopicsBySubjectAndGrade')
                .mockReturnValue(of([{ id: 11, name: 'Maddenin Hâlleri', subjectId: 3, gradeId: 4 }])),
            getSubTopicsByTopic: vi.fn().mockName('getSubTopicsByTopic').mockReturnValue(of([])),
        } as unknown as MockedObject<SubjectService>;
        TestBed.configureTestingModule({
            imports: [BooksComponent, translocoTestingModule({ langs: { 'books/tr': booksTr } })],
            providers: [
                provideNoopAnimations(),
                { provide: LabelingBookService, useValue: api },
                { provide: SubjectService, useValue: subjects },
                { provide: GradesService, useValue: { getGrades: () => of(GRADES) } },
            ],
        });
        fixture = TestBed.createComponent(BooksComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    }

    function confirmWith(result: boolean) {
        return vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(result) } as never);
    }

    beforeEach(() => {
        listResponse = of([MATLAB, COMBO]);
    });

    describe('liste', () => {
        it('init_ListsBooksWithSummaryAndEdition', () => {
            setup();

            const items = el().querySelectorAll('.bk-item');
            expect(items.length).toBe(2);
            expect(items[0].textContent).toContain('TT MatLab');
            expect(items[0].textContent).toContain('2 ofset aralığı');
            expect(items[0].textContent).toContain('cevap anahtarı: 201–210');
            expect(items[1].textContent).toContain('Baskısı: Süper Combo Türkçe');
            expect(text()).toContain(booksTr.selectHint);
        });

        it('init_Empty_ShowsEmptyState', () => {
            listResponse = of([]);
            setup();

            expect(text()).toContain(booksTr.list.empty);
        });

        it('init_Error_ShowsRetry_RetryReloads', () => {
            listResponse = throwError(() => ({ status: 500 }));
            setup();
            expect(text()).toContain(booksTr.list.failed);

            api.list.mockReturnValue(of([MATLAB]));
            (el().querySelector('.bk-state--error button') as HTMLButtonElement).click();
            fixture.detectChanges();

            expect(el().querySelectorAll('.bk-item').length).toBe(1);
        });
    });

    describe('düzenleme', () => {
        it('select_FillsFormWithRangesPreviewAndLoadsTaxonomy', async () => {
            setup();

            await component.select(MATLAB);
            fixture.detectChanges();

            expect(component.form.controls.name.value).toBe('TT MatLab');
            expect(component.offsets.length).toBe(2);
            expect(component.answerKeyRanges.length).toBe(1);
            expect(component.toc.length).toBe(1);
            const previews = Array.from(el().querySelectorAll('.bk-preview')).map((p) => p.textContent?.replace(/\s+/g, ' ').trim());
            expect(previews[0]).toBe('dosya 1 → basılı 0 · dosya 100 → basılı 99');
            expect(previews[1]).toBe('dosya 101 → basılı 1 · dosya 200 → basılı 100');
            expect(subjects.getSubjectsByGrade).toHaveBeenCalledWith(4);
            expect(subjects.getTopicsBySubjectAndGrade).toHaveBeenCalledWith(3, 4);
            expect(subjects.getSubTopicsByTopic).toHaveBeenCalledWith(11);
            expect(component.form.dirty).toBe(false);
            // Dosya 1 → basılı 0: sunucu kuralı (ilk basılı ≥ 1) ihlali, kayıt kapalı.
            expect(component.errors().map((e) => e.key)).toEqual(['printedMin']);
        });

        it('editOffset_PreviewUpdatesLive', async () => {
            setup();
            await component.select(MATLAB);

            component.offsets.at(0).controls.offset.setValue(1);
            fixture.detectChanges();

            expect(el().querySelector('.bk-preview')?.textContent).toContain('dosya 1 → basılı 2');
            expect(component.errors()).toEqual([]);
        });

        it('addOffset_ContinuesAfterPreviousRange', async () => {
            setup();
            await component.select(MATLAB);

            component.addOffset();

            expect(component.offsets.at(2).getRawValue()).toEqual({ fileFrom: 201, fileTo: null, offset: -100 });
            expect(component.form.dirty).toBe(true);
        });

        it('overlappingRanges_ShowsRowErrorAndDisablesSave', async () => {
            setup();
            await component.createNew();
            component.form.controls.name.setValue('Deneme');
            component.addAnswerKeyRange();
            component.addAnswerKeyRange();
            component.answerKeyRanges.at(0).setValue({ fileFrom: 10, fileTo: 20 });
            component.answerKeyRanges.at(1).setValue({ fileFrom: 15, fileTo: 30 });
            fixture.detectChanges();

            expect(text()).toContain('2. satır 1. satırla çakışıyor.');
            const submit = el().querySelector('button[type="submit"]') as HTMLButtonElement;
            expect(submit.disabled).toBe(true);
            component.save();
            expect(api.create).not.toHaveBeenCalled();
        });

        it('fromAfterTo_ShowsRowError', async () => {
            setup();
            await component.createNew();
            component.addAnswerKeyRange();
            component.answerKeyRanges.at(0).setValue({ fileFrom: 30, fileTo: 20 });
            fixture.detectChanges();

            expect(text()).toContain("1. satır: başlangıç bitişten büyük olamaz.");
        });

        it('tocGradeChange_ResetsSubjectTopicAndLoadsSubjects', async () => {
            setup();
            await component.select(MATLAB);
            const row = component.toc.at(0);

            row.controls.gradeId.setValue(5);
            component.onTocGradeChange(0);

            expect(row.getRawValue()).toEqual(expect.objectContaining({ gradeId: 5, subjectId: null, topicId: null, subTopicId: null }));
            expect(subjects.getSubjectsByGrade).toHaveBeenCalledWith(5);
            expect(component.errors().some((e) => e.key === 'subjectRequired')).toBe(true);
        });

        it('canonicalOptions_ExcludeSelfAndBooksThatAreThemselvesEditions', async () => {
            setup();
            await component.select(MATLAB);
            // COMBO başka kitabın baskısı: asıl baskı olarak seçilemez (tek seviye).
            expect(component.canonicalOptions().map((b) => b.id)).toEqual([]);

            await component.select(COMBO);
            expect(component.canonicalOptions().map((b) => b.id)).toEqual([7]);
        });

        it('switchWithUnsavedChanges_AsksAndStaysWhenDeclined', async () => {
            setup();
            await component.select(MATLAB);
            component.form.controls.name.setValue('Değişti');
            component.form.markAsDirty();
            const open = confirmWith(false);

            await component.select(COMBO);

            expect(open).toHaveBeenCalled();
            expect(component.selectedId()).toBe(7);
            expect(component.form.controls.name.value).toBe('Değişti');
        });
    });

    describe('kaydet / sil', () => {
        it('saveNew_PostsTrimmedRequest_SelectsSaved', async () => {
            setup();
            await component.createNew();
            component.form.controls.name.setValue('  Yeni Kitap ');
            component.form.controls.canonicalBookId.setValue(7);
            component.addOffset();
            component.offsets.at(0).setValue({ fileFrom: 1, fileTo: 40, offset: 2 });
            component.addTocEntry();
            component.toc.at(0).setValue({ fileFrom: 3, fileTo: 10, gradeId: 4, subjectId: 3, topicId: 11, subTopicId: null });
            const saved: LabelingBook = { ...COMBO, id: 12, name: 'Yeni Kitap', canonicalBookId: 7 };
            api.create.mockReturnValue(of(saved));
            vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.save();

            expect(api.create).toHaveBeenCalledWith({
                name: 'Yeni Kitap',
                canonicalBookId: 7,
                notes: null,
                offsets: [{ fileFrom: 1, fileTo: 40, offset: 2 }],
                answerKeyRanges: [],
                toc: [{ fileFrom: 3, fileTo: 10, gradeId: 4, subjectId: 3, topicId: 11, subTopicId: null }],
            });
            expect(component.selectedId()).toBe(12);
            expect(component.books().map((b) => b.id)).toContain(12);
            expect(component.form.dirty).toBe(false);
        });

        it('saveExisting_PutsById', async () => {
            setup();
            await component.select(COMBO);
            component.form.controls.notes.setValue('kurumsal baskı');
            api.update.mockReturnValue(of({ ...COMBO, notes: 'kurumsal baskı' }));
            vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            component.save();

            expect(api.update).toHaveBeenCalledWith(8, expect.objectContaining({ name: 'Güneş Koleji Türkçe', notes: 'kurumsal baskı' }));
            expect(component.selectedBook()?.notes).toBe('kurumsal baskı');
        });

        it('saveFails_ShowsServerMessage', async () => {
            setup();
            await component.select(COMBO);
            api.update.mockReturnValue(throwError(() => ({ status: 400, error: { message: 'Konu bu derse ait değil.' } })));

            component.save();
            fixture.detectChanges();

            expect(component.saving()).toBe(false);
            expect(text()).toContain('Konu bu derse ait değil.');
        });

        it('rename403_WithoutMessage_ShowsRenameForbidden_WithMessage_ShowsServerMessage', async () => {
            setup();
            await component.select(COMBO);
            component.form.controls.name.setValue('Yeni ad');
            api.update.mockReturnValue(throwError(() => ({ status: 403, error: { errorCode: 'LabelingBookRenameForbidden' } })));

            component.save();
            fixture.detectChanges();
            expect(text()).toContain(booksTr.renameForbidden);

            api.update.mockReturnValue(throwError(() => ({ status: 403, error: { errorCode: 'LabelingBookRenameForbidden', message: 'Ad değiştirilemez.' } })));
            component.save();
            fixture.detectChanges();
            expect(text()).toContain('Ad değiştirilemez.');
        });

        it('delete_Confirmed_RemovesFromList', async () => {
            setup();
            await component.select(COMBO);
            confirmWith(true);
            api.delete.mockReturnValue(of(undefined));
            vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            await component.remove();

            expect(api.delete).toHaveBeenCalledWith(8);
            expect(component.books().map((b) => b.id)).toEqual([7]);
            expect(component.selectedId()).toBeNull();
        });

        it('delete_InUse409_ShowsServerMessageAndKeepsBook', async () => {
            setup();
            await component.select(COMBO);
            confirmWith(true);
            api.delete.mockReturnValue(throwError(() => ({ status: 409, error: { message: 'Kitap kullanımda.' } })));
            const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);

            await component.remove();

            expect(snack).toHaveBeenCalledWith('Kitap kullanımda.', booksTr.ok, expect.any(Object));
            expect(component.books().length).toBe(2);
            expect(component.selectedId()).toBe(8);
        });
    });

    describe('dönüştürücü', () => {
        it('fileToPrinted_CallsPageEndpoint_ShowsPrintedAnswerKeyAndToc', async () => {
            setup();
            await component.select(MATLAB);
            fixture.detectChanges();
            api.getPage.mockReturnValue(of({ filePage: 40, printedPage: 39, isAnswerKey: false, toc: MATLAB.toc[0] }));

            const input = el().querySelector('.bk-conv-file') as HTMLInputElement;
            input.value = '40';
            input.dispatchEvent(new Event('input'));
            component.convertFile();
            fixture.detectChanges();

            expect(api.getPage).toHaveBeenCalledWith(7, 40);
            expect(el().querySelector('.bk-conv-printed')?.textContent?.trim()).toBe('dosya 40 → basılı 39');
            expect(el().querySelector('.bk-pill--toc')?.textContent).toContain('Fen Bilimleri › Maddenin Hâlleri');
        });

        it('printedToFile_Fascicles_ListsAllFilePagesWithNear', async () => {
            setup();
            await component.select(MATLAB);
            fixture.detectChanges();
            api.getFilePages.mockReturnValue(of({ printedPage: 1, filePages: [101, 2] }));

            component.convPrinted.set(1);
            component.convNear.set(120);
            component.convertPrinted();
            fixture.detectChanges();

            expect(api.getFilePages).toHaveBeenCalledWith(7, 1, 120);
            expect(el().querySelector('.bk-conv-files')?.textContent?.trim()).toBe('basılı 1 → dosya 101, 2');
        });

        it('fileToPrinted_TocStale_ShowsWarning', async () => {
            setup();
            await component.select(MATLAB);
            api.getPage.mockReturnValue(of({ filePage: 40, printedPage: 39, isAnswerKey: false, toc: null, tocStale: true }));

            component.convFile.set(40);
            component.convertFile();
            fixture.detectChanges();

            expect(el().querySelector('.bk-conv-stale')?.textContent).toContain(booksTr.converter.tocStale);
        });

        it('converterError_ShowsFailure', async () => {
            setup();
            await component.select(MATLAB);
            api.getPage.mockReturnValue(throwError(() => ({ status: 500 })));

            component.convFile.set(40);
            component.convertFile();
            fixture.detectChanges();

            expect(text()).toContain(booksTr.converter.failed);
        });

        it('newBook_ConverterHidden', async () => {
            setup();
            await component.createNew();
            fixture.detectChanges();

            expect(el().querySelector('.bk-conv')).toBeNull();
        });
    });
});

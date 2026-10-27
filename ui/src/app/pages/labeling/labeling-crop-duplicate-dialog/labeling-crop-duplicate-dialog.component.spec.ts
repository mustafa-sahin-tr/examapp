import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';

import { LabelingCropDuplicateDialogComponent } from './labeling-crop-duplicate-dialog.component';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import labelingTr from '../../../../../public/i18n/labeling/tr.json';

describe('LabelingCropDuplicateDialogComponent (#577)', () => {
    const close = vi.fn();

    function setup() {
        close.mockReset();
        TestBed.configureTestingModule({
            imports: [LabelingCropDuplicateDialogComponent, translocoTestingModule({ langs: { 'labeling/tr': labelingTr } })],
            providers: [
                provideRouter([]),
                { provide: MatDialogRef, useValue: { close } },
                {
                    provide: MAT_DIALOG_DATA,
                    useValue: {
                        existing: [
                            { questionIndex: 0, questionId: 501, bookName: 'TT MatLab', filePage: 40, printedPage: 39, activityLabel: 'Etkinlik 9', itemLabel: '3' },
                            { questionIndex: 0, questionId: 777, bookName: 'Güneş Koleji', filePage: 12, printedPage: null, activityLabel: null, itemLabel: null },
                            { questionIndex: 1, otherOwner: true },
                        ],
                    },
                },
            ],
        });
        const fixture = TestBed.createComponent(LabelingCropDuplicateDialogComponent);
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    it('listsExistingRecordsWithLinksToQuestions', () => {
        const el = setup();

        const items = el.querySelectorAll('.cd-item');
        expect(items.length).toBe(3);
        expect(items[2].querySelector('a')).toBeNull();
        expect(items[2].textContent?.trim()).toBe(labelingTr.bank.cropDuplicate.otherOwner);
        const link = items[0].querySelector('a.cd-link') as HTMLAnchorElement;
        expect(link.getAttribute('href')).toBe('/question/501');
        expect(link.target).toBe('_blank');
        expect(items[0].textContent).toContain('TT MatLab');
        expect(items[0].textContent).toContain('dosya 40');
        expect(items[0].textContent).toContain('basılı 39');
        expect(items[0].textContent).toContain('Etkinlik 9');
        expect(items[0].textContent).toContain('madde 3');
        expect(items[1].textContent).not.toContain('basılı');
        expect(el.textContent).toContain('3 kayıtta');
    });

    it('force_ClosesTrue_Cancel_ClosesFalse', () => {
        const el = setup();

        (el.querySelector('.cd-force') as HTMLButtonElement).click();
        expect(close).toHaveBeenLastCalledWith(true);
        (el.querySelector('.cd-cancel') as HTMLButtonElement).click();
        expect(close).toHaveBeenLastCalledWith(false);
    });
});

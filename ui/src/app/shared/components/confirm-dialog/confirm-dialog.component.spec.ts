import type { MockedObject } from "vitest";
// #545 — Angular 21 control-flow migrasyonu (*ngIf → @if) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { ConfirmDialogComponent, ConfirmDialogData } from './confirm-dialog.component';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('ConfirmDialogComponent (#545)', () => {
    let fixture: ComponentFixture<ConfirmDialogComponent>;
    let dialogRef: MockedObject<MatDialogRef<ConfirmDialogComponent, boolean>>;

    const el = () => fixture.nativeElement as HTMLElement;
    const iconContainer = () => el().querySelector('.icon-container');
    const buttons = () => Array.from(el().querySelectorAll('button')) as HTMLButtonElement[];
    const cancelButton = () => el().querySelector('.cancel-btn') as HTMLButtonElement;
    const confirmButton = () => el().querySelector('.confirm-btn') as HTMLButtonElement;

    function setup(data: ConfirmDialogData): void {
        dialogRef = {
            close: vi.fn().mockName("MatDialogRef.close")
        } as unknown as MockedObject<MatDialogRef<ConfirmDialogComponent, boolean>>;
        TestBed.configureTestingModule({
            imports: [ConfirmDialogComponent, NoopAnimationsModule, translocoTestingModule()],
            providers: [
                { provide: MatDialogRef, useValue: dialogRef },
                { provide: MAT_DIALOG_DATA, useValue: data },
            ],
        });
        fixture = TestBed.createComponent(ConfirmDialogComponent);
        fixture.detectChanges();
    }

    it('template_IconAndColorGiven_RendersIconWithColorClass', () => {
        setup({ title: 'Sil', message: 'Emin misiniz?', icon: 'delete', confirmColor: 'warn' });

        expect(iconContainer()).not.toBeNull();
        const icon = el().querySelector('.dialog-icon');
        expect(icon?.textContent?.trim()).toBe('delete');
        expect(icon?.classList.contains('icon-warn')).toBe(true);
    });

    it('template_NoIcon_DoesNotRenderIconContainer', () => {
        setup({ title: 'Onay', message: 'Devam?' });

        expect(iconContainer()).toBeNull();
        expect(el().querySelector('.dialog-icon')).toBeNull();
    });

    it('template_NoCustomButtonTexts_UsesCommonTranslations', () => {
        setup({ title: 'Onay', message: 'Devam?' });

        expect(cancelButton().textContent?.trim()).toBe('İptal');
        expect(confirmButton().textContent?.trim()).toBe('Onayla');
    });

    it('template_CustomButtonTexts_OverridesDefaults', () => {
        setup({ title: 'Yenile', message: 'Devam?', cancelText: 'Vazgeç', confirmText: 'Yenile' });

        expect(cancelButton().textContent?.trim()).toBe('Vazgeç');
        expect(confirmButton().textContent?.trim()).toBe('Yenile');
        expect(buttons().length).toBe(2);
    });

    it('onCancel_CancelClicked_ClosesWithFalse', () => {
        setup({ title: 'Onay', message: 'Devam?' });

        cancelButton().click();

        expect(dialogRef.close).toHaveBeenCalledTimes(1);

        expect(dialogRef.close).toHaveBeenCalledWith(false);
    });

    it('onConfirm_ConfirmClicked_ClosesWithTrue', () => {
        setup({ title: 'Onay', message: 'Devam?' });

        confirmButton().click();

        expect(dialogRef.close).toHaveBeenCalledTimes(1);

        expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
});

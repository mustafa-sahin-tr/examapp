import type { Mock, MockedObject } from "vitest";
// #545 — Angular 21 control-flow migrasyonu (*ngIf → @if) sonrası davranış sözleşmesi.
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { ClassifierCacheComponent } from './classifier-cache.component';
import { AdminService } from '../../../services/admin.service';
import { ClassifierCacheRefreshResult, ClassifierCacheStatus } from '../../../models/taxonomy';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import adminTr from '../../../../../public/i18n/admin/tr.json';

const T = adminTr.classifierCache;

function status(overrides: Partial<ClassifierCacheStatus> = {}): ClassifierCacheStatus {
    return {
        cachedContentName: 'cachedContents/abc',
        model: 'gemini-test',
        refreshedAt: null,
        subTopicCount: 12,
        configuredInSettings: true,
        stale: false,
        ...overrides,
    };
}

describe('ClassifierCacheComponent (#545)', () => {
    let fixture: ComponentFixture<ClassifierCacheComponent>;
    let admin: MockedObject<AdminService>;
    // MatDialog/MatSnackBar, komponentin `imports` içindeki MatDialogModule/MatSnackBarModule ile
    // komponent ortam enjektöründe sağlanır; TestBed `providers` bunları ezemez. Bu yüzden
    // komponentin gördüğü örnek (`debugElement.injector`) üzerinde spy kurulur.
    let dialog: Mock;
    let snack: Mock;

    const el = () => fixture.nativeElement as HTMLElement;

    function setup(initial: ClassifierCacheStatus | 'error'): void {
        admin = {
            getClassifierCache: vi.fn().mockName("AdminService.getClassifierCache"),
            refreshClassifierCache: vi.fn().mockName("AdminService.refreshClassifierCache")
        } as unknown as MockedObject<AdminService>;
        admin.getClassifierCache.mockReturnValue(initial === 'error' ? throwError(() => new Error('down')) : of(initial));

        TestBed.configureTestingModule({
            imports: [
                ClassifierCacheComponent,
                NoopAnimationsModule,
                translocoTestingModule({ langs: { tr: { ...rootTr, admin: adminTr }, 'admin/tr': adminTr } }),
            ],
            providers: [{ provide: AdminService, useValue: admin }],
        });
        fixture = TestBed.createComponent(ClassifierCacheComponent);
        const injector = fixture.debugElement.injector;
        dialog = vi.spyOn(injector.get(MatDialog), 'open').mockReturnValue(undefined as never);
        snack = vi.spyOn(injector.get(MatSnackBar), 'open').mockReturnValue(undefined as never);
        fixture.detectChanges();
    }

    function refreshButton(): HTMLButtonElement {
        return Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.includes(T.refreshCache)) as HTMLButtonElement;
    }

    it('template_FreshStatus_ShowsFreshMessageAndCheckIcon', () => {
        setup(status({ stale: false }));

        expect(el().querySelector('.status-line')?.classList.contains('warn')).toBe(false);
        expect(el().textContent).toContain(T.fresh);
        expect(el().textContent).not.toContain(T.stale);
        expect(el().querySelector('.status-line mat-icon')?.textContent?.trim()).toBe('check_circle');
    });

    it('template_StaleStatus_ShowsStaleMessageAndWarnClass', () => {
        setup(status({ stale: true }));

        expect(el().querySelector('.status-line')?.classList.contains('warn')).toBe(true);
        expect(el().textContent).toContain(T.stale);
        expect(el().textContent).not.toContain(T.fresh);
        expect(el().querySelector('.status-line mat-icon')?.textContent?.trim()).toBe('warning');
    });

    it('template_NotCreatedCache_ShowsNotCreatedAndDashPlaceholder', () => {
        setup(status({ cachedContentName: null, model: null, stale: false }));

        expect(el().textContent).toContain(T.notCreated);
        expect(el().textContent).not.toContain(T.fresh);
        expect(el().textContent).not.toContain(T.stale);
        const cachedRow = Array.from(el().querySelectorAll('.details > div')).find((d) => d.querySelector('dt')?.textContent?.includes(T.fields.cachedContent));
        expect(cachedRow?.querySelector('dd')?.textContent?.trim()).toBe('—');
    });

    it('template_ApiKeyFlag_ShowsConfiguredOrMissingText', () => {
        setup(status({ configuredInSettings: false }));

        expect(el().textContent).toContain(T.apiKeyMissing);
        expect(el().textContent).not.toContain(T.apiKeyConfigured);
    });

    it('template_NoStatusLoaded_RendersNoDetailsBlock', () => {
        setup('error');

        expect(el().querySelector('.details')).toBeNull();
        expect(el().querySelector('.status-line')).toBeNull();
    });

    it('load_RequestFails_ShowsStatusFailedSnackAndNoStatus', () => {
        setup('error');

        expect(snack).toHaveBeenCalledWith(T.statusFailed, T.close, expect.any(Object));
        expect(fixture.componentInstance.status()).toBeNull();
        expect(fixture.componentInstance.loading()).toBe(false);
    });

    it('template_NotConfiguredInSettings_DisablesRefreshButton', () => {
        setup(status({ configuredInSettings: false }));

        expect(refreshButton().disabled).toBe(true);
    });

    it('refresh_UserCancelsDialog_DoesNotCallRefreshEndpoint', async () => {
        setup(status());
        dialog.mockReturnValue({ afterClosed: () => of(false) } as unknown as MatDialogRef<unknown, boolean>);

        await fixture.componentInstance.refresh();

        expect(admin.refreshClassifierCache).not.toHaveBeenCalled();
        expect(refreshButton().disabled).toBe(false);
    });

    it('refresh_UserConfirms_CallsEndpointShowsMessageAndReloadsStatus', async () => {
        setup(status());
        dialog.mockReturnValue({ afterClosed: () => of(true) } as unknown as MatDialogRef<unknown, boolean>);
        admin.refreshClassifierCache.mockReturnValue(of({ message: 'Yenilendi', cachedContentName: 'cachedContents/new' } as ClassifierCacheRefreshResult));

        await fixture.componentInstance.refresh();

        expect(admin.refreshClassifierCache).toHaveBeenCalledTimes(1);
        expect(snack).toHaveBeenCalledWith('Yenilendi', T.close, expect.any(Object));
        expect(admin.getClassifierCache).toHaveBeenCalledTimes(2);
    });
});

import { ApplicationRef, PLATFORM_ID, PendingTasks } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { TranslocoHttpLoader } from './transloco-http.loader';
import landingTr from '../../../public/i18n/landing/tr.json';
import rootTr from '../../../public/i18n/tr.json';

/**
 * Loader'in iki modu ayri ayri dogrulanir: tarayicida HTTP, SSR'da build'e gomulu `import()`.
 * SSR yolu prerender edilen HTML'in gercek metni icermesini sagladigi icin kritiktir (issue #182).
 */
describe('TranslocoHttpLoader', () => {
    function createLoader(platform: 'browser' | 'server'): TranslocoHttpLoader {
        TestBed.configureTestingModule({
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                { provide: PLATFORM_ID, useValue: platform },
                TranslocoHttpLoader,
            ],
        });

        return TestBed.inject(TranslocoHttpLoader);
    }

    describe('tarayici', () => {
        it('getTranslation_RootLang_RequestsRootDictionary', () => {
            const loader = createLoader('browser');
            const http = TestBed.inject(HttpTestingController);

            loader.getTranslation('tr').subscribe();

            const request = http.expectOne('/i18n/tr.json');
            expect(request.request.method).toBe('GET');
            request.flush({});
            http.verify();
        });

        it('getTranslation_ScopedLang_RequestsScopedDictionary', () => {
            const loader = createLoader('browser');
            const http = TestBed.inject(HttpTestingController);

            loader.getTranslation('landing/tr').subscribe();

            const request = http.expectOne('/i18n/landing/tr.json');
            expect(request.request.method).toBe('GET');
            request.flush({});
            http.verify();
        });

        it('getTranslation_UnknownLang_ReturnsEmptyWithoutRequest', async () => {
            const loader = createLoader('browser');
            const http = TestBed.inject(HttpTestingController);

            await expect(firstValueFrom(loader.getTranslation('de'))).resolves.toEqual({});

            http.verify();
        });
    });

    describe('SSR', () => {
        it('getTranslation_RootLang_LoadsBundledRootDictionary', async () => {
            const loader = createLoader('server');

            const translation = await firstValueFrom(loader.getTranslation('tr'));

            expect(translation).toEqual(rootTr);
        });

        it('getTranslation_ScopedLang_LoadsBundledScopeDictionary', async () => {
            const loader = createLoader('server');

            const translation = await firstValueFrom(loader.getTranslation('landing/tr'));

            expect(translation).toEqual(landingTr);
        });

        it('getTranslation_UnknownScope_ReturnsEmptyDictionary', async () => {
            const loader = createLoader('server');

            await expect(firstValueFrom(loader.getTranslation('no-such-scope/tr'))).resolves.toEqual({});
        });

        it('getTranslation_ScopeWithIllegalCharacters_ReturnsEmptyDictionaryWithoutImport', async () => {
            const loader = createLoader('server');

            await expect(firstValueFrom(loader.getTranslation('../secret/tr'))).resolves.toEqual({});
            await expect(firstValueFrom(loader.getTranslation('Landing/tr'))).resolves.toEqual({});
        });

        it('getTranslation_UnknownLang_ReturnsEmptyDictionary', async () => {
            const loader = createLoader('server');

            await expect(firstValueFrom(loader.getTranslation('landing/de'))).resolves.toEqual({});
        });

        // Issue #545 (Angular 20): PendingTasks.run() artık sonucu döndürmüyor; loader görevi add() ile açıp
        // sözlük gelince kapatmalı — aksi halde prerender sözlüğü beklemeden boş şablonu yazar.
        it('getTranslation_Server_HoldsPendingTaskUntilDictionaryLoaded', async () => {
            const loader = createLoader('server');
            const pendingTasks = TestBed.inject(PendingTasks);
            const removeTask = vi.fn().mockName('removeTask');
            vi.spyOn(pendingTasks, 'add').mockReturnValue(removeTask);

            const translation$ = loader.getTranslation('landing/tr');
            expect(pendingTasks.add).not.toHaveBeenCalled();

            const result = firstValueFrom(translation$);
            expect(pendingTasks.add).toHaveBeenCalledTimes(1);
            expect(removeTask).not.toHaveBeenCalled();

            expect(await result).toEqual(landingTr);
            expect(removeTask).toHaveBeenCalledTimes(1);
        });

        it('getTranslation_ServerImportFails_ReleasesPendingTask', async () => {
            const loader = createLoader('server');
            const pendingTasks = TestBed.inject(PendingTasks);
            const removeTask = vi.fn().mockName('removeTask');
            vi.spyOn(pendingTasks, 'add').mockReturnValue(removeTask);

            await expect(firstValueFrom(loader.getTranslation('no-such-scope/tr'))).resolves.toEqual({});

            expect(removeTask).toHaveBeenCalledTimes(1);
        });

        it('getTranslation_Server_DoesNotBlockStabilityAfterLoad', async () => {
            const loader = createLoader('server');
            const pendingTasks = TestBed.inject(PendingTasks);
            const addSpy = vi.spyOn(pendingTasks, 'add');

            await firstValueFrom(loader.getTranslation('tr'));

            expect(addSpy).toHaveBeenCalledTimes(1);
            await expect(TestBed.inject(ApplicationRef).whenStable()).resolves.not.toThrow();
        });
    });
});

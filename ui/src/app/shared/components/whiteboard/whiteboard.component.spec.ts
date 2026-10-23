import type { Mock } from "vitest";
import { PLATFORM_ID, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Subject } from 'rxjs';

import whiteboardTr from '../../../../../public/i18n/whiteboard/tr.json';
import { AuthService } from '../../../services/auth.service';
import { ColorScheme, ColorSchemeService } from '../../../services/color-scheme.service';
import { LocaleService } from '../../../services/locale.service';
import { WHITEBOARD_HUB_CONNECTION_FACTORY } from '../../../services/whiteboard-hub-connection';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { FakeCanvas, FakeCanvasLoader, FakeHubConnection, hubError, joinResult } from '../../testing/whiteboard-testing';
import { WHITEBOARD_CANVAS_LOADER } from './whiteboard-canvas';
import { WhiteboardLinkDialogComponent } from './whiteboard-link-dialog/whiteboard-link-dialog.component';
import { WHITEBOARD_CLOCK_TICK_MS, WHITEBOARD_MOUNT_TIMEOUT_MS, WhiteboardComponent } from './whiteboard.component';

/** Gerçek sözlük: bir anahtar bozulursa test kırılır. */
const translocoTesting = translocoTestingModule({ langs: { 'whiteboard/tr': whiteboardTr } });

describe('WhiteboardComponent', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let fixture: ComponentFixture<WhiteboardComponent>;
    let created: boolean;
    let hub: FakeHubConnection;
    let canvas: FakeCanvas;
    let loader: FakeCanvasLoader;
    let scheme: WritableSignal<ColorScheme>;
    let locale: WritableSignal<string>;
    let snackOpen: Mock;

    beforeEach(() => {
        hub = new FakeHubConnection();
        canvas = new FakeCanvas();
        loader = new FakeCanvasLoader(canvas);
        scheme = signal<ColorScheme>('dark');
        locale = signal('tr');

        TestBed.configureTestingModule({
            imports: [WhiteboardComponent, translocoTesting, NoopAnimationsModule],
            providers: [
                { provide: WHITEBOARD_CANVAS_LOADER, useValue: loader.load },
                { provide: WHITEBOARD_HUB_CONNECTION_FACTORY, useValue: () => hub },
                {
                    provide: AuthService,
                    useValue: { getToken: () => 'tkn', isExpiringSoon: () => false },
                },
                { provide: ColorSchemeService, useValue: { colorScheme: scheme.asReadonly() } },
                { provide: LocaleService, useValue: { locale: locale.asReadonly() } },
            ],
        });
        created = false;
    });

    /** Komponent sahte zamanlayıcıyla oluşturulur (tuval yükleme promise'leri sahte zamanda çözülsün). */
    function create(): void {
        snackOpen = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never);
        fixture = TestBed.createComponent(WhiteboardComponent);
        fixture.componentRef.setInput('bookingId', 7);
        created = true;
    }

    function text(): string {
        return (fixture.nativeElement as HTMLElement).textContent ?? '';
    }

    function query(selector: string): HTMLElement | null {
        return (fixture.nativeElement as HTMLElement).querySelector(selector);
    }

    /** İlk render + tuval yükleme + katılım. */
    async function render(): Promise<void> {
        if (!created) {
            create();
        }
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
    }

    async function finish(): Promise<void> {
        fixture.destroy();
        await vi.runAllTimersAsync();
    }

    it('LazyLoad_CanvasModuleLoadedOnlyAfterFirstRender', async () => {
        create();
        await vi.advanceTimersByTimeAsync(0);
        // Render edilmeden (ya da SSR'da) tuval modülü istenmez.
        expect(loader.loadCalls).toBe(0);

        await render();

        expect(loader.loadCalls).toBe(1);
        expect(loader.mountCalls.length).toBe(1);
        const mount = loader.mountCalls[0];
        expect(mount.host).toBe(query('.wb__canvas') as HTMLElement);
        expect(mount.options).toEqual({ theme: 'dark', langCode: 'tr-TR', readOnly: false });
        expect(hub.calls('JoinBoard')).toEqual([{ method: 'JoinBoard', args: [7] }]);
        expect(query('.wb__overlay')).toBeNull();
        await finish();
    });

    it('LazyLoad_ServerPlatform_NeverLoadsCanvasOrConnects', async () => {
        TestBed.overrideProvider(PLATFORM_ID, { useValue: 'server' });
        await render();
        expect(loader.loadCalls).toBe(0);
        expect(hub.startCalls).toBe(0);
        await finish();
    });

    it('LoaderFailure_ShowsErrorAndRetries', async () => {
        loader.fail = true;
        await render();
        expect(text()).toContain(whiteboardTr.loadFailed);

        loader.fail = false;
        (query('.wb__overlay--error button') as HTMLButtonElement).click();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        expect(loader.loadCalls).toBe(2);
        expect(query('.wb__overlay')).toBeNull();
        await finish();
    });

    it('StatusUi_ConnectedAndPeerOnline', async () => {
        hub.respond = () => Promise.resolve(joinResult({ role: 'teacher', peerOnline: true }));
        await render();
        expect(query('.wb__status')?.getAttribute('data-status')).toBe('connected');
        expect(text()).toContain(whiteboardTr.status.connected);
        expect(query('[data-testid="wb-peer"]')?.textContent).toContain('Öğrenci çevrimiçi');

        hub.emit('PeerPresenceChanged', 'student', false);
        fixture.detectChanges();
        expect(query('[data-testid="wb-peer"]')?.textContent).toContain('Öğrenci çevrimdışı');

        hub.simulateReconnecting();
        fixture.detectChanges();
        expect(text()).toContain(whiteboardTr.status.reconnecting);
        await finish();
    });

    it('EndingSoon_ShownFiveMinutesBeforeWindowCloses', async () => {
        hub.respond = () => Promise.resolve(joinResult({ windowClosesAtUtc: new Date(Date.now() + 6 * 60000).toISOString() }));
        await render();
        expect(query('[data-testid="wb-ending"]')).toBeNull();

        await vi.advanceTimersByTimeAsync(WHITEBOARD_CLOCK_TICK_MS * 5); // 75 sn → 4 dk 45 sn kaldı
        fixture.detectChanges();
        const notice = query('[data-testid="wb-ending"]');
        expect(notice).not.toBeNull();
        expect(notice?.textContent).toContain('5 dakika');
        await finish();
    });

    it('BoardClosed_CanvasReadOnlyAndMessageShown', async () => {
        await render();
        expect(canvas.viewModes.at(-1)).toBe(false);

        hub.emit('BoardClosed', 'BookingCancelled');
        fixture.detectChanges();

        expect(canvas.viewModes.at(-1)).toBe(true);
        expect(query('[data-testid="wb-closed"]')?.textContent).toContain(whiteboardTr.closed.BookingCancelled);
        expect(text()).toContain(whiteboardTr.status.closed);
        expect(query('[data-testid="wb-ending"]')).toBeNull();
        await finish();
    });

    // Issue #298: anlık kapanma / yeniden doğrulama — salt okunur ve nötr mesaj.
    it('BoardClosed_TeacherUnavailable_ReadOnlyWithNeutralMessage', async () => {
        await render();
        hub.emit('BoardClosed', 'TeacherUnavailable');
        fixture.detectChanges();

        expect(canvas.viewModes.at(-1)).toBe(true);
        expect(query('[data-testid="wb-closed"]')?.textContent).toContain(whiteboardTr.closed.TeacherUnavailable);
        expect(query('[data-testid="wb-error"]')).toBeNull();
        await finish();
    });

    it('JoinError_TeacherUnavailable_ReadOnlyWithNeutralMessage', async () => {
        hub.respond = () => Promise.reject(hubError('TeacherUnavailable'));
        await render();

        expect(query('[data-testid="wb-closed"]')?.textContent).toContain(whiteboardTr.closed.TeacherUnavailable);
        expect(query('[data-testid="wb-error"]')).toBeNull();
        expect(hub.calls('JoinBoard').length).toBe(1);
        await finish();
    });

    it('JoinError_MappedToLocalizedMessageWithRetry', async () => {
        hub.respond = () => Promise.reject(hubError('NotParticipant'));
        await render();
        const error = query('[data-testid="wb-error"]');
        expect(error?.textContent).toContain(whiteboardTr.errors.NotParticipant);

        hub.respond = () => Promise.resolve(joinResult());
        (error?.querySelector('button') as HTMLButtonElement).click();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        expect(query('[data-testid="wb-error"]')).toBeNull();
        expect(hub.calls('JoinBoard').length).toBe(2);
        await finish();
    });

    it('Theme_FollowsAppColorScheme', async () => {
        await render();
        scheme.set('light');
        fixture.detectChanges();
        expect(canvas.themes.at(-1)).toBe('light');
        await finish();
    });

    it('Language_FollowsLocaleAndHtmlLangRestoredOnDestroy', async () => {
        await render();
        locale.set('en');
        fixture.detectChanges();
        expect(canvas.langCodes.at(-1)).toBe('en');

        // Excalidraw <html lang>'ı kendi koduyla (ör. tr-TR) değiştirir; yıkımda uygulama dili geri yazılır.
        const original = document.documentElement.lang;
        document.documentElement.lang = 'tr-TR';
        fixture.destroy();
        expect(document.documentElement.lang).toBe('en');
        document.documentElement.lang = original;
        await vi.runAllTimersAsync();
    });

    it('MountTimeout_AbortsShowsFailedAndRetries', async () => {
        loader.hang = true;
        await render();
        expect(query('.wb__overlay[aria-busy]')).not.toBeNull();

        await vi.advanceTimersByTimeAsync(WHITEBOARD_MOUNT_TIMEOUT_MS);
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        expect(loader.mountCalls[0].signal?.aborted).toBe(true);
        expect(text()).toContain(whiteboardTr.loadFailed);

        loader.hang = false;
        (query('.wb__overlay--error button') as HTMLButtonElement).click();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        expect(loader.loadCalls).toBe(2);
        expect(query('.wb__overlay')).toBeNull();
        await finish();
    });

    it('HiddenTabThenVisible_RefreshesCanvas', async () => {
        const callbacks: ResizeObserverCallback[] = [];
        const original = window.ResizeObserver;
        window.ResizeObserver = class {
            constructor(callback: ResizeObserverCallback) {
                callbacks.push(callback);
            }
            observe(): void { }
            unobserve(): void { }
            disconnect(): void { }
        } as unknown as typeof ResizeObserver;
        try {
            await render();
            const resize = (width: number, height: number): void => callbacks[0]([{ contentRect: { width, height } } as ResizeObserverEntry], {} as ResizeObserver);

            resize(0, 0); // gizli sekme
            expect(canvas.refreshCalls).toBe(0);
            resize(390, 600); // sekme görünür oldu
            expect(canvas.refreshCalls).toBe(1);
            resize(400, 600); // sıradan yeniden boyutlanma: Excalidraw kendisi ele alır
            expect(canvas.refreshCalls).toBe(1);
        }
        finally {
            window.ResizeObserver = original;
        }
        await finish();
    });

    it('PeerPointer_ForwardedToCanvasWithRoleLabel', async () => {
        await render();
        hub.emit('PointerUpdated', 'student', { x: 3, y: 4, tool: 'pointer' });
        fixture.detectChanges();
        expect(canvas.peer).toEqual({ pointer: { x: 3, y: 4, tool: 'pointer', role: 'student' }, label: 'Öğrenci' });
        await finish();
    });

    it('Warnings_LinkBlockedAndOversize_ShowSnackbar', async () => {
        await render();
        loader.mountCalls[0].callbacks.onLinkOpen('javascript:alert(1)');
        expect(snackOpen).toHaveBeenCalledWith(whiteboardTr.warnings.linkBlocked, whiteboardTr.dismiss, expect.any(Object));

        canvas.elements = [
            {
                id: 'huge',
                type: 'freedraw',
                version: 1,
                versionNonce: 1,
                isDeleted: false,
                points: Array.from({ length: 8000 }, () => [123.456, 789.012]),
            } as never,
        ];
        loader.mountCalls[0].callbacks.onChange();
        await vi.advanceTimersByTimeAsync(100);
        await vi.advanceTimersByTimeAsync(0);
        expect(snackOpen).toHaveBeenCalledWith(whiteboardTr.warnings.elementTooLarge, whiteboardTr.dismiss, expect.any(Object));
        expect(hub.calls('SendElements').length).toBe(0);
        await finish();
    });

    describe('link open (issue #332)', () => {
        let windowOpen: Mock;
        let dialogOpen: Mock;
        let dialogResult: Subject<boolean | undefined>;

        async function renderWithLinkSpies(): Promise< void > {
            await render();
            windowOpen = vi.spyOn(window, 'open').mockReturnValue(null);
            dialogResult = new Subject<boolean | undefined>();
            dialogOpen = vi.spyOn(fixture.debugElement.injector.get(MatDialog), 'open').mockReturnValue({
                afterClosed: () => dialogResult.asObservable(),
            } as unknown as MatDialogRef<unknown>);
        }

        it('LinkOpen_BlockedSchemes_NeverOpenOrAskAndShowSnackbar', async () => {
            await renderWithLinkSpies();
            for (const link of ['javascript:alert(1)', 'data:text/html,x', 'file:///etc/passwd', 'vbscript:x', '/relative', null]) {
                loader.mountCalls[0].callbacks.onLinkOpen(link);
            }
            expect(windowOpen).not.toHaveBeenCalled();
            expect(dialogOpen).not.toHaveBeenCalled();
            expect(snackOpen).toHaveBeenCalledWith(whiteboardTr.warnings.linkBlocked, whiteboardTr.dismiss, expect.any(Object));
            await finish();
        });

        it('LinkOpen_SameOrigin_OpensDirectlyInNewTabWithoutDialog', async () => {
            await renderWithLinkSpies();
            const url = `${window.location.origin}/programs/3/detail`;
            loader.mountCalls[0].callbacks.onLinkOpen(url);
            expect(dialogOpen).not.toHaveBeenCalled();
            expect(windowOpen).toHaveBeenCalledTimes(1);
            expect(windowOpen).toHaveBeenCalledWith(url, '_blank', 'noopener,noreferrer');
            await finish();
        });

        it('LinkOpen_ExternalConfirmed_ShowsUrlThenOpensWithNoopener', async () => {
            await renderWithLinkSpies();
            loader.mountCalls[0].callbacks.onLinkOpen('https://Phish.example.org/login?x=1');

            expect(dialogOpen).toHaveBeenCalledTimes(1);

            expect(dialogOpen).toHaveBeenCalledWith(WhiteboardLinkDialogComponent, expect.objectContaining({ data: { url: 'https://phish.example.org/login?x=1', host: 'phish.example.org' } }));
            expect(windowOpen).not.toHaveBeenCalled();

            dialogResult.next(true);
            expect(windowOpen).toHaveBeenCalledTimes(1);
            expect(windowOpen).toHaveBeenCalledWith('https://phish.example.org/login?x=1', '_blank', 'noopener,noreferrer');
            await finish();
        });

        it('LinkOpen_SameOriginSensitivePath_GoesThroughDialog', async () => {
            await renderWithLinkSpies();
            loader.mountCalls[0].callbacks.onLinkOpen(`${window.location.origin}/realms/exam/protocol/openid-connect/auth`);
            expect(dialogOpen).toHaveBeenCalledTimes(1);
            expect(windowOpen).not.toHaveBeenCalled();
            await finish();
        });

        it('LinkOpen_DialogAlreadyOpen_NoSecondDialog', async () => {
            await renderWithLinkSpies();
            loader.mountCalls[0].callbacks.onLinkOpen('https://a.example.org');
            loader.mountCalls[0].callbacks.onLinkOpen('https://b.example.org');
            expect(dialogOpen).toHaveBeenCalledTimes(1);

            dialogResult.next(true);
            expect(windowOpen).toHaveBeenCalledTimes(1);
            expect(windowOpen).toHaveBeenCalledWith('https://a.example.org/', '_blank', 'noopener,noreferrer');
            await finish();
        });

        it('LinkOpen_ExternalCancelledOrDismissed_DoesNotOpen', async () => {
            await renderWithLinkSpies();
            loader.mountCalls[0].callbacks.onLinkOpen('https://example.org');
            dialogResult.next(false);
            loader.mountCalls[0].callbacks.onLinkOpen('https://example.org');
            dialogResult.next(undefined); // ESC / backdrop
            expect(dialogOpen).toHaveBeenCalledTimes(2);
            expect(windowOpen).not.toHaveBeenCalled();
            await finish();
        });
    });

    it('Destroy_UnmountsCanvasAndStopsConnection', async () => {
        await render();
        fixture.destroy();
        expect(canvas.destroyed).toBe(true);
        expect(hub.stopCalls).toBe(1);
        await vi.runAllTimersAsync();
    });
});

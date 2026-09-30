import { PLATFORM_ID, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import whiteboardTr from '../../../../../public/i18n/whiteboard/tr.json';
import { AuthService } from '../../../services/auth.service';
import { ColorScheme, ColorSchemeService } from '../../../services/color-scheme.service';
import { LocaleService } from '../../../services/locale.service';
import { WHITEBOARD_HUB_CONNECTION_FACTORY } from '../../../services/whiteboard-hub-connection';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { FakeCanvas, FakeCanvasLoader, FakeHubConnection, hubError, joinResult } from '../../testing/whiteboard-testing';
import { WHITEBOARD_CANVAS_LOADER } from './whiteboard-canvas';
import { WHITEBOARD_CLOCK_TICK_MS, WHITEBOARD_MOUNT_TIMEOUT_MS, WhiteboardComponent } from './whiteboard.component';

/** Gerçek sözlük: bir anahtar bozulursa test kırılır. */
const translocoTesting = translocoTestingModule({ langs: { 'whiteboard/tr': whiteboardTr } });

describe('WhiteboardComponent', () => {
  let fixture: ComponentFixture<WhiteboardComponent>;
  let created: boolean;
  let hub: FakeHubConnection;
  let canvas: FakeCanvas;
  let loader: FakeCanvasLoader;
  let scheme: WritableSignal<ColorScheme>;
  let locale: WritableSignal<string>;
  let snackOpen: jasmine.Spy;

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

  /** Komponent fakeAsync içinde oluşturulur (tuval yükleme promise'leri sahte zamanda çözülsün). */
  function create(): void {
    snackOpen = spyOn(TestBed.inject(MatSnackBar), 'open');
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
  function render(): void {
    if (!created) {
      create();
    }
    fixture.detectChanges();
    flushMicrotasks();
    fixture.detectChanges();
  }

  function finish(): void {
    fixture.destroy();
    discardPeriodicTasks();
    flush();
  }

  it('LazyLoad_CanvasModuleLoadedOnlyAfterFirstRender', fakeAsync(() => {
    create();
    flushMicrotasks();
    // Render edilmeden (ya da SSR'da) tuval modülü istenmez.
    expect(loader.loadCalls).toBe(0);

    render();

    expect(loader.loadCalls).toBe(1);
    expect(loader.mountCalls.length).toBe(1);
    const mount = loader.mountCalls[0];
    expect(mount.host).toBe(query('.wb__canvas') as HTMLElement);
    expect(mount.options).toEqual({ theme: 'dark', langCode: 'tr-TR', readOnly: false });
    expect(hub.calls('JoinBoard')).toEqual([{ method: 'JoinBoard', args: [7] }]);
    expect(query('.wb__overlay')).toBeNull();
    finish();
  }));

  it('LazyLoad_ServerPlatform_NeverLoadsCanvasOrConnects', fakeAsync(() => {
    TestBed.overrideProvider(PLATFORM_ID, { useValue: 'server' });
    render();
    expect(loader.loadCalls).toBe(0);
    expect(hub.startCalls).toBe(0);
    finish();
  }));

  it('LoaderFailure_ShowsErrorAndRetries', fakeAsync(() => {
    loader.fail = true;
    render();
    expect(text()).toContain(whiteboardTr.loadFailed);

    loader.fail = false;
    (query('.wb__overlay--error button') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(loader.loadCalls).toBe(2);
    expect(query('.wb__overlay')).toBeNull();
    finish();
  }));

  it('StatusUi_ConnectedAndPeerOnline', fakeAsync(() => {
    hub.respond = () => Promise.resolve(joinResult({ role: 'teacher', peerOnline: true }));
    render();
    expect(query('.wb__status')?.getAttribute('data-status')).toBe('connected');
    expect(text()).toContain(whiteboardTr.status.connected);
    expect(query('[data-testid="wb-peer"]')?.textContent).toContain('Öğrenci çevrimiçi');

    hub.emit('PeerPresenceChanged', 'student', false);
    fixture.detectChanges();
    expect(query('[data-testid="wb-peer"]')?.textContent).toContain('Öğrenci çevrimdışı');

    hub.simulateReconnecting();
    fixture.detectChanges();
    expect(text()).toContain(whiteboardTr.status.reconnecting);
    finish();
  }));

  it('EndingSoon_ShownFiveMinutesBeforeWindowCloses', fakeAsync(() => {
    hub.respond = () =>
      Promise.resolve(joinResult({ windowClosesAtUtc: new Date(Date.now() + 6 * 60_000).toISOString() }));
    render();
    expect(query('[data-testid="wb-ending"]')).toBeNull();

    tick(WHITEBOARD_CLOCK_TICK_MS * 5); // 75 sn → 4 dk 45 sn kaldı
    fixture.detectChanges();
    const notice = query('[data-testid="wb-ending"]');
    expect(notice).not.toBeNull();
    expect(notice?.textContent).toContain('5 dakika');
    finish();
  }));

  it('BoardClosed_CanvasReadOnlyAndMessageShown', fakeAsync(() => {
    render();
    expect(canvas.viewModes.at(-1)).toBeFalse();

    hub.emit('BoardClosed', 'BookingCancelled');
    fixture.detectChanges();

    expect(canvas.viewModes.at(-1)).toBeTrue();
    expect(query('[data-testid="wb-closed"]')?.textContent).toContain(whiteboardTr.closed.BookingCancelled);
    expect(text()).toContain(whiteboardTr.status.closed);
    expect(query('[data-testid="wb-ending"]')).toBeNull();
    finish();
  }));

  // Issue #298: anlık kapanma / yeniden doğrulama — salt okunur ve nötr mesaj.
  it('BoardClosed_TeacherUnavailable_ReadOnlyWithNeutralMessage', fakeAsync(() => {
    render();
    hub.emit('BoardClosed', 'TeacherUnavailable');
    fixture.detectChanges();

    expect(canvas.viewModes.at(-1)).toBeTrue();
    expect(query('[data-testid="wb-closed"]')?.textContent).toContain(whiteboardTr.closed.TeacherUnavailable);
    expect(query('[data-testid="wb-error"]')).toBeNull();
    finish();
  }));

  it('JoinError_TeacherUnavailable_ReadOnlyWithNeutralMessage', fakeAsync(() => {
    hub.respond = () => Promise.reject(hubError('TeacherUnavailable'));
    render();

    expect(query('[data-testid="wb-closed"]')?.textContent).toContain(whiteboardTr.closed.TeacherUnavailable);
    expect(query('[data-testid="wb-error"]')).toBeNull();
    expect(hub.calls('JoinBoard').length).toBe(1);
    finish();
  }));

  it('JoinError_MappedToLocalizedMessageWithRetry', fakeAsync(() => {
    hub.respond = () => Promise.reject(hubError('NotParticipant'));
    render();
    const error = query('[data-testid="wb-error"]');
    expect(error?.textContent).toContain(whiteboardTr.errors.NotParticipant);

    hub.respond = () => Promise.resolve(joinResult());
    (error?.querySelector('button') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(query('[data-testid="wb-error"]')).toBeNull();
    expect(hub.calls('JoinBoard').length).toBe(2);
    finish();
  }));

  it('Theme_FollowsAppColorScheme', fakeAsync(() => {
    render();
    scheme.set('light');
    fixture.detectChanges();
    expect(canvas.themes.at(-1)).toBe('light');
    finish();
  }));

  it('Language_FollowsLocaleAndHtmlLangRestoredOnDestroy', fakeAsync(() => {
    render();
    locale.set('en');
    fixture.detectChanges();
    expect(canvas.langCodes.at(-1)).toBe('en');

    // Excalidraw <html lang>'ı kendi koduyla (ör. tr-TR) değiştirir; yıkımda uygulama dili geri yazılır.
    const original = document.documentElement.lang;
    document.documentElement.lang = 'tr-TR';
    fixture.destroy();
    expect(document.documentElement.lang).toBe('en');
    document.documentElement.lang = original;
    discardPeriodicTasks();
    flush();
  }));

  it('MountTimeout_AbortsShowsFailedAndRetries', fakeAsync(() => {
    loader.hang = true;
    render();
    expect(query('.wb__overlay[aria-busy]')).not.toBeNull();

    tick(WHITEBOARD_MOUNT_TIMEOUT_MS);
    flushMicrotasks();
    fixture.detectChanges();
    expect(loader.mountCalls[0].signal?.aborted).toBeTrue();
    expect(text()).toContain(whiteboardTr.loadFailed);

    loader.hang = false;
    (query('.wb__overlay--error button') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(loader.loadCalls).toBe(2);
    expect(query('.wb__overlay')).toBeNull();
    finish();
  }));

  it('HiddenTabThenVisible_RefreshesCanvas', fakeAsync(() => {
    const callbacks: ResizeObserverCallback[] = [];
    const original = window.ResizeObserver;
    window.ResizeObserver = class {
      constructor(callback: ResizeObserverCallback) {
        callbacks.push(callback);
      }
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    } as unknown as typeof ResizeObserver;
    try {
      render();
      const resize = (width: number, height: number): void =>
        callbacks[0]([{ contentRect: { width, height } } as ResizeObserverEntry], {} as ResizeObserver);

      resize(0, 0); // gizli sekme
      expect(canvas.refreshCalls).toBe(0);
      resize(390, 600); // sekme görünür oldu
      expect(canvas.refreshCalls).toBe(1);
      resize(400, 600); // sıradan yeniden boyutlanma: Excalidraw kendisi ele alır
      expect(canvas.refreshCalls).toBe(1);
    } finally {
      window.ResizeObserver = original;
    }
    finish();
  }));

  it('PeerPointer_ForwardedToCanvasWithRoleLabel', fakeAsync(() => {
    render();
    hub.emit('PointerUpdated', 'student', { x: 3, y: 4, tool: 'pointer' });
    fixture.detectChanges();
    expect(canvas.peer).toEqual({ pointer: { x: 3, y: 4, tool: 'pointer', role: 'student' }, label: 'Öğrenci' });
    finish();
  }));

  it('Warnings_LinkBlockedAndOversize_ShowSnackbar', fakeAsync(() => {
    render();
    loader.mountCalls[0].callbacks.onLinkBlocked();
    expect(snackOpen).toHaveBeenCalledWith(whiteboardTr.warnings.linkBlocked, whiteboardTr.dismiss, jasmine.any(Object));

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
    tick(100);
    flushMicrotasks();
    expect(snackOpen).toHaveBeenCalledWith(whiteboardTr.warnings.elementTooLarge, whiteboardTr.dismiss, jasmine.any(Object));
    expect(hub.calls('SendElements').length).toBe(0);
    finish();
  }));

  it('Destroy_UnmountsCanvasAndStopsConnection', fakeAsync(() => {
    render();
    fixture.destroy();
    expect(canvas.destroyed).toBeTrue();
    expect(hub.stopCalls).toBe(1);
    discardPeriodicTasks();
    flush();
  }));
});

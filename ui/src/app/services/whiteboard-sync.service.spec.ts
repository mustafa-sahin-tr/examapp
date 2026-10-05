import { TestBed, fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import { of } from 'rxjs';

import { WhiteboardWarning } from '../models/whiteboard.model';
import {
  FakeCanvas,
  FakeHubConnection,
  element,
  hubError,
  joinResult,
} from '../shared/testing/whiteboard-testing';
import {
  WHITEBOARD_MAX_SCENE_ELEMENTS,
  WHITEBOARD_TOMBSTONE_TTL_MS,
} from '../shared/utils/whiteboard-sync.util';
import { AuthService } from './auth.service';
import { WHITEBOARD_HUB_CONNECTION_FACTORY, WhiteboardHubConnectionFactory } from './whiteboard-hub-connection';
import {
  WHITEBOARD_FLUSH_THROTTLE_MS,
  WHITEBOARD_POINTER_THROTTLE_MS,
  WHITEBOARD_RATE_LIMIT_RETRY_MS,
  WhiteboardSyncService,
} from './whiteboard-sync.service';

const BOOKING_ID = 7;

/** ~45 KB'lık serbest çizim (2500 nokta). */
function freedraw(id: string, points: number, version = 1) {
  return element(id, version, { type: 'freedraw', points: Array.from({ length: points }, () => [123.456, 789.012]) });
}

describe('WhiteboardSyncService', () => {
  let hub: FakeHubConnection;
  let canvas: FakeCanvas;
  let service: WhiteboardSyncService;
  let warnings: WhiteboardWarning[];
  let auth: {
    getToken: jasmine.Spy;
    isExpiringSoon: jasmine.Spy;
    refreshToken: jasmine.Spy;
  };

  beforeEach(() => {
    hub = new FakeHubConnection();
    canvas = new FakeCanvas();
    auth = {
      getToken: jasmine.createSpy('getToken').and.returnValue('tkn'),
      isExpiringSoon: jasmine.createSpy('isExpiringSoon').and.returnValue(false),
      refreshToken: jasmine.createSpy('refreshToken').and.returnValue(of('fresh')),
    };
    const factory: WhiteboardHubConnectionFactory = (tokenFactory) => {
      hub.tokenFactory = tokenFactory;
      return hub;
    };
    TestBed.configureTestingModule({
      providers: [
        WhiteboardSyncService,
        { provide: AuthService, useValue: auth },
        { provide: WHITEBOARD_HUB_CONNECTION_FACTORY, useValue: factory },
      ],
    });
    service = TestBed.inject(WhiteboardSyncService);
    // Excalidraw gibi: sahne her uygulandığında onChange tetiklenir.
    canvas.onChange = () => service.notifyLocalChange();
    warnings = [];
    service.warnings$.subscribe((w) => warnings.push(w));
  });

  function startJoined(): void {
    service.start(BOOKING_ID, canvas);
    flushMicrotasks();
  }

  function localChange(): void {
    service.notifyLocalChange();
    tick(WHITEBOARD_FLUSH_THROTTLE_MS);
    flushMicrotasks();
  }

  function finish(): void {
    service.destroy();
    flush();
  }

  describe('join', () => {
    it('Start_JoinsBoardAndAppliesServerScene', fakeAsync(() => {
      hub.respond = () =>
        Promise.resolve(
          joinResult({
            elements: [{ id: 's1', type: 'rectangle', version: 2, versionNonce: 5 }],
            role: 'teacher',
            peerOnline: true,
            windowClosesAtUtc: '2026-09-30T10:00:00',
          })
        );

      startJoined();

      expect(hub.startCalls).toBe(1);
      expect(hub.calls('JoinBoard')).toEqual([{ method: 'JoinBoard', args: [BOOKING_ID] }]);
      expect(service.status()).toBe('connected');
      expect(service.role()).toBe('teacher');
      expect(service.peerRole()).toBe('student');
      expect(service.peerOnline()).toBeTrue();
      expect(service.windowClosesAt()?.toISOString()).toBe('2026-09-30T10:00:00.000Z');
      expect(canvas.elements.map((e) => e.id)).toEqual(['s1']);
      expect(canvas.restoreCalls).toBeGreaterThan(0);

      // Sunucudan gelen sahne geri gönderilmez.
      localChange();
      expect(hub.calls('SendElements').length).toBe(0);
      finish();
    }));

    it('JoinError_WindowNotOpen_ShowsErrorAndDoesNotAutoReconnect', fakeAsync(() => {
      hub.respond = () => Promise.reject(hubError('WindowNotOpen'));
      startJoined();

      expect(service.error()).toBe('WindowNotOpen');
      expect(service.status()).toBe('disconnected');
      expect(hub.stopCalls).toBe(1);

      hub.simulateClose();
      tick(60_000);
      expect(hub.startCalls).toBe(1);

      // Elle tekrar dene.
      hub.respond = () => Promise.resolve(joinResult());
      service.retry();
      flushMicrotasks();
      expect(hub.startCalls).toBe(2);
      expect(service.error()).toBeNull();
      expect(service.status()).toBe('connected');
      finish();
    }));

    it('JoinError_RateLimited_RetriesSilently', fakeAsync(() => {
      let attempts = 0;
      hub.respond = () => (++attempts === 1 ? Promise.reject(hubError('RateLimited')) : Promise.resolve(joinResult()));
      startJoined();

      expect(service.error()).toBeNull();
      expect(service.status()).toBe('connecting');
      tick(WHITEBOARD_RATE_LIMIT_RETRY_MS);
      flushMicrotasks();
      expect(hub.calls('JoinBoard').length).toBe(2);
      expect(service.status()).toBe('connected');
      expect(service.error()).toBeNull();
      finish();
    }));

    it('JoinError_WindowClosed_ClosesBoardReadOnly', fakeAsync(() => {
      hub.respond = () => Promise.reject(hubError('WindowClosed'));
      startJoined();
      expect(service.status()).toBe('closed');
      expect(service.readOnly()).toBeTrue();
      expect(service.closedReason()).toBe('WindowClosed');
      finish();
    }));

    // Issue #298: öğretmen askıda/onaysız — hata değil, salt okunur kapanış; otomatik yeniden deneme yok.
    it('JoinError_TeacherUnavailable_ClosesBoardReadOnlyWithoutRetry', fakeAsync(() => {
      hub.respond = () => Promise.reject(hubError('TeacherUnavailable'));
      startJoined();

      expect(service.status()).toBe('closed');
      expect(service.readOnly()).toBeTrue();
      expect(service.closedReason()).toBe('TeacherUnavailable');
      expect(service.error()).toBeNull();
      expect(hub.stopCalls).toBe(1);

      hub.simulateClose();
      tick(60_000);
      service.retry();
      flushMicrotasks();
      expect(hub.startCalls).toBe(1);
      expect(hub.calls('JoinBoard').length).toBe(1);
      finish();
    }));
  });

  describe('sending', () => {
    it('LocalChange_SendsOnlyChangedElements_Throttled', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('a'), element('b')];

      service.notifyLocalChange();
      service.notifyLocalChange();
      tick(WHITEBOARD_FLUSH_THROTTLE_MS - 1);
      expect(hub.calls('SendElements').length).toBe(0);
      service.notifyLocalChange();
      tick(1);
      flushMicrotasks();
      expect(hub.sentIds()).toEqual([['a', 'b']]);
      expect(hub.calls('SendElements')[0].args[0]).toBe(BOOKING_ID);

      // Değişiklik yok → gönderim yok.
      localChange();
      expect(hub.sentIds().length).toBe(1);

      // Yalnızca version'ı değişen gider.
      canvas.elements = [element('a', 2), element('b')];
      localChange();
      expect(hub.sentIds()).toEqual([['a', 'b'], ['a']]);

      // versionNonce farkı da değişikliktir.
      canvas.elements = [element('a', 2), element('b', 1, { versionNonce: 7 })];
      localChange();
      expect(hub.sentIds()[2]).toEqual(['b']);
      finish();
    }));

    it('ManyElements_SplitInto500ElementChunks', fakeAsync(() => {
      startJoined();
      canvas.elements = Array.from({ length: 1200 }, (_, i) => element(`e${i}`));
      localChange();
      expect(hub.sentIds().map((ids) => ids.length)).toEqual([500, 500, 200]);
      finish();
    }));

    it('LargeElements_SplitByByteBudget', fakeAsync(() => {
      startJoined();
      canvas.elements = [freedraw('f1', 2500), freedraw('f2', 2500), freedraw('f3', 2500)];
      localChange();
      expect(hub.sentIds()).toEqual([['f1', 'f2'], ['f3']]);
      finish();
    }));

    it('OversizedFreedraw_NotSentAndWarnsOnce', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('a'), freedraw('huge', 8000)];
      localChange();
      expect(hub.sentIds()).toEqual([['a']]);
      expect(warnings).toEqual(['elementTooLarge']);

      canvas.elements = [element('a'), freedraw('huge', 8100, 2)];
      localChange();
      expect(hub.sentIds()).toEqual([['a']]);
      expect(warnings).toEqual(['elementTooLarge']);
      finish();
    }));

    it('UnsupportedElements_RemovedLocallyAndNeverSent', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('a'), element('img', 1, { type: 'image', fileId: 'f1' }), element('web', 1, { type: 'embeddable' })];
      localChange();
      expect(canvas.elements.map((e) => e.id)).toEqual(['a']);
      expect(hub.sentIds()).toEqual([['a']]);
      expect(warnings).toContain('unsupportedContent');
      finish();
    }));

    it('OutgoingPayload_HasNoCustomDataAndOnlyHttpLinks', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('a', 1, { customData: { secret: 1 }, link: 'javascript:alert(1)' })];
      localChange();
      const payload = (hub.calls('SendElements')[0].args[1] as Record<string, unknown>[])[0];
      expect('customData' in payload).toBeFalse();
      expect(payload['link']).toBeNull();
      finish();
    }));

    it('Corrections_AppliedAndNotEchoed', fakeAsync(() => {
      hub.respond = (method) =>
        method === 'JoinBoard'
          ? Promise.resolve(joinResult())
          : Promise.resolve({
              serverVersion: 3,
              accepted: 0,
              corrections: [{ id: 'a', type: 'rectangle', version: 9, versionNonce: 1 }],
            });
      startJoined();
      canvas.elements = [element('a', 2)];
      localChange();

      expect(canvas.elements.find((e) => e.id === 'a')?.version).toBe(9);
      localChange();
      expect(hub.sentIds()).toEqual([['a']]);
      finish();
    }));

    it('SendRateLimited_RetriesSilentlyWithSameElements', fakeAsync(() => {
      let sends = 0;
      hub.respond = (method) =>
        method === 'JoinBoard'
          ? Promise.resolve(joinResult())
          : ++sends === 1
            ? Promise.reject(hubError('RateLimited'))
            : Promise.resolve({ serverVersion: 1, accepted: 1, corrections: [] });
      startJoined();
      canvas.elements = [element('a')];
      localChange();
      expect(hub.sentIds().length).toBe(1);

      tick(WHITEBOARD_RATE_LIMIT_RETRY_MS);
      flushMicrotasks();
      expect(hub.sentIds()).toEqual([['a'], ['a']]);
      expect(warnings).toEqual([]);
      finish();
    }));

    it('SendNotJoined_RejoinsBoard', fakeAsync(() => {
      hub.respond = (method) =>
        method === 'JoinBoard' ? Promise.resolve(joinResult()) : Promise.reject(hubError('NotJoined'));
      startJoined();
      canvas.elements = [element('a')];
      localChange();
      expect(hub.calls('JoinBoard').length).toBe(2);
      finish();
    }));

    // Issue #298: gönderimdeki yeniden doğrulama öğretmeni müsait bulmadı.
    it('SendTeacherUnavailable_ClosesBoardAndStopsSending', fakeAsync(() => {
      hub.respond = (method) =>
        method === 'JoinBoard' ? Promise.resolve(joinResult()) : Promise.reject(hubError('TeacherUnavailable'));
      startJoined();
      canvas.elements = [element('a')];
      localChange();

      expect(service.status()).toBe('closed');
      expect(service.closedReason()).toBe('TeacherUnavailable');
      expect(hub.calls('JoinBoard').length).toBe(1);

      canvas.elements = [element('b')];
      localChange();
      tick(5000);
      expect(hub.sentIds().length).toBe(1);
      finish();
    }));

    it('SendSceneLimit_WarnsAndDoesNotLoop', fakeAsync(() => {
      hub.respond = (method) =>
        method === 'JoinBoard' ? Promise.resolve(joinResult()) : Promise.reject(hubError('SceneSizeLimit'));
      startJoined();
      canvas.elements = [element('a')];
      localChange();
      localChange();
      tick(5000);
      expect(hub.sentIds().length).toBe(1);
      expect(warnings).toEqual(['sceneLimit']);
      finish();
    }));
  });

  describe('content rejection (bisect)', () => {
    /** SendElements: parçada `bad*` id'si varsa tüm parça `code` ile reddedilir. */
    function rejectBad(code: string): void {
      hub.respond = (method, args) => {
        if (method === 'JoinBoard') {
          return Promise.resolve(joinResult());
        }
        const ids = (args[1] as { id: string }[]).map((e) => e.id);
        return ids.some((id) => id.startsWith('bad'))
          ? Promise.reject(hubError(code))
          : Promise.resolve({ serverVersion: 1, accepted: ids.length, corrections: [] });
      };
    }

    it('RejectedChunk_BisectedUntilSingleElement_OthersDelivered', fakeAsync(() => {
      const warn = spyOn(console, 'warn');
      rejectBad('InvalidElement');
      startJoined();
      canvas.elements = [element('a'), element('b'), element('bad'), element('c')];
      localChange();

      expect(hub.sentIds()).toEqual([['a', 'b', 'bad', 'c'], ['a', 'b'], ['bad', 'c'], ['bad'], ['c']]);
      expect(warnings).toEqual(['rejected']);
      expect(warn).toHaveBeenCalledWith(jasmine.stringMatching(/bad \(rectangle\) rejected by server: InvalidElement/));

      // Reddedilen tek eleman damgalı kalır: döngü yok.
      localChange();
      expect(hub.sentIds().length).toBe(5);
      finish();
    }));

    it('SceneElementLimit_SingleElement_WarnsUserOncePerFlush', fakeAsync(() => {
      spyOn(console, 'warn');
      rejectBad('SceneElementLimit');
      startJoined();
      canvas.elements = [element('bad1'), element('ok'), element('bad2')];
      localChange();
      expect(warnings).toEqual(['sceneLimit']);
      expect(hub.sentIds()).toContain(['ok']);
      finish();
    }));

    it('Bisect_PreviouslyKnownStampsSurviveTransientFailure', fakeAsync(() => {
      spyOn(console, 'warn');
      startJoined();
      canvas.elements = [element('k', 1)];
      localChange();
      expect(hub.sentIds()).toEqual([['k']]);

      // k v2 + bad → içerik reddi → [k] RateLimited (geçici): k'nin ÖNCEKİ damgası (v1) geri gelmeli, silinmemeli.
      let calls = 0;
      hub.respond = (method, args) => {
        if (method === 'JoinBoard') {
          return Promise.resolve(joinResult());
        }
        calls++;
        const ids = (args[1] as { id: string }[]).map((e) => e.id);
        if (ids.includes('bad')) {
          return Promise.reject(hubError('InvalidElement'));
        }
        return calls === 2 ? Promise.reject(hubError('RateLimited')) : Promise.resolve({ serverVersion: 1, accepted: 1, corrections: [] });
      };
      canvas.elements = [element('k', 2), element('bad')];
      localChange();
      const known = (service as unknown as { known: Map<string, { version: number }> }).known;
      expect(known.get('k')?.version).toBe(1);

      tick(WHITEBOARD_RATE_LIMIT_RETRY_MS);
      flushMicrotasks();
      expect(known.get('k')?.version).toBe(2);
      finish();
    }));
  });

  describe('robustness', () => {
    it('RestoreOptions_BindingRepairOnlyForFullScene', fakeAsync(() => {
      hub.respond = (method) =>
        method === 'JoinBoard'
          ? Promise.resolve(joinResult({ elements: [{ id: 's', type: 'rectangle', version: 1, versionNonce: 1 }] }))
          : Promise.resolve({ serverVersion: 1, accepted: 1, corrections: [{ id: 'a', type: 'rectangle', version: 9, versionNonce: 1 }] });
      startJoined();
      hub.emit('ElementsUpdated', [{ id: 'p', type: 'rectangle', version: 1, versionNonce: 1 }]);
      canvas.elements = [...canvas.elements, element('a')];
      localChange();

      expect(canvas.restoreOptions).toEqual([{ repairBindings: true }, { repairBindings: false }, { repairBindings: false }]);
      finish();
    }));

    it('ApplyFailure_NoStampsAndFullResyncViaJoin', fakeAsync(() => {
      spyOn(console, 'warn');
      startJoined();
      canvas.failNextApply = true;
      hub.emit('ElementsUpdated', [{ id: 'r', type: 'rectangle', version: 3, versionNonce: 1 }]);

      const known = (service as unknown as { known: Map<string, unknown> }).known;
      expect(known.has('r')).toBeFalse();
      flushMicrotasks();
      expect(hub.calls('JoinBoard').length).toBe(2);

      // Aynı hata tekrarlanırsa (join sonrası) döngü yok: yeni bir güncelleme en fazla bir kez daha join tetikler.
      canvas.failNextApply = true;
      hub.emit('ElementsUpdated', [{ id: 'r2', type: 'rectangle', version: 1, versionNonce: 1 }]);
      flushMicrotasks();
      expect(hub.calls('JoinBoard').length).toBe(3);
      finish();
    }));

    it('RepeatedRepair_CircuitBreakerStopsResending', fakeAsync(() => {
      const warn = spyOn(console, 'warn');
      startJoined();
      canvas.repairOnRestore.add('fix');
      const known = (service as unknown as { known: Map<string, { version: number }> }).known;

      for (let v = 1; v <= 5; v++) {
        hub.emit('ElementsUpdated', [{ id: 'fix', type: 'rectangle', version: v * 10, versionNonce: 1 }]);
      }
      expect(known.has('fix')).toBeFalse();
      expect(warn).not.toHaveBeenCalled();

      hub.emit('ElementsUpdated', [{ id: 'fix', type: 'rectangle', version: 60, versionNonce: 1 }]);
      expect(known.get('fix')?.version).toBe(61);
      expect(warn).toHaveBeenCalledWith(jasmine.stringMatching(/fix repaired 6 times/));
      finish();
    }));

    it('CorrectionDiscardedWhileEditing_StampClearedSoLocalIsResent', fakeAsync(() => {
      let sends = 0;
      hub.respond = (method) =>
        method === 'JoinBoard'
          ? Promise.resolve(joinResult())
          : Promise.resolve({
              serverVersion: 1,
              accepted: 0,
              corrections: ++sends === 1 ? [{ id: 'a', type: 'text', version: 5, versionNonce: 1 }] : [],
            });
      startJoined();
      canvas.editingIds.add('a');
      canvas.elements = [element('a', 2, { type: 'text' })];
      localChange();

      expect(canvas.elements[0].version).toBe(2);
      const known = (service as unknown as { known: Map<string, unknown> }).known;
      expect(known.has('a')).toBeFalse();
      localChange();
      expect(hub.sentIds()).toEqual([['a'], ['a']]);
      finish();
    }));

    it('Retry_WaitsForStopBeforeStart', fakeAsync(() => {
      hub.respond = () => Promise.reject(hubError('WindowNotOpen'));
      startJoined();
      hub.lifecycle.length = 0;
      hub.respond = () => Promise.resolve(joinResult());
      hub.holdStop = true;

      void service.retry();
      flushMicrotasks();
      expect(hub.lifecycle).toEqual(['stop']);

      hub.releaseStop();
      flushMicrotasks();
      expect(hub.lifecycle).toEqual(['stop', 'stopped', 'start']);
      expect(service.status()).toBe('connected');
      finish();
    }));
  });

  describe('remote updates', () => {
    it('ElementsUpdated_AppliedAndNotEchoed', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('mine')];
      localChange();
      expect(hub.sentIds()).toEqual([['mine']]);

      hub.emit('ElementsUpdated', [{ id: 'r1', type: 'ellipse', version: 3, versionNonce: 5 }]);
      expect(canvas.elements.map((e) => e.id)).toEqual(['mine', 'r1']);

      // applyElements → onChange → kısılmış flush: uzak eleman geri gönderilmez.
      tick(WHITEBOARD_FLUSH_THROTTLE_MS);
      flushMicrotasks();
      expect(hub.sentIds()).toEqual([['mine']]);
      finish();
    }));

    it('ElementsUpdated_OlderThanLocal_LocalKeptAndSent', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('a', 5)];
      hub.emit('ElementsUpdated', [{ id: 'a', type: 'rectangle', version: 3, versionNonce: 1 }]);
      tick(WHITEBOARD_FLUSH_THROTTLE_MS);
      flushMicrotasks();

      expect(canvas.elements[0].version).toBe(5);
      const payload = (hub.calls('SendElements')[0].args[1] as { id: string; version: number }[])[0];
      expect(payload).toEqual(jasmine.objectContaining({ id: 'a', version: 5 }));
      finish();
    }));

    it('RemoteElements_SortedByFractionalIndexBeforeRestore', fakeAsync(() => {
      startJoined();
      hub.emit('ElementsUpdated', [
        { id: 'c', type: 'rectangle', version: 1, versionNonce: 1, index: 'a2' },
        { id: 'x', type: 'rectangle', version: 1, versionNonce: 1 },
        { id: 'a', type: 'rectangle', version: 1, versionNonce: 1, index: 'a0' },
        { id: 'b', type: 'rectangle', version: 1, versionNonce: 1, index: 'a1' },
      ]);
      expect(canvas.lastRestoreOrder).toEqual(['a', 'b', 'c', 'x']);
      finish();
    }));

    it('RemoteElement_RepairedByRestore_ResentSoPeersConverge', fakeAsync(() => {
      startJoined();
      canvas.repairOnRestore.add('fix');
      hub.emit('ElementsUpdated', [
        { id: 'ok', type: 'rectangle', version: 2, versionNonce: 1 },
        { id: 'fix', type: 'rectangle', version: 2, versionNonce: 1 },
      ]);
      tick(WHITEBOARD_FLUSH_THROTTLE_MS);
      flushMicrotasks();

      // Onarılan (version 3) eleman geri gönderilir; aynen giren eleman gönderilmez.
      expect(hub.sentIds()).toEqual([['fix']]);
      const payload = (hub.calls('SendElements')[0].args[1] as { version: number }[])[0];
      expect(payload.version).toBe(3);
      finish();
    }));

    it('ElementsUpdated_UntrustedContent_SanitizedAndFiltered', fakeAsync(() => {
      startJoined();
      hub.emit('ElementsUpdated', [
        { id: 'r', type: 'rectangle', version: 1, versionNonce: 1, customData: { evil: true }, link: 'javascript:x' },
        { id: 'img', type: 'image', version: 1, versionNonce: 1, fileId: 'f' },
        { nope: true },
      ]);
      expect(canvas.elements.map((e) => e.id)).toEqual(['r']);
      expect(Object.prototype.hasOwnProperty.call(canvas.elements[0], 'customData')).toBeFalse();
      expect(canvas.elements[0].link).toBeNull();

      hub.emit('ElementsUpdated', 'not-an-array');
      expect(canvas.elements.length).toBe(1);
      finish();
    }));
  });

  describe('client scene limit (issue #332)', () => {
    function rects(count: number, prefix = 'r'): Record<string, unknown>[] {
      return Array.from({ length: count }, (_, i) => ({ id: `${prefix}${i}`, type: 'rectangle', version: 1, versionNonce: 1 }));
    }

    it('JoinScene_OverElementLimit_NotAppliedWarnsAndStaysConnected', fakeAsync(() => {
      hub.respond = (method) =>
        method === 'JoinBoard'
          ? Promise.resolve(joinResult({ elements: rects(WHITEBOARD_MAX_SCENE_ELEMENTS + 1) }))
          : Promise.resolve({ serverVersion: 1, accepted: 0, corrections: [] });
      startJoined();

      expect(canvas.restoreCalls).toBe(0);
      expect(canvas.elements).toEqual([]);
      expect(warnings).toEqual(['remoteSceneLimit']);
      expect(service.status()).toBe('connected');
      finish();
    }));

    it('ElementsUpdated_OverElementLimit_NotAppliedWarnedOnceThenRearmedAfterSuccess', fakeAsync(() => {
      startJoined();
      const tooMany = rects(WHITEBOARD_MAX_SCENE_ELEMENTS + 1);

      hub.emit('ElementsUpdated', tooMany);
      hub.emit('ElementsUpdated', tooMany);
      expect(canvas.restoreCalls).toBe(0);
      expect(canvas.elements).toEqual([]);
      expect(warnings).toEqual(['remoteSceneLimit']);
      // Tam senkron istenmez (aynı sahne yine sınırı aşardı).
      expect(hub.calls('JoinBoard').length).toBe(1);

      hub.emit('ElementsUpdated', [{ id: 'ok', type: 'rectangle', version: 1, versionNonce: 1 }]);
      expect(canvas.elements.map((e) => e.id)).toEqual(['ok']);
      hub.emit('ElementsUpdated', tooMany);
      expect(warnings).toEqual(['remoteSceneLimit', 'remoteSceneLimit']);
      finish();
    }));

    it('ElementsUpdated_LargeByteSize_StillApplied_ByteLimitIsServerSide', fakeAsync(() => {
      startJoined();
      // İstemci bayt ölçmez (sunucu ham metniyle tutarsız ölçüm → griefing); 3 MB'lık tek eleman uygulanır.
      hub.emit('ElementsUpdated', [{ id: 'big', type: 'text', version: 1, versionNonce: 1, text: 'x'.repeat(3 * 1024 * 1024) }]);
      expect(canvas.elements.map((e) => e.id)).toEqual(['big']);
      expect(warnings).toEqual([]);
      finish();
    }));

    it('ElementsUpdated_LocalUnsentElementsNearLimit_RemoteUpdateStillApplied', fakeAsync(() => {
      startJoined();
      // Yerelde gönderilmemiş elemanlar + uzak güncelleme birlikte sınırı aşsa da uzak güncelleme reddedilmez.
      hub.respond = (method) =>
        method === 'SendElements' ? Promise.reject(new Error('transport')) : Promise.resolve(joinResult());
      canvas.elements = rects(WHITEBOARD_MAX_SCENE_ELEMENTS, 'l').map((r) => element(r['id'] as string));

      hub.emit('ElementsUpdated', rects(2, 'new'));

      expect(canvas.elements.length).toBe(WHITEBOARD_MAX_SCENE_ELEMENTS + 2);
      expect(warnings).toEqual([]);
      finish();
    }));
  });

  describe('tombstone cleanup (issue #332)', () => {
    it('AcknowledgedTombstone_PurgedLocallyAfterTtl_NotResent', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('keep'), element('gone')];
      localChange();
      canvas.elements = [element('keep'), element('gone', 2, { isDeleted: true })];
      localChange();
      expect(hub.sentIds()).toEqual([['keep', 'gone'], ['gone']]);

      // Onaylandıktan sonra TTL dolana kadar sahnede kalır (undo geçmişi için).
      localChange();
      expect(canvas.elements.map((e) => e.id)).toEqual(['keep', 'gone']);

      tick(WHITEBOARD_TOMBSTONE_TTL_MS);
      localChange();
      expect(canvas.elements.map((e) => e.id)).toEqual(['keep']);
      expect(hub.calls('SendElements').length).toBe(2);
      finish();
    }));

    it('RemoteTombstone_PurgedAfterTtl', fakeAsync(() => {
      startJoined();
      hub.emit('ElementsUpdated', [{ id: 'r', type: 'rectangle', version: 4, versionNonce: 1, isDeleted: true }]);
      tick(WHITEBOARD_FLUSH_THROTTLE_MS);
      flushMicrotasks();
      expect(canvas.elements.map((e) => e.id)).toEqual(['r']);

      tick(WHITEBOARD_TOMBSTONE_TTL_MS);
      localChange();
      expect(canvas.elements).toEqual([]);
      expect(hub.calls('SendElements').length).toBe(0);
      finish();
    }));

    it('UnsentTombstone_NeverPurged', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('a')];
      localChange();
      // Silme sunucuya ulaşamıyor (taşıma hatası): damga geri alınır, tombstone sahnede kalmalı.
      hub.respond = (method) =>
        method === 'SendElements' ? Promise.reject(new Error('transport')) : Promise.resolve(joinResult());
      canvas.elements = [element('a', 2, { isDeleted: true })];
      localChange();
      tick(WHITEBOARD_TOMBSTONE_TTL_MS * 2);
      localChange();

      expect(canvas.elements.map((e) => e.id)).toEqual(['a']);
      expect(canvas.elements[0].isDeleted).toBeTrue();
      finish();
    }));

    it('ServerRejectedSingleTombstone_StampedButNeverPurged', fakeAsync(() => {
      spyOn(console, 'warn');
      startJoined();
      // Tek eleman olarak içerik reddi: damgalı bırakılır (döngü yok) ama sunucuda yok → temizlenmemeli.
      hub.respond = (method) =>
        method === 'SendElements' ? Promise.reject(hubError('InvalidElement')) : Promise.resolve(joinResult());
      canvas.elements = [element('bad', 2, { isDeleted: true })];
      localChange();
      expect(hub.sentIds()).toEqual([['bad']]);

      tick(WHITEBOARD_TOMBSTONE_TTL_MS * 2);
      localChange();
      expect(canvas.elements.map((e) => e.id)).toEqual(['bad']);
      expect(hub.sentIds().length).toBe(1);
      finish();
    }));

    it('PurgedTombstone_StampDroppedAndReStampedWhenServerSendsItAgain', fakeAsync(() => {
      startJoined();
      const tombstone = { id: 'r', type: 'rectangle', version: 4, versionNonce: 1, isDeleted: true };
      hub.emit('ElementsUpdated', [tombstone]);
      tick(WHITEBOARD_FLUSH_THROTTLE_MS);
      flushMicrotasks();
      tick(WHITEBOARD_TOMBSTONE_TTL_MS);
      localChange();
      expect(canvas.elements).toEqual([]);

      // Aynı tombstone yeniden gelir (ör. sunucu yayını): görünmez olarak eklenir, geri gönderilmez, yine temizlenir.
      hub.emit('ElementsUpdated', [tombstone]);
      tick(WHITEBOARD_FLUSH_THROTTLE_MS);
      flushMicrotasks();
      expect(canvas.elements.map((e) => e.id)).toEqual(['r']);
      tick(WHITEBOARD_TOMBSTONE_TTL_MS);
      localChange();
      expect(canvas.elements).toEqual([]);
      expect(hub.calls('SendElements').length).toBe(0);
      finish();
    }));
  });

  describe('reconnect', () => {
    it('Reconnected_RejoinsMergesAndResendsOfflineChanges', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('a')];
      localChange();
      expect(hub.sentIds()).toEqual([['a']]);

      hub.simulateReconnecting();
      expect(service.status()).toBe('reconnecting');
      canvas.elements = [element('a'), element('offline')];
      localChange();
      expect(hub.sentIds().length).toBe(1);

      hub.respond = (method) =>
        method === 'JoinBoard'
          ? Promise.resolve(
              joinResult({
                elements: [
                  { id: 'a', type: 'rectangle', version: 1, versionNonce: 100 },
                  { id: 'peer', type: 'rectangle', version: 1, versionNonce: 1 },
                ],
              })
            )
          : Promise.resolve({ serverVersion: 2, accepted: 1, corrections: [] });
      hub.simulateReconnected();
      flushMicrotasks();

      expect(hub.calls('JoinBoard').length).toBe(2);
      expect(service.status()).toBe('connected');
      expect(canvas.elements.map((e) => e.id).sort()).toEqual(['a', 'offline', 'peer']);
      expect(hub.sentIds()[1]).toEqual(['offline']);
      finish();
    }));

    it('Reconnected_ServerSceneLost_ResendsWholeLocalScene', fakeAsync(() => {
      startJoined();
      canvas.elements = [element('a'), element('b')];
      localChange();
      hub.simulateReconnecting();
      hub.simulateReconnected();
      flushMicrotasks();
      expect(hub.sentIds()).toEqual([['a', 'b'], ['a', 'b']]);
      finish();
    }));

    it('Closed_ReconnectsWithBackoffAndFreshToken', fakeAsync(() => {
      startJoined();
      hub.simulateClose();
      expect(service.status()).toBe('disconnected');
      tick(1999);
      expect(hub.startCalls).toBe(1);
      tick(1);
      flushMicrotasks();
      expect(hub.startCalls).toBe(2);
      expect(hub.calls('JoinBoard').length).toBe(2);
      expect(service.status()).toBe('connected');
      finish();
    }));

    it('AccessTokenFactory_ExpiringToken_RefreshesFirst', fakeAsync(() => {
      const stored = localStorage.getItem('auth_token');
      startJoined();
      auth.isExpiringSoon.and.returnValue(true);
      let token = '';
      void hub.tokenFactory!().then((t) => (token = t));
      flushMicrotasks();
      expect(auth.refreshToken).toHaveBeenCalled();
      expect(token).toBe('fresh');
      if (stored === null) {
        localStorage.removeItem('auth_token');
      } else {
        localStorage.setItem('auth_token', stored);
      }
      finish();
    }));
  });

  describe('board closed', () => {
    it('BoardClosed_ReadOnlyNoReconnectNoSend', fakeAsync(() => {
      startJoined();
      hub.emit('BoardClosed', 'WindowClosed');

      expect(service.status()).toBe('closed');
      expect(service.readOnly()).toBeTrue();
      expect(service.closedReason()).toBe('WindowClosed');
      expect(hub.stopCalls).toBe(1);

      hub.simulateClose();
      tick(60_000);
      expect(hub.startCalls).toBe(1);

      canvas.elements = [element('late')];
      localChange();
      expect(hub.sentIds().length).toBe(0);

      service.retry();
      flushMicrotasks();
      expect(hub.startCalls).toBe(1);
      finish();
    }));

    it('BoardClosed_AllKnownReasons_Kept', fakeAsync(() => {
      for (const reason of ['BookingCancelled', 'TeacherNotApproved', 'TeacherUnavailable', 'AccessRevoked']) {
        const local = TestBed.runInInjectionContext(() => new WhiteboardSyncService());
        local.start(BOOKING_ID, new FakeCanvas());
        flushMicrotasks();
        hub.emit('BoardClosed', reason);
        expect(local.closedReason()).toBe(reason as never);
        local.destroy();
      }
      finish();
    }));

    it('BoardClosed_UnknownReason_GenericNull', fakeAsync(() => {
      startJoined();
      hub.emit('BoardClosed', '<script>');
      expect(service.status()).toBe('closed');
      expect(service.closedReason()).toBeNull();
      finish();
    }));
  });

  describe('pointer and presence', () => {
    it('LocalPointer_ThrottledToLatest', fakeAsync(() => {
      startJoined();
      for (let i = 0; i < 5; i++) {
        service.notifyLocalPointer({ x: i, y: i * 2, tool: 'pointer' });
      }
      expect(hub.sends.length).toBe(0);
      tick(WHITEBOARD_POINTER_THROTTLE_MS);
      expect(hub.sends).toEqual([{ method: 'SendPointer', args: [BOOKING_ID, { x: 4, y: 8, tool: 'pointer' }] }]);

      service.notifyLocalPointer({ x: Number.NaN, y: 1, tool: 'laser' });
      tick(WHITEBOARD_POINTER_THROTTLE_MS);
      expect(hub.sends.length).toBe(1);
      finish();
    }));

    it('PeerPointerAndPresence_ValidatedAndApplied', fakeAsync(() => {
      startJoined();
      hub.emit('PointerUpdated', 'student', { x: 10, y: 20, tool: 'laser' });
      expect(service.peerPointer()).toEqual({ x: 10, y: 20, tool: 'laser', role: 'student' });

      hub.emit('PointerUpdated', 'teacher', { x: 1, y: 1 });
      hub.emit('PointerUpdated', 'student', { x: 'x', y: 1 });
      expect(service.peerPointer()?.x).toBe(10);

      hub.emit('PeerPresenceChanged', 'student', true);
      expect(service.peerOnline()).toBeTrue();
      hub.emit('PeerPresenceChanged', 'student', false);
      expect(service.peerOnline()).toBeFalse();
      expect(service.peerPointer()).toBeNull();

      // Kendi rolünün presence'ı yok sayılır.
      hub.emit('PeerPresenceChanged', 'teacher', true);
      expect(service.peerOnline()).toBeFalse();
      finish();
    }));
  });

  it('Destroy_StopsConnection', fakeAsync(() => {
    startJoined();
    service.destroy();
    expect(hub.stopCalls).toBe(1);
    flush();
  }));
});

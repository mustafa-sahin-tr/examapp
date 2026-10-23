import type { Mock } from "vitest";
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { WhiteboardWarning } from '../models/whiteboard.model';
import { FakeCanvas, FakeHubConnection, element, hubError, joinResult, } from '../shared/testing/whiteboard-testing';
import { WHITEBOARD_MAX_SCENE_ELEMENTS, WHITEBOARD_TOMBSTONE_TTL_MS, } from '../shared/utils/whiteboard-sync.util';
import { AuthService } from './auth.service';
import { WHITEBOARD_HUB_CONNECTION_FACTORY, WhiteboardHubConnectionFactory } from './whiteboard-hub-connection';
import { WHITEBOARD_FLUSH_THROTTLE_MS, WHITEBOARD_POINTER_THROTTLE_MS, WHITEBOARD_RATE_LIMIT_RETRY_MS, WHITEBOARD_CAPACITY_MAX_AUTO_RETRIES, WHITEBOARD_CAPACITY_RETRY_MS, WhiteboardSyncService, } from './whiteboard-sync.service';

const BOOKING_ID = 7;

/** ~45 KB'lık serbest çizim (2500 nokta). */
function freedraw(id: string, points: number, version = 1) {
    return element(id, version, { type: 'freedraw', points: Array.from({ length: points }, () => [123.456, 789.012]) });
}

describe('WhiteboardSyncService', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let hub: FakeHubConnection;
    let canvas: FakeCanvas;
    let service: WhiteboardSyncService;
    let warnings: WhiteboardWarning[];
    let auth: {
        getToken: Mock;
        isExpiringSoon: Mock;
        refreshToken: Mock;
    };

    beforeEach(() => {
        hub = new FakeHubConnection();
        canvas = new FakeCanvas();
        auth = {
            getToken: vi.fn().mockName('getToken').mockReturnValue('tkn'),
            isExpiringSoon: vi.fn().mockName('isExpiringSoon').mockReturnValue(false),
            refreshToken: vi.fn().mockName('refreshToken').mockReturnValue(of('fresh')),
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

    async function startJoined(): Promise< void > {
        service.start(BOOKING_ID, canvas);
        await vi.advanceTimersByTimeAsync(0);
    }

    async function localChange(): Promise<void> {
        service.notifyLocalChange();
        await vi.advanceTimersByTimeAsync(WHITEBOARD_FLUSH_THROTTLE_MS);
        await vi.advanceTimersByTimeAsync(0);
    }

    async function finish(): Promise<void> {
        service.destroy();
        await vi.runAllTimersAsync();
    }

    describe('join', () => {
        it('Start_JoinsBoardAndAppliesServerScene', async () => {
            hub.respond = () => Promise.resolve(joinResult({
                elements: [{ id: 's1', type: 'rectangle', version: 2, versionNonce: 5 }],
                role: 'teacher',
                peerOnline: true,
                windowClosesAtUtc: '2026-09-30T10:00:00',
            }));

            await startJoined();

            expect(hub.startCalls).toBe(1);
            expect(hub.calls('JoinBoard')).toEqual([{ method: 'JoinBoard', args: [BOOKING_ID] }]);
            expect(service.status()).toBe('connected');
            expect(service.role()).toBe('teacher');
            expect(service.peerRole()).toBe('student');
            expect(service.peerOnline()).toBe(true);
            expect(service.windowClosesAt()?.toISOString()).toBe('2026-09-30T10:00:00.000Z');
            expect(canvas.elements.map((e) => e.id)).toEqual(['s1']);
            expect(canvas.restoreCalls).toBeGreaterThan(0);

            // Sunucudan gelen sahne geri gönderilmez.
            await localChange();
            expect(hub.calls('SendElements').length).toBe(0);
            await finish();
        });

        it('JoinError_WindowNotOpen_ShowsErrorAndDoesNotAutoReconnect', async () => {
            hub.respond = () => Promise.reject(hubError('WindowNotOpen'));
            await startJoined();

            expect(service.error()).toBe('WindowNotOpen');
            expect(service.status()).toBe('disconnected');
            expect(hub.stopCalls).toBe(1);

            hub.simulateClose();
            await vi.advanceTimersByTimeAsync(60000);
            expect(hub.startCalls).toBe(1);

            // Elle tekrar dene.
            hub.respond = () => Promise.resolve(joinResult());
            service.retry();
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.startCalls).toBe(2);
            expect(service.error()).toBeNull();
            expect(service.status()).toBe('connected');
            await finish();
        });

        it('JoinError_RateLimited_RetriesSilently', async () => {
            let attempts = 0;
            hub.respond = () => (++attempts === 1 ? Promise.reject(hubError('RateLimited')) : Promise.resolve(joinResult()));
            await startJoined();

            expect(service.error()).toBeNull();
            expect(service.status()).toBe('connecting');
            await vi.advanceTimersByTimeAsync(WHITEBOARD_RATE_LIMIT_RETRY_MS);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.calls('JoinBoard').length).toBe(2);
            expect(service.status()).toBe('connected');
            expect(service.error()).toBeNull();
            await finish();
        });

        it('JoinError_WindowClosed_ClosesBoardReadOnly', async () => {
            hub.respond = () => Promise.reject(hubError('WindowClosed'));
            await startJoined();
            expect(service.status()).toBe('closed');
            expect(service.readOnly()).toBe(true);
            expect(service.closedReason()).toBe('WindowClosed');
            await finish();
        });

        // Issue #311: kullanıcı başına bağlantı sınırı — sunucu ConnectionRejected gönderip bağlantıyı kapatır.
        it('ConnectionRejected_TooManyConnections_ShowsErrorWithoutAutoReconnect', async () => {
            hub.respond = (method) => {
                if (method === 'JoinBoard') {
                    hub.emit('ConnectionRejected', 'TooManyConnections');
                    return Promise.reject(new Error('Invocation canceled due to the underlying connection being closed.'));
                }
                return Promise.resolve(undefined);
            };
            await startJoined();

            expect(service.error()).toBe('TooManyConnections');
            expect(service.status()).toBe('disconnected');
            expect(service.readOnly()).toBe(false);

            hub.simulateClose();
            // onclose hatayı ezmez ve hemen yeniden bağlanmaz.
            expect(service.error()).toBe('TooManyConnections');
            await vi.advanceTimersByTimeAsync(WHITEBOARD_CAPACITY_RETRY_MS - 1);
            expect(hub.startCalls).toBe(1);

            // Arka plandaki gecikmeli deneme: başka sekme kapanmış → bağlanır.
            hub.respond = () => Promise.resolve(joinResult());
            await vi.advanceTimersByTimeAsync(1);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.startCalls).toBe(2);
            expect(service.error()).toBeNull();
            expect(service.status()).toBe('connected');
            await finish();
        });

        it('ConnectionRejected_AutoRetriesAreBounded_ThenManualRetryOnly', async () => {
            hub.respond = (method) => {
                if (method === 'JoinBoard') {
                    hub.emit('ConnectionRejected', 'TooManyConnections');
                    return Promise.reject(new Error('connection closed'));
                }
                return Promise.resolve(undefined);
            };
            await startJoined();
            for (let i = 0; i < WHITEBOARD_CAPACITY_MAX_AUTO_RETRIES; i++) {
                await vi.advanceTimersByTimeAsync(WHITEBOARD_CAPACITY_RETRY_MS);
                await vi.advanceTimersByTimeAsync(0);
            }
            expect(hub.startCalls).toBe(1 + WHITEBOARD_CAPACITY_MAX_AUTO_RETRIES);
            await vi.advanceTimersByTimeAsync(10 * WHITEBOARD_CAPACITY_RETRY_MS);
            expect(hub.startCalls).toBe(1 + WHITEBOARD_CAPACITY_MAX_AUTO_RETRIES);
            expect(service.error()).toBe('TooManyConnections');

            hub.respond = () => Promise.resolve(joinResult());
            service.retry();
            await vi.advanceTimersByTimeAsync(0);
            expect(service.status()).toBe('connected');
            await finish();
        });

        it('JoinError_TooManyOpenBoards_ShowsErrorAndRetriesLater', async () => {
            hub.respond = () => Promise.reject(hubError('TooManyOpenBoards'));
            await startJoined();

            expect(service.error()).toBe('TooManyOpenBoards');
            expect(service.status()).toBe('disconnected');
            hub.simulateClose();
            await vi.advanceTimersByTimeAsync(WHITEBOARD_CAPACITY_RETRY_MS - 1);
            expect(hub.startCalls).toBe(1);

            hub.respond = () => Promise.resolve(joinResult());
            await vi.advanceTimersByTimeAsync(1);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.startCalls).toBe(2);
            expect(service.status()).toBe('connected');
            await finish();
        });

        // Issue #298: öğretmen askıda/onaysız — hata değil, salt okunur kapanış; otomatik yeniden deneme yok.
        it('JoinError_TeacherUnavailable_ClosesBoardReadOnlyWithoutRetry', async () => {
            hub.respond = () => Promise.reject(hubError('TeacherUnavailable'));
            await startJoined();

            expect(service.status()).toBe('closed');
            expect(service.readOnly()).toBe(true);
            expect(service.closedReason()).toBe('TeacherUnavailable');
            expect(service.error()).toBeNull();
            expect(hub.stopCalls).toBe(1);

            hub.simulateClose();
            await vi.advanceTimersByTimeAsync(60000);
            service.retry();
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.startCalls).toBe(1);
            expect(hub.calls('JoinBoard').length).toBe(1);
            await finish();
        });
    });

    describe('sending', () => {
        it('LocalChange_SendsOnlyChangedElements_Throttled', async () => {
            await startJoined();
            canvas.elements = [element('a'), element('b')];

            service.notifyLocalChange();
            service.notifyLocalChange();
            await vi.advanceTimersByTimeAsync(WHITEBOARD_FLUSH_THROTTLE_MS - 1);
            expect(hub.calls('SendElements').length).toBe(0);
            service.notifyLocalChange();
            await vi.advanceTimersByTimeAsync(1);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.sentIds()).toEqual([['a', 'b']]);
            expect(hub.calls('SendElements')[0].args[0]).toBe(BOOKING_ID);

            // Değişiklik yok → gönderim yok.
            await localChange();
            expect(hub.sentIds().length).toBe(1);

            // Yalnızca version'ı değişen gider.
            canvas.elements = [element('a', 2), element('b')];
            await localChange();
            expect(hub.sentIds()).toEqual([['a', 'b'], ['a']]);

            // versionNonce farkı da değişikliktir.
            canvas.elements = [element('a', 2), element('b', 1, { versionNonce: 7 })];
            await localChange();
            expect(hub.sentIds()[2]).toEqual(['b']);
            await finish();
        });

        it('ManyElements_SplitInto500ElementChunks', async () => {
            await startJoined();
            canvas.elements = Array.from({ length: 1200 }, (_, i) => element(`e${i}`));
            await localChange();
            expect(hub.sentIds().map((ids) => ids.length)).toEqual([500, 500, 200]);
            await finish();
        });

        it('LargeElements_SplitByByteBudget', async () => {
            await startJoined();
            canvas.elements = [freedraw('f1', 2500), freedraw('f2', 2500), freedraw('f3', 2500)];
            await localChange();
            expect(hub.sentIds()).toEqual([['f1', 'f2'], ['f3']]);
            await finish();
        });

        it('OversizedFreedraw_NotSentAndWarnsOnce', async () => {
            await startJoined();
            canvas.elements = [element('a'), freedraw('huge', 8000)];
            await localChange();
            expect(hub.sentIds()).toEqual([['a']]);
            expect(warnings).toEqual(['elementTooLarge']);

            canvas.elements = [element('a'), freedraw('huge', 8100, 2)];
            await localChange();
            expect(hub.sentIds()).toEqual([['a']]);
            expect(warnings).toEqual(['elementTooLarge']);
            await finish();
        });

        it('UnsupportedElements_RemovedLocallyAndNeverSent', async () => {
            await startJoined();
            canvas.elements = [element('a'), element('img', 1, { type: 'image', fileId: 'f1' }), element('web', 1, { type: 'embeddable' })];
            await localChange();
            expect(canvas.elements.map((e) => e.id)).toEqual(['a']);
            expect(hub.sentIds()).toEqual([['a']]);
            expect(warnings).toContain('unsupportedContent');
            await finish();
        });

        it('OutgoingPayload_HasNoCustomDataAndOnlyHttpLinks', async () => {
            await startJoined();
            canvas.elements = [element('a', 1, { customData: { secret: 1 }, link: 'javascript:alert(1)' })];
            await localChange();
            const payload = (hub.calls('SendElements')[0].args[1] as Record<string, unknown>[])[0];
            expect('customData' in payload).toBe(false);
            expect(payload['link']).toBeNull();
            await finish();
        });

        it('Corrections_AppliedAndNotEchoed', async () => {
            hub.respond = (method) => method === 'JoinBoard'
                ? Promise.resolve(joinResult())
                : Promise.resolve({
                    serverVersion: 3,
                    accepted: 0,
                    corrections: [{ id: 'a', type: 'rectangle', version: 9, versionNonce: 1 }],
                });
            await startJoined();
            canvas.elements = [element('a', 2)];
            await localChange();

            expect(canvas.elements.find((e) => e.id === 'a')?.version).toBe(9);
            await localChange();
            expect(hub.sentIds()).toEqual([['a']]);
            await finish();
        });

        it('SendRateLimited_RetriesSilentlyWithSameElements', async () => {
            let sends = 0;
            hub.respond = (method) => method === 'JoinBoard'
                ? Promise.resolve(joinResult())
                : ++sends === 1
                    ? Promise.reject(hubError('RateLimited'))
                    : Promise.resolve({ serverVersion: 1, accepted: 1, corrections: [] });
            await startJoined();
            canvas.elements = [element('a')];
            await localChange();
            expect(hub.sentIds().length).toBe(1);

            await vi.advanceTimersByTimeAsync(WHITEBOARD_RATE_LIMIT_RETRY_MS);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.sentIds()).toEqual([['a'], ['a']]);
            expect(warnings).toEqual([]);
            await finish();
        });

        it('SendNotJoined_RejoinsBoard', async () => {
            hub.respond = (method) => method === 'JoinBoard' ? Promise.resolve(joinResult()) : Promise.reject(hubError('NotJoined'));
            await startJoined();
            canvas.elements = [element('a')];
            await localChange();
            expect(hub.calls('JoinBoard').length).toBe(2);
            await finish();
        });

        // Issue #298: gönderimdeki yeniden doğrulama öğretmeni müsait bulmadı.
        it('SendTeacherUnavailable_ClosesBoardAndStopsSending', async () => {
            hub.respond = (method) => method === 'JoinBoard' ? Promise.resolve(joinResult()) : Promise.reject(hubError('TeacherUnavailable'));
            await startJoined();
            canvas.elements = [element('a')];
            await localChange();

            expect(service.status()).toBe('closed');
            expect(service.closedReason()).toBe('TeacherUnavailable');
            expect(hub.calls('JoinBoard').length).toBe(1);

            canvas.elements = [element('b')];
            await localChange();
            await vi.advanceTimersByTimeAsync(5000);
            expect(hub.sentIds().length).toBe(1);
            await finish();
        });

        it('SendSceneLimit_WarnsAndDoesNotLoop', async () => {
            hub.respond = (method) => method === 'JoinBoard' ? Promise.resolve(joinResult()) : Promise.reject(hubError('SceneSizeLimit'));
            await startJoined();
            canvas.elements = [element('a')];
            await localChange();
            await localChange();
            await vi.advanceTimersByTimeAsync(5000);
            expect(hub.sentIds().length).toBe(1);
            expect(warnings).toEqual(['sceneLimit']);
            await finish();
        });
    });

    describe('content rejection (bisect)', () => {
        /** SendElements: parçada `bad*` id'si varsa tüm parça `code` ile reddedilir. */
        function rejectBad(code: string): void {
            hub.respond = (method, args) => {
                if (method === 'JoinBoard') {
                    return Promise.resolve(joinResult());
                }
                const ids = (args[1] as {
                    id: string;
                }[]).map((e) => e.id);
                return ids.some((id) => id.startsWith('bad'))
                    ? Promise.reject(hubError(code))
                    : Promise.resolve({ serverVersion: 1, accepted: ids.length, corrections: [] });
            };
        }

        it('RejectedChunk_BisectedUntilSingleElement_OthersDelivered', async () => {
            const warn = vi.spyOn(console, 'warn').mockReturnValue(undefined);
            rejectBad('InvalidElement');
            await startJoined();
            canvas.elements = [element('a'), element('b'), element('bad'), element('c')];
            await localChange();

            expect(hub.sentIds()).toEqual([['a', 'b', 'bad', 'c'], ['a', 'b'], ['bad', 'c'], ['bad'], ['c']]);
            expect(warnings).toEqual(['rejected']);
            expect(warn).toHaveBeenCalledWith(expect.stringMatching(/bad \(rectangle\) rejected by server: InvalidElement/));

            // Reddedilen tek eleman damgalı kalır: döngü yok.
            await localChange();
            expect(hub.sentIds().length).toBe(5);
            await finish();
        });

        it('SceneElementLimit_SingleElement_WarnsUserOncePerFlush', async () => {
            vi.spyOn(console, 'warn').mockReturnValue(undefined);
            rejectBad('SceneElementLimit');
            await startJoined();
            canvas.elements = [element('bad1'), element('ok'), element('bad2')];
            await localChange();
            expect(warnings).toEqual(['sceneLimit']);
            expect(hub.sentIds()).toContainEqual(['ok']);
            await finish();
        });

        it('Bisect_PreviouslyKnownStampsSurviveTransientFailure', async () => {
            vi.spyOn(console, 'warn').mockReturnValue(undefined);
            await startJoined();
            canvas.elements = [element('k', 1)];
            await localChange();
            expect(hub.sentIds()).toEqual([['k']]);

            // k v2 + bad → içerik reddi → [k] RateLimited (geçici): k'nin ÖNCEKİ damgası (v1) geri gelmeli, silinmemeli.
            let calls = 0;
            hub.respond = (method, args) => {
                if (method === 'JoinBoard') {
                    return Promise.resolve(joinResult());
                }
                calls++;
                const ids = (args[1] as {
                    id: string;
                }[]).map((e) => e.id);
                if (ids.includes('bad')) {
                    return Promise.reject(hubError('InvalidElement'));
                }
                return calls === 2 ? Promise.reject(hubError('RateLimited')) : Promise.resolve({ serverVersion: 1, accepted: 1, corrections: [] });
            };
            canvas.elements = [element('k', 2), element('bad')];
            await localChange();
            const known = (service as unknown as {
                known: Map<string, {
                    version: number;
                }>;
            }).known;
            expect(known.get('k')?.version).toBe(1);

            await vi.advanceTimersByTimeAsync(WHITEBOARD_RATE_LIMIT_RETRY_MS);
            await vi.advanceTimersByTimeAsync(0);
            expect(known.get('k')?.version).toBe(2);
            await finish();
        });
    });

    describe('robustness', () => {
        it('RestoreOptions_BindingRepairOnlyForFullScene', async () => {
            hub.respond = (method) => method === 'JoinBoard'
                ? Promise.resolve(joinResult({ elements: [{ id: 's', type: 'rectangle', version: 1, versionNonce: 1 }] }))
                : Promise.resolve({ serverVersion: 1, accepted: 1, corrections: [{ id: 'a', type: 'rectangle', version: 9, versionNonce: 1 }] });
            await startJoined();
            hub.emit('ElementsUpdated', [{ id: 'p', type: 'rectangle', version: 1, versionNonce: 1 }]);
            canvas.elements = [...canvas.elements, element('a')];
            await localChange();

            expect(canvas.restoreOptions).toEqual([{ repairBindings: true }, { repairBindings: false }, { repairBindings: false }]);
            await finish();
        });

        it('ApplyFailure_NoStampsAndFullResyncViaJoin', async () => {
            vi.spyOn(console, 'warn').mockReturnValue(undefined);
            await startJoined();
            canvas.failNextApply = true;
            hub.emit('ElementsUpdated', [{ id: 'r', type: 'rectangle', version: 3, versionNonce: 1 }]);

            const known = (service as unknown as {
                known: Map<string, unknown>;
            }).known;
            expect(known.has('r')).toBe(false);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.calls('JoinBoard').length).toBe(2);

            // Aynı hata tekrarlanırsa (join sonrası) döngü yok: yeni bir güncelleme en fazla bir kez daha join tetikler.
            canvas.failNextApply = true;
            hub.emit('ElementsUpdated', [{ id: 'r2', type: 'rectangle', version: 1, versionNonce: 1 }]);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.calls('JoinBoard').length).toBe(3);
            await finish();
        });

        it('RepeatedRepair_CircuitBreakerStopsResending', async () => {
            const warn = vi.spyOn(console, 'warn').mockReturnValue(undefined);
            await startJoined();
            canvas.repairOnRestore.add('fix');
            const known = (service as unknown as {
                known: Map<string, {
                    version: number;
                }>;
            }).known;

            for (let v = 1; v <= 5; v++) {
                hub.emit('ElementsUpdated', [{ id: 'fix', type: 'rectangle', version: v * 10, versionNonce: 1 }]);
            }
            expect(known.has('fix')).toBe(false);
            expect(warn).not.toHaveBeenCalled();

            hub.emit('ElementsUpdated', [{ id: 'fix', type: 'rectangle', version: 60, versionNonce: 1 }]);
            expect(known.get('fix')?.version).toBe(61);
            expect(warn).toHaveBeenCalledWith(expect.stringMatching(/fix repaired 6 times/));
            await finish();
        });

        it('CorrectionDiscardedWhileEditing_StampClearedSoLocalIsResent', async () => {
            let sends = 0;
            hub.respond = (method) => method === 'JoinBoard'
                ? Promise.resolve(joinResult())
                : Promise.resolve({
                    serverVersion: 1,
                    accepted: 0,
                    corrections: ++sends === 1 ? [{ id: 'a', type: 'text', version: 5, versionNonce: 1 }] : [],
                });
            await startJoined();
            canvas.editingIds.add('a');
            canvas.elements = [element('a', 2, { type: 'text' })];
            await localChange();

            expect(canvas.elements[0].version).toBe(2);
            const known = (service as unknown as {
                known: Map<string, unknown>;
            }).known;
            expect(known.has('a')).toBe(false);
            await localChange();
            expect(hub.sentIds()).toEqual([['a'], ['a']]);
            await finish();
        });

        it('Retry_WaitsForStopBeforeStart', async () => {
            hub.respond = () => Promise.reject(hubError('WindowNotOpen'));
            await startJoined();
            hub.lifecycle.length = 0;
            hub.respond = () => Promise.resolve(joinResult());
            hub.holdStop = true;

            void service.retry();
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.lifecycle).toEqual(['stop']);

            hub.releaseStop();
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.lifecycle).toEqual(['stop', 'stopped', 'start']);
            expect(service.status()).toBe('connected');
            await finish();
        });
    });

    describe('remote updates', () => {
        it('ElementsUpdated_AppliedAndNotEchoed', async () => {
            await startJoined();
            canvas.elements = [element('mine')];
            await localChange();
            expect(hub.sentIds()).toEqual([['mine']]);

            hub.emit('ElementsUpdated', [{ id: 'r1', type: 'ellipse', version: 3, versionNonce: 5 }]);
            expect(canvas.elements.map((e) => e.id)).toEqual(['mine', 'r1']);

            // applyElements → onChange → kısılmış flush: uzak eleman geri gönderilmez.
            await vi.advanceTimersByTimeAsync(WHITEBOARD_FLUSH_THROTTLE_MS);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.sentIds()).toEqual([['mine']]);
            await finish();
        });

        it('ElementsUpdated_OlderThanLocal_LocalKeptAndSent', async () => {
            await startJoined();
            canvas.elements = [element('a', 5)];
            hub.emit('ElementsUpdated', [{ id: 'a', type: 'rectangle', version: 3, versionNonce: 1 }]);
            await vi.advanceTimersByTimeAsync(WHITEBOARD_FLUSH_THROTTLE_MS);
            await vi.advanceTimersByTimeAsync(0);

            expect(canvas.elements[0].version).toBe(5);
            const payload = (hub.calls('SendElements')[0].args[1] as {
                id: string;
                version: number;
            }[])[0];
            expect(payload).toEqual(expect.objectContaining({ id: 'a', version: 5 }));
            await finish();
        });

        it('RemoteElements_SortedByFractionalIndexBeforeRestore', async () => {
            await startJoined();
            hub.emit('ElementsUpdated', [
                { id: 'c', type: 'rectangle', version: 1, versionNonce: 1, index: 'a2' },
                { id: 'x', type: 'rectangle', version: 1, versionNonce: 1 },
                { id: 'a', type: 'rectangle', version: 1, versionNonce: 1, index: 'a0' },
                { id: 'b', type: 'rectangle', version: 1, versionNonce: 1, index: 'a1' },
            ]);
            expect(canvas.lastRestoreOrder).toEqual(['a', 'b', 'c', 'x']);
            await finish();
        });

        it('RemoteElement_RepairedByRestore_ResentSoPeersConverge', async () => {
            await startJoined();
            canvas.repairOnRestore.add('fix');
            hub.emit('ElementsUpdated', [
                { id: 'ok', type: 'rectangle', version: 2, versionNonce: 1 },
                { id: 'fix', type: 'rectangle', version: 2, versionNonce: 1 },
            ]);
            await vi.advanceTimersByTimeAsync(WHITEBOARD_FLUSH_THROTTLE_MS);
            await vi.advanceTimersByTimeAsync(0);

            // Onarılan (version 3) eleman geri gönderilir; aynen giren eleman gönderilmez.
            expect(hub.sentIds()).toEqual([['fix']]);
            const payload = (hub.calls('SendElements')[0].args[1] as {
                version: number;
            }[])[0];
            expect(payload.version).toBe(3);
            await finish();
        });

        it('ElementsUpdated_UntrustedContent_SanitizedAndFiltered', async () => {
            await startJoined();
            hub.emit('ElementsUpdated', [
                { id: 'r', type: 'rectangle', version: 1, versionNonce: 1, customData: { evil: true }, link: 'javascript:x' },
                { id: 'img', type: 'image', version: 1, versionNonce: 1, fileId: 'f' },
                { nope: true },
            ]);
            expect(canvas.elements.map((e) => e.id)).toEqual(['r']);
            expect(Object.prototype.hasOwnProperty.call(canvas.elements[0], 'customData')).toBe(false);
            expect(canvas.elements[0].link).toBeNull();

            hub.emit('ElementsUpdated', 'not-an-array');
            expect(canvas.elements.length).toBe(1);
            await finish();
        });
    });

    describe('client scene limit (issue #332)', () => {
        function rects(count: number, prefix = 'r'): Record<string, unknown>[] {
            return Array.from({ length: count }, (_, i) => ({ id: `${prefix}${i}`, type: 'rectangle', version: 1, versionNonce: 1 }));
        }

        it('JoinScene_OverElementLimit_NotAppliedWarnsAndStaysConnected', async () => {
            hub.respond = (method) => method === 'JoinBoard'
                ? Promise.resolve(joinResult({ elements: rects(WHITEBOARD_MAX_SCENE_ELEMENTS + 1) }))
                : Promise.resolve({ serverVersion: 1, accepted: 0, corrections: [] });
            await startJoined();

            expect(canvas.restoreCalls).toBe(0);
            expect(canvas.elements).toEqual([]);
            expect(warnings).toEqual(['remoteSceneLimit']);
            expect(service.status()).toBe('connected');
            await finish();
        });

        it('ElementsUpdated_OverElementLimit_NotAppliedWarnedOnceThenRearmedAfterSuccess', async () => {
            await startJoined();
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
            await finish();
        });

        it('ElementsUpdated_LargeByteSize_StillApplied_ByteLimitIsServerSide', async () => {
            await startJoined();
            // İstemci bayt ölçmez (sunucu ham metniyle tutarsız ölçüm → griefing); 3 MB'lık tek eleman uygulanır.
            hub.emit('ElementsUpdated', [{ id: 'big', type: 'text', version: 1, versionNonce: 1, text: 'x'.repeat(3 * 1024 * 1024) }]);
            expect(canvas.elements.map((e) => e.id)).toEqual(['big']);
            expect(warnings).toEqual([]);
            await finish();
        });

        it('ElementsUpdated_LocalUnsentElementsNearLimit_RemoteUpdateStillApplied', async () => {
            await startJoined();
            // Yerelde gönderilmemiş elemanlar + uzak güncelleme birlikte sınırı aşsa da uzak güncelleme reddedilmez.
            hub.respond = (method) => method === 'SendElements' ? Promise.reject(new Error('transport')) : Promise.resolve(joinResult());
            canvas.elements = rects(WHITEBOARD_MAX_SCENE_ELEMENTS, 'l').map((r) => element(r['id'] as string));

            hub.emit('ElementsUpdated', rects(2, 'new'));

            expect(canvas.elements.length).toBe(WHITEBOARD_MAX_SCENE_ELEMENTS + 2);
            expect(warnings).toEqual([]);
            await finish();
        });
    });

    describe('tombstone cleanup (issue #332)', () => {
        it('AcknowledgedTombstone_PurgedLocallyAfterTtl_NotResent', async () => {
            await startJoined();
            canvas.elements = [element('keep'), element('gone')];
            await localChange();
            canvas.elements = [element('keep'), element('gone', 2, { isDeleted: true })];
            await localChange();
            expect(hub.sentIds()).toEqual([['keep', 'gone'], ['gone']]);

            // Onaylandıktan sonra TTL dolana kadar sahnede kalır (undo geçmişi için).
            await localChange();
            expect(canvas.elements.map((e) => e.id)).toEqual(['keep', 'gone']);

            await vi.advanceTimersByTimeAsync(WHITEBOARD_TOMBSTONE_TTL_MS);
            await localChange();
            expect(canvas.elements.map((e) => e.id)).toEqual(['keep']);
            expect(hub.calls('SendElements').length).toBe(2);
            await finish();
        });

        it('RemoteTombstone_PurgedAfterTtl', async () => {
            await startJoined();
            hub.emit('ElementsUpdated', [{ id: 'r', type: 'rectangle', version: 4, versionNonce: 1, isDeleted: true }]);
            await vi.advanceTimersByTimeAsync(WHITEBOARD_FLUSH_THROTTLE_MS);
            await vi.advanceTimersByTimeAsync(0);
            expect(canvas.elements.map((e) => e.id)).toEqual(['r']);

            await vi.advanceTimersByTimeAsync(WHITEBOARD_TOMBSTONE_TTL_MS);
            await localChange();
            expect(canvas.elements).toEqual([]);
            expect(hub.calls('SendElements').length).toBe(0);
            await finish();
        });

        it('UnsentTombstone_NeverPurged', async () => {
            await startJoined();
            canvas.elements = [element('a')];
            await localChange();
            // Silme sunucuya ulaşamıyor (taşıma hatası): damga geri alınır, tombstone sahnede kalmalı.
            hub.respond = (method) => method === 'SendElements' ? Promise.reject(new Error('transport')) : Promise.resolve(joinResult());
            canvas.elements = [element('a', 2, { isDeleted: true })];
            await localChange();
            await vi.advanceTimersByTimeAsync(WHITEBOARD_TOMBSTONE_TTL_MS * 2);
            await localChange();

            expect(canvas.elements.map((e) => e.id)).toEqual(['a']);
            expect(canvas.elements[0].isDeleted).toBe(true);
            await finish();
        });

        it('ServerRejectedSingleTombstone_StampedButNeverPurged', async () => {
            vi.spyOn(console, 'warn').mockReturnValue(undefined);
            await startJoined();
            // Tek eleman olarak içerik reddi: damgalı bırakılır (döngü yok) ama sunucuda yok → temizlenmemeli.
            hub.respond = (method) => method === 'SendElements' ? Promise.reject(hubError('InvalidElement')) : Promise.resolve(joinResult());
            canvas.elements = [element('bad', 2, { isDeleted: true })];
            await localChange();
            expect(hub.sentIds()).toEqual([['bad']]);

            await vi.advanceTimersByTimeAsync(WHITEBOARD_TOMBSTONE_TTL_MS * 2);
            await localChange();
            expect(canvas.elements.map((e) => e.id)).toEqual(['bad']);
            expect(hub.sentIds().length).toBe(1);
            await finish();
        });

        it('PurgedTombstone_StampDroppedAndReStampedWhenServerSendsItAgain', async () => {
            await startJoined();
            const tombstone = { id: 'r', type: 'rectangle', version: 4, versionNonce: 1, isDeleted: true };
            hub.emit('ElementsUpdated', [tombstone]);
            await vi.advanceTimersByTimeAsync(WHITEBOARD_FLUSH_THROTTLE_MS);
            await vi.advanceTimersByTimeAsync(0);
            await vi.advanceTimersByTimeAsync(WHITEBOARD_TOMBSTONE_TTL_MS);
            await localChange();
            expect(canvas.elements).toEqual([]);

            // Aynı tombstone yeniden gelir (ör. sunucu yayını): görünmez olarak eklenir, geri gönderilmez, yine temizlenir.
            hub.emit('ElementsUpdated', [tombstone]);
            await vi.advanceTimersByTimeAsync(WHITEBOARD_FLUSH_THROTTLE_MS);
            await vi.advanceTimersByTimeAsync(0);
            expect(canvas.elements.map((e) => e.id)).toEqual(['r']);
            await vi.advanceTimersByTimeAsync(WHITEBOARD_TOMBSTONE_TTL_MS);
            await localChange();
            expect(canvas.elements).toEqual([]);
            expect(hub.calls('SendElements').length).toBe(0);
            await finish();
        });
    });

    describe('reconnect', () => {
        it('Reconnected_RejoinsMergesAndResendsOfflineChanges', async () => {
            await startJoined();
            canvas.elements = [element('a')];
            await localChange();
            expect(hub.sentIds()).toEqual([['a']]);

            hub.simulateReconnecting();
            expect(service.status()).toBe('reconnecting');
            canvas.elements = [element('a'), element('offline')];
            await localChange();
            expect(hub.sentIds().length).toBe(1);

            hub.respond = (method) => method === 'JoinBoard'
                ? Promise.resolve(joinResult({
                    elements: [
                        { id: 'a', type: 'rectangle', version: 1, versionNonce: 100 },
                        { id: 'peer', type: 'rectangle', version: 1, versionNonce: 1 },
                    ],
                }))
                : Promise.resolve({ serverVersion: 2, accepted: 1, corrections: [] });
            hub.simulateReconnected();
            await vi.advanceTimersByTimeAsync(0);

            expect(hub.calls('JoinBoard').length).toBe(2);
            expect(service.status()).toBe('connected');
            expect(canvas.elements.map((e) => e.id).sort()).toEqual(['a', 'offline', 'peer']);
            expect(hub.sentIds()[1]).toEqual(['offline']);
            await finish();
        });

        it('Reconnected_ServerSceneLost_ResendsWholeLocalScene', async () => {
            await startJoined();
            canvas.elements = [element('a'), element('b')];
            await localChange();
            hub.simulateReconnecting();
            hub.simulateReconnected();
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.sentIds()).toEqual([['a', 'b'], ['a', 'b']]);
            await finish();
        });

        it('Closed_ReconnectsWithBackoffAndFreshToken', async () => {
            await startJoined();
            hub.simulateClose();
            expect(service.status()).toBe('disconnected');
            await vi.advanceTimersByTimeAsync(1999);
            expect(hub.startCalls).toBe(1);
            await vi.advanceTimersByTimeAsync(1);
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.startCalls).toBe(2);
            expect(hub.calls('JoinBoard').length).toBe(2);
            expect(service.status()).toBe('connected');
            await finish();
        });

        it('AccessTokenFactory_ExpiringToken_RefreshesFirst', async () => {
            const stored = localStorage.getItem('auth_token');
            await startJoined();
            auth.isExpiringSoon.mockReturnValue(true);
            let token = '';
            void hub.tokenFactory!().then((t) => (token = t));
            await vi.advanceTimersByTimeAsync(0);
            expect(auth.refreshToken).toHaveBeenCalled();
            expect(token).toBe('fresh');
            if (stored === null) {
                localStorage.removeItem('auth_token');
            }
            else {
                localStorage.setItem('auth_token', stored);
            }
            await finish();
        });
    });

    describe('board closed', () => {
        it('BoardClosed_ReadOnlyNoReconnectNoSend', async () => {
            await startJoined();
            hub.emit('BoardClosed', 'WindowClosed');

            expect(service.status()).toBe('closed');
            expect(service.readOnly()).toBe(true);
            expect(service.closedReason()).toBe('WindowClosed');
            expect(hub.stopCalls).toBe(1);

            hub.simulateClose();
            await vi.advanceTimersByTimeAsync(60000);
            expect(hub.startCalls).toBe(1);

            canvas.elements = [element('late')];
            await localChange();
            expect(hub.sentIds().length).toBe(0);

            service.retry();
            await vi.advanceTimersByTimeAsync(0);
            expect(hub.startCalls).toBe(1);
            await finish();
        });

        it('BoardClosed_AllKnownReasons_Kept', async () => {
            for (const reason of ['BookingCancelled', 'TeacherNotApproved', 'TeacherUnavailable', 'AccessRevoked']) {
                const local = TestBed.runInInjectionContext(() => new WhiteboardSyncService());
                local.start(BOOKING_ID, new FakeCanvas());
                await vi.advanceTimersByTimeAsync(0);
                hub.emit('BoardClosed', reason);
                expect(local.closedReason()).toBe(reason as never);
                local.destroy();
            }
            await finish();
        });

        it('BoardClosed_UnknownReason_GenericNull', async () => {
            await startJoined();
            hub.emit('BoardClosed', '<script>');
            expect(service.status()).toBe('closed');
            expect(service.closedReason()).toBeNull();
            await finish();
        });
    });

    describe('pointer and presence', () => {
        it('LocalPointer_ThrottledToLatest', async () => {
            await startJoined();
            for (let i = 0; i < 5; i++) {
                service.notifyLocalPointer({ x: i, y: i * 2, tool: 'pointer' });
            }
            expect(hub.sends.length).toBe(0);
            await vi.advanceTimersByTimeAsync(WHITEBOARD_POINTER_THROTTLE_MS);
            expect(hub.sends).toEqual([{ method: 'SendPointer', args: [BOOKING_ID, { x: 4, y: 8, tool: 'pointer' }] }]);

            service.notifyLocalPointer({ x: Number.NaN, y: 1, tool: 'laser' });
            await vi.advanceTimersByTimeAsync(WHITEBOARD_POINTER_THROTTLE_MS);
            expect(hub.sends.length).toBe(1);
            await finish();
        });

        it('PeerPointerAndPresence_ValidatedAndApplied', async () => {
            await startJoined();
            hub.emit('PointerUpdated', 'student', { x: 10, y: 20, tool: 'laser' });
            expect(service.peerPointer()).toEqual({ x: 10, y: 20, tool: 'laser', role: 'student' });

            hub.emit('PointerUpdated', 'teacher', { x: 1, y: 1 });
            hub.emit('PointerUpdated', 'student', { x: 'x', y: 1 });
            expect(service.peerPointer()?.x).toBe(10);

            hub.emit('PeerPresenceChanged', 'student', true);
            expect(service.peerOnline()).toBe(true);
            hub.emit('PeerPresenceChanged', 'student', false);
            expect(service.peerOnline()).toBe(false);
            expect(service.peerPointer()).toBeNull();

            // Kendi rolünün presence'ı yok sayılır.
            hub.emit('PeerPresenceChanged', 'teacher', true);
            expect(service.peerOnline()).toBe(false);
            await finish();
        });
    });

    it('Destroy_StopsConnection', async () => {
        await startJoined();
        service.destroy();
        expect(hub.stopCalls).toBe(1);
        await vi.runAllTimersAsync();
    });
});

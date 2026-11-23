import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MockedObject } from 'vitest';
import { of } from 'rxjs';
import { AssistantError, AssistantStreamEvent } from '../models/assistant';
import { AssistantService, parseSseBlock } from './assistant.service';
import { AuthService } from './auth.service';

function sseResponse(chunks: string[], status = 200): Response {
    const encoder = new TextEncoder();
    const body = new ReadableStream<Uint8Array>({
        start(controller) {
            for (const c of chunks) controller.enqueue(encoder.encode(c));
            controller.close();
        },
    });
    return new Response(body, { status, headers: { 'Content-Type': 'text/event-stream' } });
}

async function collect(gen: AsyncGenerator<AssistantStreamEvent>): Promise<AssistantStreamEvent[]> {
    const out: AssistantStreamEvent[] = [];
    for await (const e of gen) out.push(e);
    return out;
}

describe('AssistantService (#507)', () => {
    let service: AssistantService;
    let auth: MockedObject<AuthService>;
    let fetchSpy: ReturnType<typeof vi.spyOn>;
    const request = { message: 'Merhaba', context: { route: '/dashboard', locale: 'tr' } };

    beforeEach(() => {
        auth = {
            getToken: vi.fn().mockReturnValue('token-1'),
            isExpiringSoon: vi.fn().mockReturnValue(false),
            refreshToken: vi.fn().mockReturnValue(of('token-2')),
        } as unknown as MockedObject<AuthService>;
        TestBed.configureTestingModule({
            providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
        });
        service = TestBed.inject(AssistantService);
        fetchSpy = vi.spyOn(window, 'fetch');
    });

    afterEach(() => {
        fetchSpy.mockRestore();
        localStorage.removeItem('auth_token');
    });

    it('parseSseBlock_ReadsEventAndJson_IgnoresKeepAlive', () => {
        expect(parseSseBlock('event: text\ndata: {"delta":"Mer"}')).toEqual({ event: 'text', data: { delta: 'Mer' } });
        expect(parseSseBlock(': keep-alive')).toBeNull();
        expect(parseSseBlock('event: text\ndata: not-json')).toBeNull();
    });

    it('chat_SplitsEventsAcrossChunks_AndSendsBearer', async () => {
        fetchSpy.mockResolvedValue(
            sseResponse(['event: text\ndata: {"delta":"Mer', 'haba"}\n\n: keep-alive\n\n', 'event: done\ndata: {"conversationId":"c1","messageId":7}\n\n'])
        );

        const events = await collect(service.chat(request));

        expect(events).toEqual([
            { event: 'text', data: { delta: 'Merhaba' } },
            { event: 'done', data: { conversationId: 'c1', messageId: 7 } },
        ]);
        const init = fetchSpy.mock.calls[0][1] as RequestInit;
        expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer token-1');
        expect(JSON.parse(init.body as string)).toEqual(request);
    });

    it('chat_ExpiringToken_RefreshesBeforeRequest', async () => {
        auth.isExpiringSoon.mockReturnValue(true);
        fetchSpy.mockResolvedValue(sseResponse([]));

        await collect(service.chat(request));

        const init = fetchSpy.mock.calls[0][1] as RequestInit;
        expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer token-2');
        expect(localStorage.getItem('auth_token')).toBe('token-2');
    });

    it('chat_401_RefreshesOnceAndRetries', async () => {
        fetchSpy.mockResolvedValueOnce(new Response(null, { status: 401 })).mockResolvedValueOnce(sseResponse([]));

        await collect(service.chat(request));

        expect(fetchSpy).toHaveBeenCalledTimes(2);
        expect(auth.refreshToken).toHaveBeenCalledTimes(1);
    });

    it('chat_BudgetExceeded_ThrowsAssistantErrorWithCodeAndLimit', async () => {
        fetchSpy.mockResolvedValue(
            new Response(JSON.stringify({ code: 'AssistantBudgetExceeded', limit: 'MessagesPerHour' }), { status: 429 })
        );

        const error = await collect(service.chat(request)).catch((e: unknown) => e);

        expect(error).toBeInstanceOf(AssistantError);
        expect((error as AssistantError).code).toBe('AssistantBudgetExceeded');
        expect((error as AssistantError).status).toBe(429);
        expect((error as AssistantError).limit).toBe('MessagesPerHour');
    });

    it('chat_GatewayHtmlError_FallsBackToUnavailable', async () => {
        fetchSpy.mockResolvedValue(new Response('<html>bad gateway</html>', { status: 502 }));

        const error = (await collect(service.chat(request)).catch((e: unknown) => e)) as AssistantError;

        expect(error.code).toBe('AssistantUnavailable');
    });
});

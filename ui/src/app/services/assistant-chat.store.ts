import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import {
  AssistantCard,
  AssistantContext,
  AssistantError,
  AssistantUsage,
  ConfirmState,
  FeedbackRating,
  FeedbackReason,
  PanelMessage,
  SuggestionChip,
  UiAction,
} from '../models/assistant';
import { AssistantService } from './assistant.service';

/** Sunucunun izin verdiği en uzun mesaj (`ChatTurnRunner.MaxMessageLength`). */
export const ASSISTANT_MAX_MESSAGE_LENGTH = 2000;

/**
 * Asistan sohbetinin durumu (#507). Kökte tutulur: panel kapanıp açılınca konuşma kaybolmaz; sayfa yenilenince yeni
 * konuşma başlar (geçmiş listesi ayrı iş). Ekranlar arası gezinme `navigated` ile panele bildirilir (mobilde kapanır).
 */
/** #515 ui_highlight: the element may render after a lazy route loads. */
const HIGHLIGHT_WAIT_MS = 100;
const HIGHLIGHT_WAIT_STEPS = 30;
const HIGHLIGHT_MS = 3000;

@Injectable({ providedIn: 'root' })
export class AssistantChatStore {
  private readonly api = inject(AssistantService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  readonly messages = signal<PanelMessage[]>([]);
  readonly suggestions = signal<SuggestionChip[]>([]);
  readonly sending = signal(false);
  readonly usage = signal<AssistantUsage | null>(null);
  /** Bir `navigate` eylemi uygulandığında artar; panel mobilde kendini kapatır. */
  readonly navigated = signal(0);

  private conversationId: string | null = null;
  private nextId = 1;
  private abort: AbortController | null = null;

  /** Ekran bağlamı: rota, açık test, dil. */
  contextFor(url: string): AssistantContext {
    const path = url.split(/[?#]/)[0];
    const context: AssistantContext = { route: path, locale: this.transloco.getActiveLang() };
    const worksheet = /^\/test\/(\d+)/.exec(path);
    if (worksheet) context.worksheetId = Number(worksheet[1]);
    return context;
  }

  loadUsage(): void {
    this.api.usage().subscribe({ next: (u) => this.usage.set(u), error: () => this.usage.set(null) });
  }

  newConversation(): void {
    this.abort?.abort();
    this.abort = null;
    this.conversationId = null;
    this.messages.set([]);
    this.suggestions.set([]);
    this.sending.set(false);
  }

  /** Panel kapanırken / çıkışta: akan tur kesilir, konuşma kalır. */
  cancel(): void {
    this.abort?.abort();
  }

  async send(text: string): Promise<void> {
    const message = text.trim();
    if (!message || message.length > ASSISTANT_MAX_MESSAGE_LENGTH || this.sending()) return;
    this.sending.set(true);
    this.suggestions.set([]);
    this.messages.update((list) => [...list, this.newMessage('user', message)]);
    await this.runTurn(message, true);
  }

  private async runTurn(message: string, mayRetry: boolean): Promise<void> {
    const assistant = this.newMessage('assistant', '');
    assistant.streaming = true;
    this.messages.update((list) => [...list, assistant]);
    const abort = new AbortController();
    this.abort = abort;
    let retry = false;

    try {
      const request = { conversationId: this.conversationId, message, context: this.contextFor(this.router.url) };
      for await (const e of this.api.chat(request, abort.signal)) {
        switch (e.event) {
          case 'text':
            this.patch(assistant.id, (m) => ({ text: m.text + e.data.delta }));
            break;
          case 'tool_start':
            this.patch(assistant.id, () => ({ activeTool: e.data.tool }));
            break;
          case 'tool_end':
            this.patch(assistant.id, () => ({ activeTool: null }));
            break;
          case 'card':
          case 'confirm_required':
            this.addCard(assistant.id, e.data);
            break;
          case 'ui_action':
            this.applyUiAction(e.data);
            break;
          case 'done':
            this.conversationId = e.data.conversationId;
            this.patch(assistant.id, () => ({ messageId: e.data.messageId ?? null }));
            break;
          case 'error':
            if (e.data.code === 'TokenExpired' && mayRetry) {
              retry = true;
            } else {
              this.patch(assistant.id, () => ({ errorCode: e.data.code }));
            }
            break;
        }
      }
    } catch (err) {
      if (abort.signal.aborted) {
        // Kullanıcı kesti / yeni konuşma: hata gösterilmez.
      } else {
        const code = err instanceof AssistantError ? err.code : 'AssistantUnavailable';
        if (code === 'ConversationNotFound') this.conversationId = null;
        this.patch(assistant.id, () => ({ errorCode: code }));
      }
    } finally {
      if (this.abort === abort) this.abort = null;
    }

    if (retry) {
      // Tur sunucuda saklanmadı: asistan balonu atılır, token yenilenip aynı mesaj bir kez yeniden gönderilir.
      this.messages.update((list) => list.filter((m) => m.id !== assistant.id));
      try {
        await this.api.refreshAfterExpiry();
        await this.runTurn(message, false);
        return;
      } catch {
        this.messages.update((list) => [...list, { ...assistant, streaming: false, errorCode: 'Unauthenticated' }]);
      }
    }

    this.patch(assistant.id, () => ({ streaming: false, activeTool: null }));
    this.sending.set(false);
    this.loadUsage();
  }

  confirm(messageId: number, card: AssistantCard): void {
    if (card.kind !== 'confirm' || this.confirmState(messageId, card.pendingActionId) !== 'pending') return;
    const id = card.pendingActionId;
    this.setConfirmState(messageId, id, 'busy');
    this.api.confirmAction(id).subscribe({
      next: (result) => {
        this.setConfirmState(messageId, id, result.ok ? 'confirmed' : 'failed');
        for (const action of result.uiActions ?? []) this.applyUiAction(action);
      },
      error: (err: unknown) => this.setConfirmState(messageId, id, decisionErrorState(err)),
    });
  }

  reject(messageId: number, card: AssistantCard): void {
    if (card.kind !== 'confirm' || this.confirmState(messageId, card.pendingActionId) !== 'pending') return;
    const id = card.pendingActionId;
    this.setConfirmState(messageId, id, 'busy');
    this.api.rejectAction(id).subscribe({
      next: () => this.setConfirmState(messageId, id, 'rejected'),
      error: (err: unknown) => this.setConfirmState(messageId, id, decisionErrorState(err)),
    });
  }

  rate(localId: number, rating: FeedbackRating, reason: FeedbackReason | null = null): void {
    const message = this.messages().find((m) => m.id === localId);
    if (!message?.messageId) return;
    const previous = message.feedback;
    this.patch(localId, () => ({ feedback: rating }));
    this.api.feedback(message.messageId, rating, reason).subscribe({
      error: () => this.patch(localId, () => ({ feedback: previous })),
    });
  }

  private applyUiAction(action: UiAction): void {
    if (action.type === 'suggestions') {
      this.suggestions.set(action.items ?? []);
    } else if (action.type === 'navigate' && typeof action.route === 'string' && action.route.startsWith('/')) {
      // Rotayı sunucu kurar (izinli ekranlar listesi); yalnız uygulama içi yol kabul edilir.
      void this.router.navigateByUrl(action.route);
      this.navigated.update((n) => n + 1);
    } else if (action.type === 'highlight' && typeof action.route === 'string' && action.route.startsWith('/')
      && typeof action.key === 'string' && /^[a-z]+\.[a-zA-Z]+$/.test(action.key)) {
      void this.highlight(action.key, action.route);
    }
  }

  /** #515: opens the screen when needed, then scrolls to the element and outlines it for a few seconds. */
  private async highlight(key: string, route: string): Promise<void> {
    if (this.router.url.split('?')[0] !== route) {
      if (!(await this.router.navigateByUrl(route))) return;
      this.navigated.update((n) => n + 1);
    }
    for (let i = 0; i < HIGHLIGHT_WAIT_STEPS; i++) {
      const el = document.querySelector<HTMLElement>(`[data-assistant-key="${key}"]`);
      if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        el.classList.add('assistant-highlight');
        setTimeout(() => el.classList.remove('assistant-highlight'), HIGHLIGHT_MS);
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, HIGHLIGHT_WAIT_MS));
    }
  }

  private addCard(id: number, card: AssistantCard): void {
    this.patch(id, (m) => ({
      cards: [...m.cards, card],
      confirmStates: card.kind === 'confirm' ? { ...m.confirmStates, [card.pendingActionId]: 'pending' } : m.confirmStates,
    }));
  }

  private confirmState(messageId: number, actionId: string): ConfirmState | undefined {
    return this.messages().find((m) => m.id === messageId)?.confirmStates[actionId];
  }

  private setConfirmState(messageId: number, actionId: string, state: ConfirmState): void {
    this.patch(messageId, (m) => ({ confirmStates: { ...m.confirmStates, [actionId]: state } }));
  }

  private patch(id: number, change: (m: PanelMessage) => Partial<PanelMessage>): void {
    this.messages.update((list) => list.map((m) => (m.id === id ? { ...m, ...change(m) } : m)));
  }

  private newMessage(role: PanelMessage['role'], text: string): PanelMessage {
    return {
      id: this.nextId++,
      role,
      text,
      cards: [],
      confirmStates: {},
      streaming: false,
      messageId: null,
      errorCode: null,
      feedback: null,
      activeTool: null,
    };
  }
}

function decisionErrorState(err: unknown): ConfirmState {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 410 || err.status === 404) return 'expired';
    if (err.status === 409) return 'decided';
  }
  return 'failed';
}

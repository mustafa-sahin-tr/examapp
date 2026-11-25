/** Uygulama içi asistan (#507, epic #495). Sunucu sözleşmesi: `docs/assistant.md`. */

export const ASSISTANT_SCOPE = 'assistant';

/** `GET /api/assistant/status`: kapalıysa 200 + `enabled: false` (`code` nedeni). */
export interface AssistantStatus {
  enabled: boolean;
  code: string | null;
  roles: string[];
  limits: { messagesPerHour: number; explanationsPerDay: number } | null;
}

/** `GET /api/assistant/usage/me`. */
export interface AssistantUsage {
  messagesThisHour: number;
  messagesPerHour: number;
  spentTodayUsd: number;
  dailyBudgetUsd: number;
  totalBudgetReached: boolean;
}

/** Sohbet isteğinin ekran bağlamı (`POST /api/assistant/chat` `context`). */
export interface AssistantContext {
  route: string;
  worksheetId?: number;
  instanceId?: number;
  locale: string;
}

export interface KpiItem {
  key: string;
  value: number | null;
}

/** topicTable satırı: konu satırı (`topicName`, `total`) ya da ders özeti (`answered`). */
export interface TopicRow {
  subjectName?: string | null;
  topicName?: string | null;
  subTopicName?: string | null;
  total?: number | null;
  answered?: number | null;
  correct?: number | null;
  accuracyPct?: number | null;
}

export interface ConfirmPreview {
  action?: string;
  targetCount?: number | null;
  mistakes?: boolean | null;
  dueReview?: boolean | null;
  currentStatus?: string | null;
  scopeApplies?: boolean | null;
  [key: string]: unknown;
}

/** Issue #526: zorlandığım sorular kartındaki bir soru (görsel / metin yalnız panelde, modele gitmez). */
export interface QuestionCardItem {
  questionId: number;
  subjectName?: string | null;
  topicName?: string | null;
  missCount?: number | null;
  /** #526 global mode: share of students who answered it correctly. */
  correctPercent?: number | null;
  imageUrl?: string | null;
  text?: string | null;
}

export type AssistantCard =
  | { kind: 'kpi'; items: KpiItem[] }
  /** `solvable: false` (#536, parent): no "solve these" button. */
  | { kind: 'questions'; items: QuestionCardItem[]; solvable?: boolean }
  | { kind: 'topicTable'; rows: TopicRow[] }
  | { kind: 'confirm'; pendingActionId: string; tool: string; preview: ConfirmPreview | null; expiresAt: string };

export type ConfirmState = 'pending' | 'busy' | 'confirmed' | 'rejected' | 'expired' | 'decided' | 'failed';

export interface SuggestionChip {
  label: string;
  prompt: string;
}

export interface NavigateAction {
  type: 'navigate';
  screen: string;
  route: string;
}

export interface SuggestionsAction {
  type: 'suggestions';
  items: SuggestionChip[];
}

export type UiAction = NavigateAction | SuggestionsAction;

/** SSE olayları (`event:` adı + `data:` JSON). */
export type AssistantStreamEvent =
  | { event: 'text'; data: { delta: string } }
  | { event: 'tool_start'; data: { tool: string } }
  | { event: 'tool_end'; data: { tool: string; ok: boolean; errorCode: string | null; ms: number } }
  | { event: 'card'; data: AssistantCard }
  | { event: 'confirm_required'; data: AssistantCard }
  | { event: 'ui_action'; data: UiAction }
  | { event: 'done'; data: { conversationId: string; messageId: number | null } }
  | { event: 'error'; data: { code: string; detail?: string } };

export interface ChatRequest {
  conversationId?: string | null;
  message: string;
  context: AssistantContext;
}

/** Akış öncesi HTTP hatası ya da akış içi `error` olayı; `code` sunucu kodu. */
export class AssistantError extends Error {
  constructor(
    readonly code: string,
    readonly status: number | null = null,
    readonly limit: string | null = null
  ) {
    super(code);
  }
}

/** `POST /api/assistant/actions/{id}/confirm` sonucu. */
export interface ActionResult {
  ok: boolean;
  data: unknown;
  errorCode: string | null;
  message: string | null;
  cards: AssistantCard[] | null;
  uiActions: UiAction[] | null;
}

export type FeedbackRating = 'up' | 'down';
export const FEEDBACK_REASONS = ['wrongInfo', 'notHelpful', 'tooLong', 'inappropriate', 'other'] as const;
export type FeedbackReason = (typeof FEEDBACK_REASONS)[number];

/** Paneldeki bir mesaj. Asistan mesajı akarken büyür. */
export interface PanelMessage {
  id: number;
  role: 'user' | 'assistant';
  text: string;
  cards: AssistantCard[];
  confirmStates: Record<string, ConfirmState>;
  streaming: boolean;
  /** `done` olayından; geri bildirim için. */
  messageId: number | null;
  errorCode: string | null;
  feedback: FeedbackRating | null;
  activeTool: string | null;
}

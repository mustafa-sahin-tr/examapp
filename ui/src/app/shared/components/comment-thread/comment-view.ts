import { WorksheetComment } from '../../../models/worksheet-comment.model';

/** Optimistic gönderim durumu: sunucudan gelen/kaydedilen = `sent`. */
export type CommentSendState = 'sent' | 'sending' | 'failed';

/** Ekrandaki tek yorum. `key` gönderim boyunca sabittir (optimistic kayıt sunucu kaydıyla değişse de DOM korunur). */
export interface CommentView {
  key: string;
  comment: WorksheetComment;
  state: CommentSendState;
  /** `failed` durumunda kullanıcıya gösterilen (backend) mesaj. */
  error: string | null;
}

/** Kök yorum + tek seviye cevapları. */
export interface ThreadView {
  key: string;
  root: CommentView;
  /** Eskiden yeniye; gönderilmekte olanlar sonda. */
  replies: CommentView[];
  /** Kökün sunucudaki toplam reply sayısı. */
  replyCount: number;
  canReply: boolean;
  /** Replies ucunun sonraki (daha yeni) sayfası; `olderStarted` iken null = hepsi yüklendi. */
  olderCursor: string | null;
  olderStarted: boolean;
  loadingOlder: boolean;
  olderError: string | null;
}

/** Ekranda gösterilmeyen (önceki) cevap sayısı. */
export function hiddenReplyCount(thread: ThreadView): number {
  const shown = thread.replies.filter((r) => r.state === 'sent').length;
  return Math.max(0, thread.replyCount - shown);
}

/** "Önceki cevapları göster" butonu görünür mü. */
export function canLoadOlderReplies(thread: ThreadView): boolean {
  return hiddenReplyCount(thread) > 0 && !(thread.olderStarted && thread.olderCursor === null);
}

export function serverCommentView(comment: WorksheetComment): CommentView {
  return { key: `c${comment.id}`, comment, state: 'sent', error: null };
}

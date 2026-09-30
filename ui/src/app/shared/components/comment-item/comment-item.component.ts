import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, model, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { formatRelativeTime, parseUtcDate } from '../../../pages/notifications/notification-format';
import { CommentComposerComponent } from '../comment-composer/comment-composer.component';
import { CommentView, ThreadView, canLoadOlderReplies, hiddenReplyCount } from '../comment-thread/comment-view';

/**
 * Issue #105 — tek kök yorum + tek seviye cevapları (recursion yok). Veri ve HTTP `app-comment-thread`'te;
 * bu komponent yalnız çizer ve olay yayar. Yorum gövdesi/yazar adı düz metin olarak, bidi-izole (`<bdi dir="auto">`)
 * ve `white-space: pre-wrap` ile gösterilir — `innerHTML` kullanılmaz.
 * Çeviriler `comments` scope'undan gelir — scope'u ebeveyn sağlar.
 */
@Component({
  selector: 'app-comment-item',
  standalone: true,
  imports: [NgTemplateOutlet, MatButtonModule, MatIconModule, TranslocoDirective, CommentComposerComponent],
  templateUrl: './comment-item.component.html',
  styleUrls: ['./comment-item.component.scss'],
})
export class CommentItemComponent {
  readonly thread = input.required<ThreadView>();
  /** 2 sn vurgulanan yorum (derin link). */
  readonly highlightedId = input<number | null>(null);
  /** Göreli zaman için "şimdi" (ebeveynin dakikalık tick'i) ve Intl dili. */
  readonly now = input(Date.now());
  readonly locale = input('tr');
  /** "Yanıtla" alanı açık mı — derin linkte ebeveyn açabilir. */
  readonly replyOpen = model(false);
  /** Cevap alanı açılınca odak alsın mı. */
  readonly replyAutofocus = input(true);

  readonly replySubmit = output<string>();
  readonly retry = output<string>();
  /** Gönderilemeyen yorumu listeden kaldır (içerik yazma alanına geri alındıktan sonra). */
  readonly discard = output<string>();
  /** Gönderilemeyen KÖK yorumu düzenle — ebeveyn içeriği ana yazma alanına taşır. */
  readonly editRoot = output<string>();
  readonly loadOlder = output<void>();

  protected readonly replyDraft = signal('');
  protected readonly hiddenCount = computed(() => hiddenReplyCount(this.thread()));
  protected readonly showOlder = computed(() => canLoadOlderReplies(this.thread()));
  protected readonly repliesId = computed(() => `comment-replies-${this.thread().key}`);

  protected initial(view: CommentView): string {
    const name = view.comment.authorDisplayName?.trim() ?? '';
    const first = Array.from(name)[0];
    return first ? first.toLocaleUpperCase(this.locale()) : '?';
  }

  protected relativeTime(view: CommentView): string {
    return formatRelativeTime(view.comment.createdAt, this.now(), this.locale());
  }

  protected absoluteTime(view: CommentView): string {
    const date = parseUtcDate(view.comment.createdAt);
    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleString(this.locale(), { dateStyle: 'medium', timeStyle: 'short' });
  }

  protected toggleReply(): void {
    this.replyOpen.update((open) => !open);
  }

  protected submitReply(body: string): void {
    this.replySubmit.emit(body);
    this.replyDraft.set('');
  }

  protected cancelReply(): void {
    this.replyOpen.set(false);
  }

  /** Gönderilemeyen cevabı yazma alanına geri al ve listeden kaldır. */
  protected editReply(view: CommentView): void {
    this.replyDraft.set(view.comment.body);
    this.replyOpen.set(true);
    this.discard.emit(view.key);
  }
}

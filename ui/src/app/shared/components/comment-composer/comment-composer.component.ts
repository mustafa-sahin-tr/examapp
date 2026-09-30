import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective } from '@jsverse/transloco';
import { WORKSHEET_COMMENT_MAX_LENGTH } from '../../../models/worksheet-comment.model';

let nextComposerId = 0;

/**
 * Issue #105 — yorum/cevap yazma alanı: karakter sayacı (2000), Ctrl/Cmd+Enter ile gönderme, mobilde ikon-only
 * 44px gönder butonu. Taslak `draft` model'i ile ebeveynde tutulur; ebeveyn başarılı gönderimde temizler.
 * Çeviriler `comments` scope'undan gelir — scope'u ebeveyn (`app-comment-thread`) sağlar.
 */
@Component({
  selector: 'app-comment-composer',
  standalone: true,
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslocoDirective],
  templateUrl: './comment-composer.component.html',
  styleUrls: ['./comment-composer.component.scss'],
})
export class CommentComposerComponent {
  private readonly injector = inject(Injector);

  readonly draft = model('');
  readonly placeholder = input.required<string>();
  readonly ariaLabel = input.required<string>();
  /** Oluşturulunca textarea'ya odaklan (derin linkte öğretmen cevap alanı, "Yanıtla"). */
  readonly autofocus = input(false);
  /** Reply alanında "Vazgeç" butonu. */
  readonly showCancel = input(false);

  readonly submitted = output<string>();
  readonly cancelled = output<void>();

  protected readonly max = WORKSHEET_COMMENT_MAX_LENGTH;
  protected readonly hintId = `comment-composer-hint-${nextComposerId++}`;
  protected readonly length = computed(() => this.draft().length);
  protected readonly tooLong = computed(() => this.length() > this.max);
  protected readonly canSend = computed(() => this.draft().trim().length > 0 && !this.tooLong());

  private readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('textarea');

  private autofocused = false;

  constructor() {
    // Textarea `*transloco` içinde: çeviri yüklenip görünüm çizildiğinde (viewChild dolunca) bir kez odaklan.
    effect(() => {
      const element = this.textarea();
      if (!element || this.autofocused || !this.autofocus()) {
        return;
      }
      this.autofocused = true;
      afterNextRender(() => element.nativeElement.focus(), { injector: this.injector });
    });
  }

  focus(): void {
    this.textarea()?.nativeElement.focus();
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      this.submit();
    }
  }

  protected submit(): void {
    if (!this.canSend()) {
      return;
    }
    this.submitted.emit(this.draft().trim());
  }
}

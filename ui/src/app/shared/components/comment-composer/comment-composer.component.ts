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
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective } from '@jsverse/transloco';
import { WORKSHEET_COMMENT_MAX_LENGTH } from '../../../models/worksheet-comment.model';
import { hasPersonalInfo } from '../../utils/pii-detect.util';

let nextComposerId = 0;

/**
 * Issue #105 — yorum/cevap yazma alanı: karakter sayacı (2000), Ctrl/Cmd+Enter ile gönderme, mobilde ikon-only
 * 44px gönder butonu. Taslak `draft` model'i ile ebeveynde tutulur; ebeveyn başarılı gönderimde temizler.
 * Çeviriler `comments` scope'undan gelir — scope'u ebeveyn (`app-comment-thread`) sağlar.
 *
 * Issue #305 — PII uyarısı: gövdede telefon/e-posta deseni varsa yazarken inline uyarı; gönderim engellenmez ama
 * ilk "Gönder"de onay istenir ("Yine de gönder" ya da ikinci kez "Gönder"). Metin değişince onay düşer.
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
  private readonly composerId = nextComposerId++;
  protected readonly hintId = `comment-composer-hint-${this.composerId}`;
  protected readonly piiId = `comment-composer-pii-${this.composerId}`;
  protected readonly length = computed(() => this.draft().length);
  protected readonly tooLong = computed(() => this.length() > this.max);
  protected readonly canSend = computed(() => this.draft().trim().length > 0 && !this.tooLong());
  /** Issue #305: gövdede telefon / e-posta var gibi mi (yalnız uyarı). */
  protected readonly piiDetected = computed(() => hasPersonalInfo(this.draft()));
  /** Onay istenen metin; taslak değişirse onay geçersizleşir. */
  private readonly piiConfirmFor = signal<string | null>(null);
  protected readonly piiConfirmPending = computed(
    () => this.piiDetected() && this.piiConfirmFor() !== null && this.piiConfirmFor() === this.draft()
  );
  protected readonly describedBy = computed(() => (this.piiDetected() ? `${this.hintId} ${this.piiId}` : this.hintId));

  private readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('textarea');
  private readonly sendAnywayButton = viewChild<string, ElementRef<HTMLButtonElement>>('sendAnywayBtn', { read: ElementRef });

  private autofocused = false;
  /** PII onayı istendi; "Yine de gönder" çizilince bir kez odaklanılır. */
  private focusSendAnyway = false;

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

    // Issue #305: buton `*transloco` + `@if` içinde; viewChild dolunca (çizildikten sonra) odaklan.
    effect(() => {
      const button = this.sendAnywayButton();
      if (!button || !this.focusSendAnyway) {
        return;
      }
      this.focusSendAnyway = false;
      afterNextRender(() => button.nativeElement.focus(), { injector: this.injector });
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
    if (this.piiDetected() && !this.piiConfirmPending()) {
      // İlk gönderim: onay iste, "Yine de gönder"e odaklan (ekran okuyucu uyarıyı ve butonu okur).
      this.focusSendAnyway = true;
      this.piiConfirmFor.set(this.draft());
      return;
    }
    this.emitDraft();
  }

  /** "Yine de gönder": PII uyarısına rağmen gönder. */
  protected sendAnyway(): void {
    if (!this.canSend()) {
      return;
    }
    this.emitDraft();
  }

  private emitDraft(): void {
    this.piiConfirmFor.set(null);
    this.submitted.emit(this.draft().trim());
  }
}

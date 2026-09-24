import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, take } from 'rxjs';
import {
  STUDY_LINK_TITLE_MAX_LENGTH,
  STUDY_LINK_URL_MAX_LENGTH,
  StudyLink,
  StudyLinkScope,
  StudyLinkSourceType,
} from '../../../models/study-link';
import {
  StudyLinkService,
  isActiveLimitError,
  rateLimitRetryAfter,
  studyLinkErrorMessage,
} from '../../../services/study-link.service';

/** Çalışma linki bileşenlerinin Transloco scope'u: `public/i18n/study-links/<lang>.json`. */
const STUDY_LINKS_SCOPE = 'study-links';

export interface StudyLinkDialogData {
  /** Yeni linkin kapsamı (düzenlemede kullanılmaz — konu/alt konu değiştirilemez). */
  scope: StudyLinkScope;
  /** Başlıkta gösterilen konu / alt konu adı. */
  scopeName?: string;
  /** Doluysa düzenleme modu. */
  link?: StudyLink;
  /** Kapsamdaki aktif link sayısı sınıra ulaştı mı (bu link hariç sayım manager'da yapılır). */
  activeLimitReached: boolean;
  maxActiveLinks: number;
}

/**
 * Yalnızca `http:` / `https:` şemalı, host'u olan mutlak URL'leri kabul eder.
 * `javascript:`, `data:`, `ftp:` vb. ve göreli adresler reddedilir (XSS / yanlış link riskine karşı).
 */
export function httpUrlValidator(control: AbstractControl<string | null>): ValidationErrors | null {
  const value = (control.value ?? '').trim();
  if (!value) return null;
  try {
    const url = new URL(value);
    if ((url.protocol === 'http:' || url.protocol === 'https:') && url.hostname) return null;
  } catch {
    // geçersiz URL → aşağıda hata döner
  }
  return { url: true };
}

/** youtube.com / youtu.be (alt alan adlarıyla) → YouTube; diğerleri → Other. Backend çıkarımıyla aynı kural. */
export function detectSourceType(value: string): StudyLinkSourceType {
  try {
    const host = new URL(value.trim()).hostname.toLowerCase();
    const isYouTube = ['youtube.com', 'youtu.be', 'youtube-nocookie.com'].some(
      (domain) => host === domain || host.endsWith(`.${domain}`)
    );
    return isYouTube ? 'YouTube' : 'Other';
  } catch {
    return 'Other';
  }
}

/**
 * Issue #61 — çalışma linki ekleme / düzenleme dialog'u. Kaydetme isteği dialog içinde atılır; hata (400 doğrulama,
 * 409 aktif link sınırı) dialog'da gösterilir, kullanıcının girdisi kaybolmaz. Başarıda kaydedilen link ile kapanır.
 */
@Component({
  selector: 'app-study-link-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(STUDY_LINKS_SCOPE)],
  templateUrl: './study-link-dialog.component.html',
  styleUrls: ['./study-link-dialog.component.scss'],
})
export class StudyLinkDialogComponent {
  readonly data = inject<StudyLinkDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<StudyLinkDialogComponent, StudyLink>>(MatDialogRef);
  private readonly service = inject(StudyLinkService);
  private readonly transloco = inject(TranslocoService);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly snack = inject(MatSnackBar);

  readonly titleMax = STUDY_LINK_TITLE_MAX_LENGTH;
  readonly urlMax = STUDY_LINK_URL_MAX_LENGTH;
  readonly isEdit = !!this.data.link;
  readonly sourceTypes: StudyLinkSourceType[] = ['YouTube', 'Other'];

  /** Aktifleştirme sınırı: link zaten aktif değilse ve sınır doluysa aktif seçilemez. */
  readonly activationBlocked = this.data.activeLimitReached && !this.data.link?.isActive;

  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.group({
    title: [
      this.data.link?.title ?? '',
      [Validators.required, Validators.maxLength(STUDY_LINK_TITLE_MAX_LENGTH), notBlank],
    ],
    url: [
      this.data.link?.url ?? '',
      [Validators.required, Validators.maxLength(STUDY_LINK_URL_MAX_LENGTH), httpUrlValidator],
    ],
    sourceType: [this.data.link?.sourceType ?? ('Other' as StudyLinkSourceType)],
    isActive: [{ value: this.data.link?.isActive ?? !this.data.activeLimitReached, disabled: this.activationBlocked }],
  });

  constructor() {
    // Hata metni şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${STUDY_LINKS_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();

    // Kullanıcı kaynak tipini elle seçmediyse URL'den tahmin et (backend ile aynı kural).
    this.form.controls.url.valueChanges.pipe(takeUntilDestroyed()).subscribe((url) => {
      if (!this.form.controls.sourceType.dirty && this.form.controls.url.valid) {
        this.form.controls.sourceType.setValue(detectSourceType(url));
      }
    });
  }

  private translate(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${STUDY_LINKS_SCOPE}.${key}`, params) ?? '';
  }

  cancel(): void {
    if (this.submitting()) return;
    this.dialogRef.close();
  }

  save(): void {
    if (this.submitting()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const title = value.title.trim();
    const url = value.url.trim();
    // Pasif alan disabled olsa da getRawValue değeri taşır; sınır doluyken aktif gönderilmez.
    const isActive = this.activationBlocked ? false : value.isActive;

    const request$: Observable<StudyLink> = this.data.link
      ? this.service.update(this.data.link.id, { title, url, sourceType: value.sourceType, isActive })
      : this.service.create({ ...this.data.scope, title, url, sourceType: value.sourceType, isActive });

    this.submitting.set(true);
    this.error.set(null);
    request$.subscribe({
      next: (link) => {
        this.submitting.set(false);
        this.dialogRef.close(link);
      },
      error: (err: unknown) => {
        this.submitting.set(false);
        // 429 (yazma rate limit): geçici durum → snackbar; dialog açık kalır, kullanıcı biraz sonra tekrar kaydeder.
        const retryAfter = rateLimitRetryAfter(err);
        if (retryAfter !== undefined) {
          const fallback =
            retryAfter === null
              ? this.translate('manager.rateLimitedNoWait')
              : this.translate('manager.rateLimited', { seconds: retryAfter });
          this.snack.open(studyLinkErrorMessage(err) ?? fallback, this.translate('manager.close'), { duration: 5000 });
          return;
        }
        // 409 ActiveLimitReached: sınır bu arada dolmuş olabilir; kullanıcı pasif olarak kaydedebilsin.
        if (isActiveLimitError(err)) this.form.controls.isActive.setValue(false);
        // 409 TotalLimitReached, 403 NotOwner/TeacherNotApproved, 400 doğrulama → sunucu metni dialog içinde.
        this.error.set(studyLinkErrorMessage(err) ?? this.translate('dialog.saveFailed'));
      },
    });
  }
}

/** Yalnız boşluktan oluşan başlığı reddeder (backend Required ile aynı). */
function notBlank(control: AbstractControl<string | null>): ValidationErrors | null {
  const value = control.value ?? '';
  return value.length > 0 && !value.trim() ? { required: true } : null;
}

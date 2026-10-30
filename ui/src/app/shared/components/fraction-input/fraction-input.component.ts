import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';

import { FractionParts, composeFraction, splitFraction } from '../../../models/slot-fill';

/**
 * Issue #575: kesir girişi — pay / payda alt alta (kesir çizgisiyle); `mixed` iken solda tam kısım (3 alan). Değer
 * `composeFraction` ile tek metne çevrilir: "3/4", "2 3/5", yalnız tam "2"; hepsi boşsa "". Karşılaştırma sunucuda
 * (`SlotAnswerNormalizer`) yapılır, bileşen yalnız biçimler.
 *
 * `valueInput` her alan değişiminde (yazarken), `valueCommit` alan bırakılınca (`change`) yayınlanır; kayıt commit'e bağlanır.
 */
@Component({
  selector: 'app-fraction-input',
  standalone: true,
  templateUrl: './fraction-input.component.html',
  styleUrl: './fraction-input.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    role: 'group',
    '[attr.aria-label]': 'ariaLabel()',
    '[class.is-mixed]': 'mixed()',
    '[class.is-disabled]': 'disabled()',
  },
})
export class FractionInputComponent {
  readonly value = input<string>('');
  /** Tam sayılı kesir: tam + pay + payda. */
  readonly mixed = input(false);
  readonly disabled = input(false);
  /** Grubun erişilebilir adı (ör. "Kutu 3"); alanlar "<ad>, pay" gibi adlandırılır. */
  readonly ariaLabel = input('');
  /** Alan adları (çağıran çevirir). */
  readonly wholeLabel = input('');
  readonly numeratorLabel = input('');
  readonly denominatorLabel = input('');
  /** Alan id'lerinin öneki (dışarıdan odaklamak için `<prefix>-numerator` vb.). */
  readonly idPrefix = input('');

  readonly valueInput = output<string>();
  readonly valueCommit = output<string>();

  readonly parts = signal<FractionParts>({ whole: '', numerator: '', denominator: '' });
  private readonly wholeEl = viewChild<ElementRef<HTMLInputElement>>('whole');
  private readonly numeratorEl = viewChild<ElementRef<HTMLInputElement>>('numerator');
  private readonly denominatorEl = viewChild<ElementRef<HTMLInputElement>>('denominator');
  readonly composed = computed(() => composeFraction(this.parts()));

  constructor() {
    // Dışarıdan gelen değer (kayıtlı cevap / temizle) yazılan haliyle aynı değilse alanlara geri yüklenir; aynıysa
    // kullanıcının ara hali (ör. yalnız pay) korunur.
    effect(() => {
      const incoming = (this.value() ?? '').trim();
      const mixed = this.mixed();
      if (incoming === untracked(() => this.composed())) return;
      const split = splitFraction(incoming);
      // Tam kısmı olmayan kesir girişinde tam alanı yok: kesir değilse paya konur (öğrenci düzeltebilsin).
      const next: FractionParts =
        !mixed && split.whole && !split.numerator && !split.denominator
          ? { whole: '', numerator: split.whole, denominator: '' }
          : !mixed && split.whole
            ? { whole: '', numerator: `${split.whole} ${split.numerator}`.trim(), denominator: split.denominator }
            : split;
      this.parts.set(next);
      // `[value]` bağlaması son çizilen değer değişmediyse DOM'u güncellemez (ör. '' → ''); ekran ile durum ayrışmasın.
      untracked(() => this.writeDom(next));
    });
  }

  /**
   * Değer her zaman üç alanın EKRANDAKİ halinden kurulur (#575 E2E): bir alanın durumu geride kalsa bile (gecikmeli
   * yankı, çizilmemiş bağlama) yayınlanan değer öğrencinin gördüğüyle aynı olur.
   */
  onInput(): void {
    this.syncFromDom();
    this.valueInput.emit(this.composed());
  }

  onCommit(): void {
    this.syncFromDom();
    this.valueCommit.emit(this.composed());
  }

  /** Gezinti notu (B1): payda yalnız rakam, pay ve tam kısım rakam + baştaki eksi alır; harf ekrana da yazılmaz. */
  private syncFromDom(): void {
    const current = this.parts();
    const next: FractionParts = {
      whole: this.mixed() ? signedDigits(this.wholeEl()?.nativeElement.value ?? current.whole) : '',
      numerator: signedDigits(this.numeratorEl()?.nativeElement.value ?? current.numerator),
      denominator: digitsOnly(this.denominatorEl()?.nativeElement.value ?? current.denominator),
    };
    this.parts.set(next);
    this.writeDom(next);
  }

  private writeDom(parts: FractionParts): void {
    const set = (ref: ElementRef<HTMLInputElement> | undefined, value: string) => {
      if (ref && ref.nativeElement.value !== value) ref.nativeElement.value = value;
    };
    set(this.wholeEl(), parts.whole);
    set(this.numeratorEl(), parts.numerator);
    set(this.denominatorEl(), parts.denominator);
  }

  fieldLabel(part: string): string {
    const base = this.ariaLabel();
    return base && part ? `${base}, ${part}` : base || part;
  }
}

function digitsOnly(value: string): string {
  return value.replace(/[^0-9]/g, '');
}

function signedDigits(value: string): string {
  const trimmed = value.trimStart();
  const negative = trimmed.startsWith('-') || trimmed.startsWith('\u2212');
  return (negative ? '-' : '') + digitsOnly(trimmed);
}

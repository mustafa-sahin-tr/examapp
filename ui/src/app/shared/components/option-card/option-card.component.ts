import { ChangeDetectionStrategy, Component, ElementRef, booleanAttribute, computed, inject, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * Sihirbaz seçenek kartı (issue #135). Native `<button>` üzerine uygulanır, böylece Space/Enter
 * tıklamayı tarayıcı üretir: `<button app-option-card [icon]="..." [label]="..." (click)="...">`.
 * Tek seçimde `role=radio`, çoklu seçimde `role=checkbox`; seçili durum renk + işaret + dolu ikon ile
 * verilir (renk tek başına değil). Ok tuşu gezinmesi ve roving tabindex `app-option-group`'tadır.
 *
 * `icon` güvenli bir Material Symbols adı olmalıdır (ör. `programOptionIcon` pipe'ından geçmiş).
 */
@Component({
  selector: 'button[app-option-card]',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './option-card.component.html',
  styleUrl: './option-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    type: 'button',
    class: 'option-card',
    '[class.option-card--selected]': 'selected()',
    '[class.option-card--multi]': 'multi()',
    '[class.option-card--numeric]': 'numeric()',
    '[attr.role]': "multi() ? 'checkbox' : 'radio'",
    '[attr.aria-checked]': 'selected()',
    '[attr.tabindex]': 'rovingTabIndex()',
    '[disabled]': 'disabled()',
  },
})
export class OptionCardComponent {
  readonly icon = input<string>('');
  readonly label = input.required<string>();
  readonly selected = input(false, { transform: booleanAttribute });
  readonly multi = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });

  /** Sayı etiketleri (8/12/16, 1/2/3) büyük puntoyla çizilir. */
  readonly numeric = computed(() => /^\d{1,3}$/.test(this.label().trim()));

  /** Grup tarafından yönetilir; `null` → tarayıcı varsayılanı (sekmeyle ulaşılabilir). */
  readonly rovingTabIndex = signal<number | null>(null);

  readonly element: HTMLButtonElement = inject<ElementRef<HTMLButtonElement>>(ElementRef).nativeElement;

  focus(): void {
    this.element.focus();
  }
}

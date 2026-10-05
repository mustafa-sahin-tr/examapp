import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  contentChildren,
  effect,
  input,
  signal,
} from '@angular/core';
import { OptionCardComponent } from './option-card.component';

/**
 * `app-option-card` kartlarını saran grup (issue #135).
 * - Tek seçim: `role=radiogroup`, roving tabindex — Tab gruba bir kez girer (seçili kart ya da ilk kart),
 *   ok tuşları kartlar arasında odağı gezdirir, Space/Enter seçer (native button tıklaması).
 * - Çoklu seçim: `role=group`, her kart sekmeyle ulaşılabilir; ok tuşları da çalışır, Space aç/kapat.
 * Ok tuşları seçim yapmaz: dallanan adımda yanlışlıkla dal değiştirmeyi önler.
 */
@Component({
  selector: 'app-option-group',
  standalone: true,
  template: '<ng-content />',
  styleUrl: './option-group.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.role]': "multi() ? 'group' : 'radiogroup'",
    '[attr.aria-labelledby]': 'labelledBy() || null',
    '[attr.aria-describedby]': 'describedBy() || null',
    '(keydown)': 'onKeydown($event)',
    '(focusin)': 'onFocusIn($event)',
  },
})
export class OptionGroupComponent {
  readonly multi = input(false, { transform: booleanAttribute });
  readonly labelledBy = input<string>('');
  readonly describedBy = input<string>('');

  readonly cards = contentChildren(OptionCardComponent);

  private readonly focused = signal<OptionCardComponent | null>(null);

  /** Roving tabindex'in 0 olduğu kart: son odaklanan (hâlâ gruptaysa) → seçili → ilk etkin kart. */
  readonly activeCard = computed<OptionCardComponent | null>(() => {
    const cards = this.cards().filter((c) => !c.disabled());
    const focused = this.focused();
    if (focused && cards.includes(focused)) return focused;
    return cards.find((c) => c.selected()) ?? cards[0] ?? null;
  });

  constructor() {
    effect(() => {
      const multi = this.multi();
      const active = this.activeCard();
      for (const card of this.cards()) {
        card.rovingTabIndex.set(multi ? null : card === active ? 0 : -1);
      }
    });
  }

  onFocusIn(event: FocusEvent): void {
    const card = this.cards().find((c) => c.element === event.target);
    if (card) this.focused.set(card);
  }

  onKeydown(event: KeyboardEvent): void {
    const cards = this.cards().filter((c) => !c.disabled());
    if (cards.length === 0) return;
    const index = cards.findIndex((c) => c.element === event.target);
    if (index < 0) return;

    let target: number;
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        target = (index + 1) % cards.length;
        break;
      case 'ArrowLeft':
      case 'ArrowUp':
        target = (index - 1 + cards.length) % cards.length;
        break;
      case 'Home':
        target = 0;
        break;
      case 'End':
        target = cards.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    this.focused.set(cards[target]);
    cards[target].focus();
  }
}

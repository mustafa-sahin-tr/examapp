import { Component, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { ConversationSummary, DIRECT_MESSAGES_SCOPE } from '../../../models/direct-message.model';
import { parseUtcDate } from '../../../pages/notifications/notification-format';
import { activeIntlLocale } from '../../utils/active-locale.util';

export interface DmConversationSelectEvent {
  item: ConversationSummary;
  /** Seçimi yapan öğe — panel kapanınca odak buraya döner. */
  trigger: HTMLElement | null;
}

/**
 * Issue #106 — konuşma satırları (öğrenci "Konuşmalarım", öğretmen gelen kutusu). Her satır bir buton;
 * okunmamış nokta yalnız görsel, sayı `aria-label` içinde. Önizleme düz metin (interpolasyon).
 */
@Component({
  selector: 'app-dm-conversation-list',
  standalone: true,
  imports: [MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope(DIRECT_MESSAGES_SCOPE)],
  templateUrl: './dm-conversation-list.component.html',
  styleUrls: ['./dm-conversation-list.component.scss'],
})
export class DmConversationListComponent {
  readonly items = input.required<ConversationSummary[]>();
  readonly selectedId = input<number | null>(null);
  /** Öğretmen görünümü: "Engellendi" etiketi. */
  readonly showBlocked = input(false);
  readonly listLabel = input('');

  readonly conversationSelect = output<DmConversationSelectEvent>();

  protected choose(item: ConversationSummary, event: Event): void {
    this.conversationSelect.emit({ item, trigger: event.currentTarget instanceof HTMLElement ? event.currentTarget : null });
  }

  protected initial(name: string): string {
    const first = Array.from(name.trim())[0];
    return first ? first.toLocaleUpperCase(activeIntlLocale()) : '?';
  }

  protected timeText(iso: string): string {
    const date = parseUtcDate(iso);
    if (Number.isNaN(date.getTime())) {
      return '';
    }
    const sameDay = date.toDateString() === new Date().toDateString();
    return sameDay
      ? date.toLocaleTimeString(activeIntlLocale(), { hour: '2-digit', minute: '2-digit' })
      : date.toLocaleDateString(activeIntlLocale(), { day: 'numeric', month: 'short' });
  }
}

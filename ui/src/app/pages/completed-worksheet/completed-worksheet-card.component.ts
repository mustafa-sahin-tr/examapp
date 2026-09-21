// completed-worksheet-card.component.ts
import { Component, Input } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { InstanceSummary } from '../../models/test-instance';

/**
 * Kart, scope'lu bir sayfanın alt komponenti değil — `worksheet-list-enhanced` içinden
 * kullanılıyor. Bu yüzden kendi Transloco scope'unu kendisi sağlar (issue #183).
 */
@Component({
  selector: 'app-completed-worksheet-card',
  standalone: true,
  imports: [TranslocoDirective],
  providers: [provideTranslocoScope('completed-worksheet')],
  templateUrl: './completed-worksheet-card.component.html',
  styleUrls: ['./completed-worksheet-card.component.scss'],
})
export class CompletedWorksheetCardComponent {
  @Input() completedTest!: InstanceSummary;
}

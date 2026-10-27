import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  QUESTION_BANK_SCOPE,
  QuestionBankPanelComponent,
} from '../../shared/components/question-bank-panel/question-bank-panel.component';
import { AuthService } from '../../services/auth.service';

/** issue #471: soru bankası sayfası — hazır soruları ders/konu/alt konuya göre arar. */
@Component({
  selector: 'app-question-bank',
  standalone: true,
  imports: [TranslocoDirective, QuestionBankPanelComponent],
  providers: [provideTranslocoScope(QUESTION_BANK_SCOPE)],
  template: `
    <div class="question-bank-page" *transloco="let t; prefix: 'question-bank'">
      <header class="question-bank-page__header">
        <h1>{{ t('title') }}</h1>
        <p>{{ t('subtitle') }}</p>
      </header>
      <app-question-bank-panel [canApproveSolutions]="canApproveSolutions" [canApproveOthersSolutions]="isAdmin" />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./question-bank.component.scss'],
})
export class QuestionBankComponent {
  private readonly auth = inject(AuthService);
  /** Issue #580: etkinlik çözümünü yalnız Admin + QuestionTrainer onaylar (realm rolü; sunucu da 403 verir). */
  readonly isAdmin = this.auth.hasRealmRole('Admin');
  readonly canApproveSolutions = this.isAdmin || this.auth.hasRealmRole('QuestionTrainer');
}

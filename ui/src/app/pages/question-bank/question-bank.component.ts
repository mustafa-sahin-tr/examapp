import { Component, ChangeDetectionStrategy } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  QUESTION_BANK_SCOPE,
  QuestionBankPanelComponent,
} from '../../shared/components/question-bank-panel/question-bank-panel.component';

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
      <app-question-bank-panel />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./question-bank.component.scss'],
})
export class QuestionBankComponent {}

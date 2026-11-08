import { Component, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  QUESTION_BANK_SCOPE,
  QuestionBankPanelComponent,
} from '../../shared/components/question-bank-panel/question-bank-panel.component';

/** issue #471: soru bankası sayfası — hazır soruları ders/konu/alt konuya göre arar. */
@Component({
  selector: 'app-question-bank',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoDirective, QuestionBankPanelComponent],
  providers: [provideTranslocoScope(QUESTION_BANK_SCOPE)],
  template: `
    <div class="question-bank-page" *transloco="let t; prefix: 'question-bank'">
      <header class="question-bank-page__header">
        <div class="question-bank-page__heading">
          <h1 class="ms-page-title">{{ t('title') }}</h1>
          <p class="ms-page-subtitle">{{ t('subtitle') }}</p>
        </div>
        <!-- #1053: fotoğraftan hızlı soru ekleme -->
        <a mat-stroked-button class="question-bank-page__quick-add" routerLink="/quick-add">
          <mat-icon aria-hidden="true">add_a_photo</mat-icon>{{ t('quickAdd') }}
        </a>
      </header>
      <app-question-bank-panel filterLayout="selects" />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./question-bank.component.scss'],
})
export class QuestionBankComponent {}

import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  QUESTION_BANK_SCOPE,
  QuestionBankPanelComponent,
  QuestionBankScope,
  QuestionBankSourceFilter,
  QuestionBankStatusFilter,
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
      <app-question-bank-panel filterLayout="selects"
        [initialScope]="initialScope" [initialSource]="initialSource" [initialStatus]="initialStatus" />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./question-bank.component.scss'],
})
export class QuestionBankComponent {
  // Issue #1315: admin panosundaki sınıflandırma grupları bankayı ?scope=all&source=AI&status=unclassified ile açar.
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  readonly initialScope = pick<QuestionBankScope>(this.query.get('scope'), ['mine', 'all'], 'mine');
  readonly initialSource = pick<QuestionBankSourceFilter>(this.query.get('source'), ['AI', 'Human', 'Book'], 'any');
  readonly initialStatus = pick<QuestionBankStatusFilter>(this.query.get('status'), ['classified', 'unclassified'], 'any');
}

/** Bilinen değerlerden biriyse onu, değilse varsayılanı döner (URL'den gelen serbest metin). */
function pick<T extends string>(value: string | null, allowed: readonly T[], fallback: T): T {
  return allowed.includes(value as T) ? (value as T) : fallback;
}

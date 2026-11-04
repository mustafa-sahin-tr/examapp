import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { filter } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { MockExamDetail, MockExamProblem, MockExamSection, MockExamService } from '../../services/mock-exam.service';
import { QuestionBankPanelComponent } from '../../shared/components/question-bank-panel/question-bank-panel.component';

export interface SectionQuestionsDialogData {
  title: string;
  /** Dersin ya da başka dersin kullandığı sorular seçilemez. */
  usedIds: number[];
  /** Bu derste kalan yer: fazlası seçimi kaybettirmeden önce engellenir. */
  remaining: number;
}

@Component({
  selector: 'app-section-questions-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, QuestionBankPanelComponent, TranslocoDirective],
  providers: [provideTranslocoScope('mock-exams')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <ng-container *transloco="let t; prefix: 'mock-exams'">
      <h2 mat-dialog-title>{{ data.title }}</h2>
      <mat-dialog-content>
        <app-question-bank-panel filterLayout="selects" [selectable]="true" [disabledIds]="data.usedIds" (selectionChange)="selected.set($event)" />
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <span class="me-note me-dialog-limit" [class.me-error]="overLimit()" [attr.role]="overLimit() ? 'alert' : null">{{ t('builder.remaining', { count: data.remaining }) }}</span>
        <button mat-button (click)="dialogRef.close()">{{ t('builder.cancel') }}</button>
        <button mat-flat-button color="primary" (click)="dialogRef.close(selected())" [disabled]="selected().length === 0 || overLimit()">{{ t('builder.addSelected', { count: selected().length }) }}</button>
      </mat-dialog-actions>
    </ng-container>
  `,
})
export class SectionQuestionsDialogComponent {
  readonly data = inject<SectionQuestionsDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject<MatDialogRef<SectionQuestionsDialogComponent, number[]>>(MatDialogRef);
  readonly selected = signal<number[]>([]);
  readonly overLimit = computed(() => this.selected().length > this.data.remaining);
}

/** Issue #693: şablondaki dersleri Soru Havuzu'ndan doldurur, sayılar tamamsa yayınlar. */
@Component({
  selector: 'app-mock-exam-builder',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatCardModule, MatCheckboxModule, MatDialogModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope('mock-exams')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <div class="me-root" *transloco="let t; prefix: 'mock-exams'">
      @if (loadFailed()) {
        <div class="me-error" role="alert">{{ t('builder.loadFailed') }}</div>
      }
      @if (exam(); as current) {
        <h1 class="me-title">{{ current.name }}</h1>
        <p class="me-note">{{ current.templateName }} · {{ current.gradeName }} · {{ current.publishedAt ? t('published') : t('draft') }}</p>

        @if (errorKey(); as key) {
          <div class="me-error" role="alert">{{ t('errors.' + key) }}</div>
        }

        @for (session of current.sessions; track session.id) {
          <mat-card class="me-card">
            <mat-card-header>
              <mat-card-title>{{ session.name }}</mat-card-title>
              <mat-card-subtitle>{{ t('minutes', { count: session.durationSeconds / 60 }) }}</mat-card-subtitle>
            </mat-card-header>
            @if (current.publishedAt && !isAdmin) {
              <a mat-button class="me-assign" [routerLink]="['/test', session.worksheetId]">{{ t('builder.assign') }}</a>
            }
            <mat-card-content>
              @for (section of session.sections; track section.sectionId) {
                <div class="me-section">
                  <div class="me-section-head">
                    <span>
                      @if (section.groupName) {
                        <span class="me-note">{{ section.groupName }} · </span>
                      }
                      <b>{{ section.name }}</b>
                    </span>
                    <span class="me-count" [class.me-count-done]="section.questionIds.length === section.requiredCount">
                      {{ section.questionIds.length }} / {{ section.requiredCount }}
                    </span>
                    @if (!current.publishedAt) {
                      <button mat-button class="me-add" (click)="addQuestions(section)" [disabled]="busy() || section.questionIds.length >= section.requiredCount">
                        <mat-icon>add</mat-icon>
                        {{ t('builder.addQuestions') }}
                      </button>
                    }
                  </div>
                  @if (section.questions.length > 0) {
                    <ul class="me-questions" [attr.aria-label]="section.name">
                      @for (question of section.questions; track question.id) {
                        <li class="me-question">
                          @if (question.imageUrl) {
                            <img class="me-question-image" [src]="question.imageUrl" [alt]="t('builder.questionImage', { id: question.id })" loading="lazy" />
                          }
                          <span class="me-question-text">
                            <b>#{{ question.id }}</b>
                            @if (question.text) {
                              <!-- Soru metni HTML olabilir; Soru Havuzu gibi Angular'ın temizlediği innerHTML ile. -->
                              <span [innerHTML]="question.text"></span>
                            }
                          </span>
                          @if (!current.publishedAt) {
                            <button mat-icon-button class="me-remove" (click)="removeQuestion(section, question.id)" [disabled]="busy()" [attr.aria-label]="t('builder.removeQuestion', { id: question.id })"><mat-icon>close</mat-icon></button>
                          }
                        </li>
                      }
                    </ul>
                  }
                </div>
              }
            </mat-card-content>
          </mat-card>
        }

        @if (!current.publishedAt) {
          @if (current.problems.length > 0) {
            <div class="me-note">
              <b>{{ t('builder.problemsTitle') }}</b>
              <ul class="me-problems">
                @for (problem of visibleProblems(); track $index) {
                  <li>{{ t('problems.' + problem.code, problemArgs(problem)) }}</li>
                }
              </ul>
            </div>
          }

          <mat-checkbox [checked]="openToGrade()" (change)="openToGrade.set($event.checked)">{{ t('builder.openToGrade', { grade: current.gradeName }) }}</mat-checkbox>
          <div class="me-note">{{ t(isAdmin ? 'builder.openToGradeHintAdmin' : 'builder.openToGradeHint') }}</div>

          <div class="me-actions">
            <button mat-button class="me-delete" (click)="deleteDraft()" [disabled]="busy()">{{ t('builder.deleteDraft') }}</button>
            <button class="me-publish" mat-raised-button color="primary" (click)="publish()" [disabled]="busy() || current.problems.length > 0">
              <mat-icon>publish</mat-icon>
              {{ t('builder.publish') }}
            </button>
          </div>
        }
      }
    </div>
  `,
})
export class MockExamBuilderComponent implements OnInit {
  private readonly service = inject(MockExamService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);
  /** Test ayrıntı sayfası yöneticiye kapalı: atama bağlantısı 403 verirdi. */
  readonly isAdmin = inject(AuthService).hasRole('Admin');

  readonly exam = signal<MockExamDetail | null>(null);
  readonly loadFailed = signal(false);
  readonly busy = signal(false);
  readonly errorKey = signal<string | null>(null);
  /** Kapalı başlar: açık deneme bütün okullardaki öğrencilere görünür. */
  readonly openToGrade = signal(false);
  readonly visibleProblems = computed(() => (this.exam()?.problems ?? []).slice(0, 12));

  ngOnInit(): void {
    this.service.get(Number(this.route.snapshot.paramMap.get('id'))).subscribe({ next: (e) => this.exam.set(e), error: () => this.loadFailed.set(true) });
  }

  private usedIds(): number[] {
    return (this.exam()?.sessions ?? []).flatMap((s) => s.sections).flatMap((s) => s.questionIds);
  }

  addQuestions(section: MockExamSection): void {
    if (this.busy()) return;
    const data: SectionQuestionsDialogData = { title: section.name, usedIds: this.usedIds(), remaining: section.requiredCount - section.questionIds.length };
    this.dialog
      .open<SectionQuestionsDialogComponent, SectionQuestionsDialogData, number[]>(SectionQuestionsDialogComponent, { data, width: '960px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((ids) => {
        if (ids?.length) this.save(section, [...section.questionIds, ...ids]);
      });
  }

  removeQuestion(section: MockExamSection, questionId: number): void {
    // Önceki kayıt dönmeden `section` eskidir: ikinci çıkarma ilk soruyu geri eklerdi.
    if (this.busy()) return;
    this.save(section, section.questionIds.filter((id) => id !== questionId));
  }

  private save(section: MockExamSection, questionIds: number[]): void {
    const exam = this.exam();
    if (!exam) return;
    this.busy.set(true);
    this.errorKey.set(null);
    this.service.setSectionQuestions(exam.id, section.sectionId, questionIds).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.exam.set(updated);
      },
      error: (err: HttpErrorResponse) => this.fail(err),
    });
  }

  publish(): void {
    const exam = this.exam();
    if (!exam || exam.problems.length > 0 || this.busy()) return;
    const open = this.openToGrade();
    this.confirm(open ? 'publishOpenConfirm' : 'publishConfirm', 'publish', 'primary', { grade: exam.gradeName }, () => this.doPublish(exam, open));
  }

  private doPublish(exam: MockExamDetail, open: boolean): void {
    this.busy.set(true);
    this.errorKey.set(null);
    this.service.publish(exam.id, open).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.exam.set(updated);
      },
      error: (err: HttpErrorResponse) => {
        // Sayfa açıkken soru yayından kalkmış olabilir: güncel sorunlar gösterilir.
        const problems = (err.error as { problems?: MockExamProblem[] } | null)?.problems;
        if (problems) {
          this.busy.set(false);
          this.exam.set({ ...exam, problems });
          return;
        }
        this.fail(err);
      },
    });
  }

  deleteDraft(): void {
    const exam = this.exam();
    if (!exam || this.busy()) return;
    this.confirm('deleteConfirm', 'delete', 'warn', {}, () => {
      this.busy.set(true);
      this.errorKey.set(null);
      this.service.deleteDraft(exam.id).subscribe({
        next: () => void this.router.navigate(['/mock-exams']),
        error: (err: HttpErrorResponse) => this.fail(err),
      });
    });
  }

  /** Yayın geri alınamaz, silinen taslak geri gelmez. */
  private confirm(key: string, icon: string, confirmColor: string, params: Record<string, unknown>, then: () => void): void {
    const text = (part: string) => this.transloco.translate<string>(`mock-exams.builder.${key}.${part}`, params);
    const data: ConfirmDialogData = { title: text('title'), message: text('message'), confirmText: text('confirm'), icon, confirmColor };
    this.dialog.open(ConfirmDialogComponent, { data }).afterClosed().pipe(filter((ok) => !!ok)).subscribe(() => then());
  }

  problemArgs(problem: MockExamProblem): Record<string, unknown> {
    return { section: problem.section, id: problem.questionId, required: problem.required, actual: problem.actual };
  }

  private fail(err: HttpErrorResponse): void {
    this.busy.set(false);
    const code = (err.error as { errorCode?: string } | null)?.errorCode;
    const known = ['AlreadyPublished', 'TooManyQuestions', 'QuestionUsedTwice', 'QuestionNotUsable'];
    this.errorKey.set(code && known.includes(code) ? code : 'Failed');
  }
}

import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { MockExamResult, MockExamService, StudentMockExam, StudentMockExamSession } from '../../services/mock-exam.service';
import { TestService } from '../../services/test.service';

/** Issue #693: öğrencinin denemeleri. Her oturum ayrı başlar, süresi sunucuda işler. */
@Component({
  selector: 'app-student-mock-exams',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope('mock-exams')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <div class="me-root" *transloco="let t; prefix: 'mock-exams'">
      <h1 class="me-title">{{ t('student.title') }}</h1>

      @if (failed()) {
        <div class="me-error" role="alert">
          {{ t('student.failed') }}
          <button mat-button (click)="load()">{{ t('retry') }}</button>
        </div>
      } @else if (loaded() && exams().length === 0) {
        <p class="me-note">{{ t('student.empty') }}</p>
      }
      @if (startFailed()) {
        <div class="me-error" role="alert">{{ t('student.startFailed') }}</div>
      }

      @for (exam of exams(); track exam.id) {
        <mat-card class="me-card">
          <mat-card-header>
            <mat-card-title>{{ exam.name }}</mat-card-title>
            <mat-card-subtitle>{{ exam.templateName }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            @for (session of exam.sessions; track session.worksheetId) {
              <div class="me-section-head">
                <span>
                  <b>{{ session.name }}</b>
                  <span class="me-note"> · {{ t('student.questions', { count: session.questionCount }) }} · {{ t('minutes', { count: session.durationSeconds / 60 }) }}</span>
                </span>
                @if (session.status === 'Finished') {
                  <span class="me-count me-count-done">{{ t('student.finished') }}</span>
                } @else if (!session.canStart) {
                  <span class="me-note">{{ t('student.notAssigned') }}</span>
                } @else {
                  <button mat-stroked-button class="me-start" (click)="start(session)" [disabled]="starting()">
                    {{ session.status === 'InProgress' ? t('student.resume') : t('student.start') }}
                  </button>
                }
              </div>
            }
          </mat-card-content>
          @if (hasFinished(exam)) {
            <mat-card-actions class="me-actions">
              <a mat-button class="me-result-link" [routerLink]="['/my-mock-exams', exam.id, 'result']">{{ t('student.result') }}</a>
            </mat-card-actions>
          }
        </mat-card>
      }
    </div>
  `,
})
export class StudentMockExamsComponent {
  private readonly service = inject(MockExamService);
  private readonly tests = inject(TestService);
  private readonly router = inject(Router);

  readonly exams = signal<StudentMockExam[]>([]);
  readonly loaded = signal(false);
  readonly failed = signal(false);
  readonly starting = signal(false);
  readonly startFailed = signal(false);

  constructor() {
    this.load();
  }

  load(): void {
    this.failed.set(false);
    this.service.listForStudent().subscribe({
      next: (exams) => {
        this.loaded.set(true);
        this.exams.set(exams ?? []);
      },
      error: () => this.failed.set(true),
    });
  }

  hasFinished(exam: StudentMockExam): boolean {
    return exam.sessions.some((s) => s.status === 'Finished');
  }

  start(session: StudentMockExamSession): void {
    if (session.instanceId !== null) {
      void this.router.navigate(['/testsolve', session.instanceId]);
      return;
    }
    this.starting.set(true);
    this.startFailed.set(false);
    this.tests.startTest(session.worksheetId).subscribe({
      next: (result) => {
        this.starting.set(false);
        void this.router.navigate(['/testsolve', result.instanceId]);
      },
      error: () => {
        this.starting.set(false);
        this.startFailed.set(true);
      },
    });
  }
}

/** Ders başına ve toplam doğru, yanlış, boş, net. */
@Component({
  selector: 'app-mock-exam-result',
  standalone: true,
  imports: [DecimalPipe, RouterLink, MatButtonModule, MatCardModule, TranslocoDirective],
  providers: [provideTranslocoScope('mock-exams')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <div class="me-root" *transloco="let t; prefix: 'mock-exams'">
      @if (failed()) {
        <div class="me-error" role="alert">{{ t('result.failed') }}</div>
      }
      @if (result(); as current) {
        <h1 class="me-title">{{ current.name }}</h1>
        <p class="me-note">{{ current.templateName }}
          @if (current.wrongAnswersPerCorrect > 0) {
            · {{ t('result.rule', { count: current.wrongAnswersPerCorrect }) }}
          }
        </p>
        @if (!current.complete) {
          <p class="me-note me-warning" role="status">{{ t('result.incomplete') }}</p>
        }

        <mat-card class="me-card">
          <mat-card-content>
            <table class="me-table">
              <thead>
                <tr>
                  <th scope="col">{{ t('result.subject') }}</th>
                  <th scope="col">{{ t('result.correct') }}</th>
                  <th scope="col">{{ t('result.wrong') }}</th>
                  <th scope="col">{{ t('result.blank') }}</th>
                  <th scope="col">{{ t('result.net') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (section of current.sections; track $index) {
                  <tr>
                    <th scope="row">
                      @if (section.groupName) {
                        <span class="me-note">{{ section.groupName }} · </span>
                      }
                      {{ section.name }}
                    </th>
                    @if (section.pending) {
                      <td colspan="4" class="me-note">{{ t('result.pending') }}</td>
                    } @else {
                      <td>{{ section.correct }}</td>
                      <td>{{ section.wrong }}</td>
                      <td>{{ section.blank }}</td>
                      <td><b>{{ section.net | number: '1.0-2' }}</b></td>
                    }
                  </tr>
                }
              </tbody>
              <tfoot>
                <tr class="me-total">
                  <th scope="row">{{ t('result.total') }}</th>
                  <td>{{ current.correct }}</td>
                  <td>{{ current.wrong }}</td>
                  <td>{{ current.blank }}</td>
                  <td><b>{{ current.net | number: '1.0-2' }}</b></td>
                </tr>
              </tfoot>
            </table>
          </mat-card-content>
        </mat-card>
      }
      <a mat-button routerLink="/my-mock-exams">{{ t('result.back') }}</a>
    </div>
  `,
})
export class MockExamResultComponent implements OnInit {
  private readonly service = inject(MockExamService);

  private readonly route = inject(ActivatedRoute);

  readonly result = signal<MockExamResult | null>(null);
  readonly failed = signal(false);

  ngOnInit(): void {
    this.service.getResult(Number(this.route.snapshot.paramMap.get('id'))).subscribe({ next: (r) => this.result.set(r), error: () => this.failed.set(true) });
  }
}

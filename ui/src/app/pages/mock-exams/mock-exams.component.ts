import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Grade } from '../../models/student';
import { GradesService } from '../../services/grades.service';
import { ExamTemplate, MockExamListItem, MockExamService } from '../../services/mock-exam.service';

/** Issue #693: öğretmenin denemeleri ve yeni deneme. #870: yeni deneme şablon kartından başlar. */
@Component({
  selector: 'app-mock-exams',
  standalone: true,
  imports: [FormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, TranslocoDirective],
  providers: [provideTranslocoScope('mock-exams')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <div class="me-root" *transloco="let t; prefix: 'mock-exams'">
      <h1 class="me-title">{{ t('list.title') }}</h1>
      <p class="me-note">{{ t('list.subtitle') }}</p>

      <!-- #870: önce şablon kartı; seçilince oturumlar ve ders başına soru sayıları görünür. -->
      <section class="me-new" [attr.aria-label]="t('list.newTitle')">
        <h2 class="me-section-title">{{ t('list.newTitle') }}</h2>
        <p class="me-note">{{ t('list.pickTemplate') }}</p>
        <div class="me-templates" role="radiogroup" [attr.aria-label]="t('list.template')">
          @for (template of templates(); track template.id) {
            <button type="button" class="me-template" role="radio" [attr.aria-checked]="templateId() === template.id"
              [class.me-template--on]="templateId() === template.id" (click)="templateId.set(template.id ?? null)"
              data-testid="me-template-card">
              <b>{{ template.name }}</b>
              <span class="me-note">{{ t('list.templateSummary', { questions: questionTotal(template), sessions: template.sessions.length }) }}</span>
              <span class="me-note">{{ template.wrongAnswersPerCorrect > 0 ? t('list.penalty', { n: template.wrongAnswersPerCorrect }) : t('list.noPenalty') }}</span>
            </button>
          }
        </div>

        @if (selectedTemplate(); as template) {
          <mat-card class="me-card">
            <mat-card-content>
              <div class="me-grid">
                <mat-form-field appearance="outline">
                  <mat-label>{{ t('list.name') }}</mat-label>
                  <input matInput maxlength="150" [ngModel]="name()" (ngModelChange)="name.set($event)" autocomplete="off" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>{{ t('list.grade') }}</mat-label>
                  <mat-select [value]="gradeId()" (selectionChange)="gradeId.set($event.value)">
                    @for (grade of grades(); track grade.id) {
                      <mat-option [value]="grade.id">{{ grade.name }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>
              </div>
              <ol class="me-sessions" data-testid="me-template-sessions">
                @for (session of template.sessions; track $index; let i = $index) {
                  <li>
                    <b>{{ t('list.session', { n: i + 1, name: session.name, minutes: minutes(session.durationSeconds) }) }}</b>
                    <span class="me-note">
                      @for (section of session.sections; track $index; let last = $last) {
                        {{ section.name }} 0/{{ section.questionCount }}@if (!last) { · }
                      }
                    </span>
                  </li>
                }
              </ol>
              <p class="me-note">{{ t('list.fillLater') }}</p>
              @if (createFailed()) {
                <div class="me-error" role="alert">{{ t('list.createFailed') }}</div>
              }
            </mat-card-content>
            <mat-card-actions class="me-actions">
              @if (missingField(); as field) {
                <span class="me-note me-warning" id="me-missing" data-testid="me-missing">{{ t('list.missing.' + field) }}</span>
              }
              <button class="me-create" mat-raised-button color="primary" (click)="create()" [disabled]="!canCreate() || creating()"
                [attr.aria-describedby]="missingField() ? 'me-missing' : null">
                <mat-icon>add</mat-icon>
                {{ t('list.create') }}
              </button>
            </mat-card-actions>
          </mat-card>
        }
      </section>

      <h2 class="me-section-title">{{ t('list.mine') }}</h2>
      @if (loadFailed()) {
        <div class="me-error" role="alert">
          {{ t('list.loadFailed') }}
          <button mat-button (click)="load()">{{ t('retry') }}</button>
        </div>
      } @else if (!loading() && exams().length === 0) {
        <p class="me-note" data-testid="me-empty">{{ t('list.empty') }}</p>
      }

      @for (exam of exams(); track exam.id) {
        <a class="me-row" [routerLink]="['/mock-exams', exam.id]">
          <span><b>{{ exam.name }}</b> · {{ exam.templateName }} · {{ exam.gradeName }}</span>
          <span class="me-note">{{ exam.publishedAt ? t('published') : t('draft') }}</span>
        </a>
      }
    </div>
  `,
})
export class MockExamsComponent {
  private readonly service = inject(MockExamService);
  private readonly gradesService = inject(GradesService);
  private readonly router = inject(Router);

  readonly templates = signal<ExamTemplate[]>([]);
  readonly grades = signal<Grade[]>([]);
  readonly exams = signal<MockExamListItem[]>([]);
  readonly templateId = signal<number | null>(null);
  readonly gradeId = signal<number | null>(null);
  readonly name = signal('');
  readonly creating = signal(false);
  readonly createFailed = signal(false);
  readonly loadFailed = signal(false);
  readonly loading = signal(true);

  constructor() {
    this.load();
  }

  /** Şablon ya da sınıf gelmezse deneme kurulamaz: üçü de hata gösterir ve yeniden denenir. */
  load(): void {
    this.loadFailed.set(false);
    this.loading.set(true);
    const failed = () => this.loadFailed.set(true);
    this.service.listTemplates().subscribe({ next: (t) => this.templates.set(t ?? []), error: failed });
    this.gradesService.getGrades().subscribe({ next: (g) => this.grades.set(g ?? []), error: failed });
    this.service.listMine().subscribe({
      next: (e) => {
        this.exams.set(e ?? []);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        failed();
      },
    });
  }

  readonly selectedTemplate = computed(() => this.templates().find((t) => t.id === this.templateId()) ?? null);

  /** #870: "Deneme oluştur" kapalıyken hangi alanın eksik olduğu (sırayla ad, sınıf). */
  missingField(): 'name' | 'grade' | null {
    if (this.name().trim().length === 0) return 'name';
    if (this.gradeId() === null) return 'grade';
    return null;
  }

  questionTotal(template: ExamTemplate): number {
    return template.sessions.reduce((sum, s) => sum + s.sections.reduce((n, sec) => n + sec.questionCount, 0), 0);
  }

  minutes(seconds: number): number {
    return Math.round(seconds / 60);
  }

  canCreate(): boolean {
    return this.templateId() !== null && this.gradeId() !== null && this.name().trim().length > 0;
  }

  create(): void {
    const templateId = this.templateId();
    const gradeId = this.gradeId();
    if (templateId === null || gradeId === null || !this.canCreate() || this.creating()) return;

    this.creating.set(true);
    this.createFailed.set(false);
    this.service.create(templateId, this.name().trim(), gradeId).subscribe({
      next: (exam) => {
        this.creating.set(false);
        void this.router.navigate(['/mock-exams', exam.id]);
      },
      error: (_: HttpErrorResponse) => {
        this.creating.set(false);
        this.createFailed.set(true);
      },
    });
  }
}

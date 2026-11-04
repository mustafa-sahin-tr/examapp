import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { ExamTemplate, MockExamService } from '../../services/mock-exam.service';

const emptyTemplate = (): ExamTemplate => ({
  code: '',
  name: '',
  wrongAnswersPerCorrect: 4,
  sourceReference: '',
  sessions: [{ name: '', durationSeconds: 3600, sections: [{ name: '', questionCount: 10 }] }],
});

/** Sunucudaki WorksheetDurationLimits.MaxSeconds. */
const MAX_DURATION_SECONDS = 86_400;

const wholeBetween = (value: number, min: number, max: number): boolean => Number.isInteger(value) && value >= min && value <= max;

/** Issue #693: admin sınav şablonlarını tanımlar. Deneme kurulmuş şablon değişmez: sonuçlar bu yapıya bağlıdır. */
@Component({
  selector: 'app-exam-templates',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslocoDirective],
  providers: [provideTranslocoScope('mock-exams')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <div class="me-root" *transloco="let t; prefix: 'mock-exams'">
      <h1 class="me-title">{{ t('templates.title') }}</h1>
      <p class="me-note">{{ t('templates.subtitle') }}</p>

      @if (loadFailed()) {
        <div class="me-error" role="alert">{{ t('templates.loadFailed') }}</div>
      }

      @for (template of templates(); track template.id) {
        <div class="me-row">
          <span>
            <b>{{ template.name }}</b>
            <span class="me-note"> · {{ t('templates.summary', { questions: questionCount(template), sessions: template.sessions.length, penalty: template.wrongAnswersPerCorrect }) }}</span>
            @if (template.sourceReference) {
              <div class="me-note">{{ template.sourceReference }}</div>
            }
          </span>
          @if (template.inUse) {
            <span class="me-note">{{ t('templates.inUse') }}</span>
          } @else {
            <button mat-button class="me-edit" (click)="edit(template)">{{ t('templates.edit') }}</button>
          }
        </div>
      }

      @if (draft(); as form) {
        <mat-card class="me-card">
          <mat-card-content>
            <div class="me-grid">
              <mat-form-field appearance="outline">
                <mat-label>{{ t('templates.code') }}</mat-label>
                <input matInput maxlength="50" [ngModel]="form.code" (ngModelChange)="change({ code: $event })" autocomplete="off" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('templates.name') }}</mat-label>
                <input matInput maxlength="100" [ngModel]="form.name" (ngModelChange)="change({ name: $event })" autocomplete="off" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('templates.penalty') }}</mat-label>
                <input matInput type="number" min="0" max="10" [ngModel]="form.wrongAnswersPerCorrect" (ngModelChange)="change({ wrongAnswersPerCorrect: toNumber($event) })" />
                <mat-hint>{{ t('templates.penaltyHint') }}</mat-hint>
              </mat-form-field>
            </div>
            <mat-form-field appearance="outline" class="me-field">
              <mat-label>{{ t('templates.source') }}</mat-label>
              <input matInput maxlength="300" [ngModel]="form.sourceReference" (ngModelChange)="change({ sourceReference: $event })" autocomplete="off" />
            </mat-form-field>

            @for (session of form.sessions; track $index; let s = $index) {
              <div class="me-section">
                <div class="me-grid">
                  <mat-form-field appearance="outline">
                    <mat-label>{{ t('templates.sessionName') }}</mat-label>
                    <input matInput maxlength="100" [ngModel]="session.name" (ngModelChange)="changeSession(s, { name: $event })" autocomplete="off" />
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>{{ t('templates.sessionMinutes') }}</mat-label>
                    <input matInput type="number" min="1" [ngModel]="session.durationSeconds / 60" (ngModelChange)="changeSession(s, { durationSeconds: toNumber($event) * 60 })" />
                  </mat-form-field>
                  @if (form.sessions.length > 1) {
                    <button mat-button class="me-remove-session" (click)="removeSession(s)">{{ t('templates.removeSession') }}</button>
                  }
                </div>
                @for (section of session.sections; track $index; let x = $index) {
                  <div class="me-grid">
                    <mat-form-field appearance="outline">
                      <mat-label>{{ t('templates.group') }}</mat-label>
                      <input matInput maxlength="100" [ngModel]="section.groupName" (ngModelChange)="changeSection(s, x, { groupName: $event })" autocomplete="off" />
                    </mat-form-field>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ t('templates.subject') }}</mat-label>
                      <input matInput maxlength="100" [ngModel]="section.name" (ngModelChange)="changeSection(s, x, { name: $event })" autocomplete="off" />
                    </mat-form-field>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ t('templates.questionCount') }}</mat-label>
                      <input matInput type="number" min="1" max="200" [ngModel]="section.questionCount" (ngModelChange)="changeSection(s, x, { questionCount: toNumber($event) })" />
                    </mat-form-field>
                    @if (session.sections.length > 1) {
                      <button mat-button class="me-remove-section" (click)="removeSection(s, x)">{{ t('templates.removeSubject') }}</button>
                    }
                  </div>
                }
                <button mat-button class="me-add-section" (click)="addSection(s)" [disabled]="session.sections.length >= 30">{{ t('templates.addSubject') }}</button>
              </div>
            }
            <button mat-button class="me-add-session" (click)="addSession()" [disabled]="form.sessions.length >= 10">{{ t('templates.addSession') }}</button>

            @if (!canSave()) {
              <p class="me-note me-incomplete">{{ t('templates.incomplete') }}</p>
            }
            @if (errorKey(); as key) {
              <div class="me-error" role="alert">{{ t('errors.' + key) }}</div>
            }
          </mat-card-content>
          <mat-card-actions class="me-actions">
            <button mat-button (click)="draft.set(null)">{{ t('builder.cancel') }}</button>
            <button class="me-save" mat-raised-button color="primary" (click)="save()" [disabled]="!canSave() || saving()">{{ t('templates.save') }}</button>
          </mat-card-actions>
        </mat-card>
      } @else {
        <div class="me-actions">
          <button class="me-new" mat-stroked-button (click)="startNew()">
            <mat-icon>add</mat-icon>
            {{ t('templates.new') }}
          </button>
        </div>
      }
    </div>
  `,
})
export class ExamTemplatesComponent {
  private readonly service = inject(MockExamService);

  readonly templates = signal<ExamTemplate[]>([]);
  readonly loadFailed = signal(false);
  readonly draft = signal<ExamTemplate | null>(null);
  readonly saving = signal(false);
  readonly errorKey = signal<string | null>(null);
  readonly newTemplate = emptyTemplate;

  readonly canSave = computed(() => {
    const form = this.draft();
    return !!form
      && form.code.trim().length > 0
      && form.name.trim().length > 0
      && wholeBetween(form.wrongAnswersPerCorrect, 0, 10)
      && form.sessions.length > 0
      && form.sessions.length <= 10
      && form.sessions.every((s) => s.name.trim().length > 0 && wholeBetween(s.durationSeconds, 60, MAX_DURATION_SECONDS) && s.sections.length > 0 && s.sections.length <= 30
        && s.sections.every((x) => x.name.trim().length > 0 && wholeBetween(x.questionCount, 1, 200)));
  });

  constructor() {
    this.load();
  }

  private load(): void {
    this.service.listTemplates().subscribe({ next: (t) => this.templates.set(t ?? []), error: () => this.loadFailed.set(true) });
  }

  questionCount(template: ExamTemplate): number {
    return template.sessions.flatMap((s) => s.sections).reduce((sum, s) => sum + s.questionCount, 0);
  }

  /** Boş kutu 0 sayılmaz: yanlış cezası sessizce kapanırdı. */
  toNumber(value: number | string | null): number {
    return value === null || value === '' ? Number.NaN : Number(value);
  }

  startNew(): void {
    this.errorKey.set(null);
    this.draft.set(this.newTemplate());
  }

  edit(template: ExamTemplate): void {
    this.errorKey.set(null);
    this.draft.set(structuredClone(template));
  }

  change(patch: Partial<ExamTemplate>): void {
    this.draft.update((d) => (d ? { ...d, ...patch } : d));
  }

  changeSession(index: number, patch: Partial<ExamTemplate['sessions'][number]>): void {
    this.draft.update((d) => d && { ...d, sessions: d.sessions.map((s, i) => (i === index ? { ...s, ...patch } : s)) });
  }

  changeSection(session: number, index: number, patch: Partial<ExamTemplate['sessions'][number]['sections'][number]>): void {
    this.draft.update((d) => d && {
      ...d,
      sessions: d.sessions.map((s, i) => (i === session ? { ...s, sections: s.sections.map((x, j) => (j === index ? { ...x, ...patch } : x)) } : s)),
    });
  }

  addSection(session: number): void {
    this.draft.update((d) => d && {
      ...d,
      sessions: d.sessions.map((s, i) => (i === session ? { ...s, sections: [...s.sections, { name: '', questionCount: 10 }] } : s)),
    });
  }

  addSession(): void {
    this.draft.update((d) => d && { ...d, sessions: [...d.sessions, { name: '', durationSeconds: 3600, sections: [{ name: '', questionCount: 10 }] }] });
  }

  removeSection(session: number, index: number): void {
    this.draft.update((d) => d && {
      ...d,
      sessions: d.sessions.map((s, i) => (i === session ? { ...s, sections: s.sections.filter((_, j) => j !== index) } : s)),
    });
  }

  removeSession(index: number): void {
    this.draft.update((d) => d && { ...d, sessions: d.sessions.filter((_, i) => i !== index) });
  }

  save(): void {
    const form = this.draft();
    if (!form || !this.canSave() || this.saving()) return;
    this.saving.set(true);
    this.errorKey.set(null);
    this.service.saveTemplate(form).subscribe({
      next: () => {
        this.saving.set(false);
        this.draft.set(null);
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const code = (err.error as { errorCode?: string } | null)?.errorCode;
        this.errorKey.set(code === 'TemplateCodeTaken' || code === 'TemplateInUse' ? code : 'Failed');
      },
    });
  }
}

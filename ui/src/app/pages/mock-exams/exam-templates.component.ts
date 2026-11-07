import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TextFieldModule } from '@angular/cdk/text-field';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { ExamTemplate, ExamTemplateSession, MockExamService } from '../../services/mock-exam.service';

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

/**
 * Issue #693: admin sınav şablonlarını tanımlar. Deneme kurulmuş şablon değişmez: sonuçlar bu yapıya bağlıdır.
 * #899: liste solda, seçilen şablon sağdaki panelde; kilitli şablon salt okunur, soru toplamı kayıtlıyla karşılaştırılır.
 */
@Component({
  selector: 'app-exam-templates',
  standalone: true,
  imports: [FormsModule, TextFieldModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslocoDirective],
  providers: [provideTranslocoScope('mock-exams')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <div class="me-root me-root--wide" *transloco="let t; prefix: 'mock-exams'">
      <h1 class="me-title">{{ t('templates.title') }}</h1>
      <p class="me-note">{{ t('templates.subtitle') }}</p>

      @if (loadFailed()) {
        <div class="me-error" role="alert">{{ t('templates.loadFailed') }}</div>
      }

      <div class="et-layout">
        <div class="et-list">
          @for (template of templates(); track template.id) {
            <button
              type="button"
              class="et-item"
              [class.et-item--selected]="selectedId() === template.id"
              [attr.aria-current]="selectedId() === template.id ? 'true' : null"
              (click)="edit(template)"
            >
              <span class="et-item-main">
                <b>{{ template.name }}</b>
                <span class="me-note">{{ t('templates.listSummary', { questions: questionCount(template), sessions: template.sessions.length }) }}</span>
              </span>
              @if (template.inUse) {
                <span class="et-lock"><mat-icon aria-hidden="true">lock</mat-icon>{{ t('templates.locked') }}</span>
              }
            </button>
          }
          <button class="me-new" mat-stroked-button (click)="startNew()" [disabled]="selectedId() === 'new'">
            <mat-icon>add</mat-icon>
            {{ t('templates.new') }}
          </button>
        </div>

        @if (locked(); as view) {
          <mat-card class="me-card et-panel" data-test="template-readonly">
            <mat-card-content>
              <div class="et-panel-head">
                <h2 class="et-panel-title">{{ view.name }}</h2>
                <span class="me-note">{{ t('templates.penaltyValue', { penalty: view.wrongAnswersPerCorrect }) }}</span>
              </div>
              <p class="me-note et-lock-note"><mat-icon aria-hidden="true">lock</mat-icon>{{ t('templates.usedBy', { count: view.mockExamCount ?? 0 }) }}</p>
              @if (view.sourceReference) {
                <p class="me-note">{{ t('templates.source') }}: {{ view.sourceReference }}</p>
              }
              @for (session of view.sessions; track $index) {
                <div class="me-section">
                  <h3 class="et-session-title">{{ session.name }} · {{ t('templates.minutes', { minutes: session.durationSeconds / 60 }) }}</h3>
                  <table class="me-table et-table">
                    <thead>
                      <tr>
                        <th scope="col">{{ t('templates.subject') }}</th>
                        <th scope="col">{{ t('templates.groupColumn') }}</th>
                        <th scope="col">{{ t('templates.questionColumn') }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (section of session.sections; track $index) {
                        <tr>
                          <th scope="row">{{ section.name }}</th>
                          <td>{{ section.groupName || '–' }}</td>
                          <td>{{ section.questionCount }}</td>
                        </tr>
                      }
                    </tbody>
                    <tfoot>
                      <tr class="me-total">
                        <th scope="row" colspan="2">{{ t('templates.sessionTotal') }}</th>
                        <td>{{ sessionCount(session) }}</td>
                      </tr>
                    </tfoot>
                  </table>
                </div>
              }
              <p class="et-total" data-test="template-total">{{ t('templates.total', { count: questionCount(view) }) }}</p>
            </mat-card-content>
            <mat-card-actions class="me-actions">
              <button mat-button (click)="close()">{{ t('templates.close') }}</button>
            </mat-card-actions>
          </mat-card>
        } @else if (draft(); as form) {
          <mat-card class="me-card et-panel" data-test="template-form">
            <mat-card-content>
              <h2 class="et-panel-title">{{ form.id ? form.name || t('templates.edit') : t('templates.new') }}</h2>
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
                <div class="me-section et-session">
                  <div class="et-session-row">
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
                    <div class="et-section-row">
                      <mat-form-field appearance="outline" subscriptSizing="dynamic">
                        <mat-label>{{ t('templates.subject') }}</mat-label>
                        <textarea
                          matInput
                          cdkTextareaAutosize
                          cdkAutosizeMinRows="1"
                          rows="1"
                          maxlength="100"
                          [ngModel]="section.name"
                          (ngModelChange)="changeSection(s, x, { name: $event })"
                          (keydown.enter)="$event.preventDefault()"
                        ></textarea>
                      </mat-form-field>
                      <mat-form-field appearance="outline" subscriptSizing="dynamic">
                        <mat-label>{{ t('templates.group') }}</mat-label>
                        <textarea
                          matInput
                          cdkTextareaAutosize
                          cdkAutosizeMinRows="1"
                          rows="1"
                          maxlength="100"
                          [ngModel]="section.groupName"
                          (ngModelChange)="changeSection(s, x, { groupName: $event })"
                          (keydown.enter)="$event.preventDefault()"
                        ></textarea>
                      </mat-form-field>
                      <mat-form-field appearance="outline" subscriptSizing="dynamic">
                        <mat-label>{{ t('templates.questionColumn') }}</mat-label>
                        <input matInput type="number" min="1" max="200" [ngModel]="section.questionCount" (ngModelChange)="changeSection(s, x, { questionCount: toNumber($event) })" />
                      </mat-form-field>
                      @if (session.sections.length > 1) {
                        <button mat-icon-button class="me-remove-section" [attr.aria-label]="t('templates.removeSubject')" (click)="removeSection(s, x)">
                          <mat-icon>close</mat-icon>
                        </button>
                      }
                    </div>
                  }
                  <div class="et-session-foot">
                    <button mat-button class="me-add-section" (click)="addSection(s)" [disabled]="session.sections.length >= 30">{{ t('templates.addSubject') }}</button>
                    <span class="me-note" data-test="session-total">{{ t('templates.sessionTotalValue', { count: sessionCount(session) }) }}</span>
                  </div>
                </div>
              }
              <button mat-button class="me-add-session" (click)="addSession()" [disabled]="form.sessions.length >= 10">{{ t('templates.addSession') }}</button>

              @if (savedTotal(); as saved) {
                <p class="et-total" [class.me-warning]="draftTotal() !== saved" data-test="template-total">
                  {{ t('templates.totalOf', { count: draftTotal(), saved: saved }) }}
                  @if (draftTotal() === saved) {
                    <mat-icon aria-hidden="true">check</mat-icon>
                  }
                </p>
                @if (draftTotal() !== saved) {
                  <p class="me-note me-warning" role="status" data-test="template-total-warning">{{ t('templates.totalChanged', { saved: saved }) }}</p>
                }
              } @else {
                <p class="et-total" data-test="template-total">{{ t('templates.total', { count: draftTotal() }) }}</p>
              }

              @if (!canSave()) {
                <p class="me-note me-incomplete">{{ t('templates.incomplete') }}</p>
              }
              @if (errorKey(); as key) {
                <div class="me-error" role="alert">{{ t('errors.' + key) }}</div>
              }
            </mat-card-content>
            <mat-card-actions class="me-actions">
              <button mat-button (click)="close()">{{ t('builder.cancel') }}</button>
              <button class="me-save" mat-raised-button color="primary" (click)="save()" [disabled]="!canSave() || saving()">{{ t('templates.save') }}</button>
            </mat-card-actions>
          </mat-card>
        } @else if (templates().length > 0) {
          <p class="me-note et-hint">{{ t('templates.pick') }}</p>
        }
      </div>
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
  /** Seçili satır: şablon kimliği ya da yeni şablon. */
  readonly selectedId = signal<number | 'new' | null>(null);

  /** Kilitli şablon salt okunur açılır: deneme kurulmuş yapının sonuçları değişmesin. */
  readonly locked = computed(() => {
    const id = this.selectedId();
    const template = typeof id === 'number' ? this.templates().find((t) => t.id === id) : undefined;
    return template?.inUse ? template : null;
  });

  readonly draftTotal = computed(() => {
    const form = this.draft();
    return form ? this.questionCount(form) : 0;
  });

  /** Düzenlenen şablonun kayıtlı soru sayısı; toplam değişince uyarı verilir. Yeni şablonda yok. */
  readonly savedTotal = computed(() => {
    const id = this.selectedId();
    const template = typeof id === 'number' ? this.templates().find((t) => t.id === id) : undefined;
    return template ? this.questionCount(template) : null;
  });

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
    return template.sessions.reduce((sum, s) => sum + this.sessionCount(s), 0);
  }

  /** Boş ya da yarım sayı toplamı bozmasın: yalnız geçerli sayılar toplanır. */
  sessionCount(session: ExamTemplateSession): number {
    return session.sections.reduce((sum, x) => sum + (Number.isFinite(x.questionCount) ? x.questionCount : 0), 0);
  }

  /** Boş kutu 0 sayılmaz: yanlış cezası sessizce kapanırdı. */
  toNumber(value: number | string | null): number {
    return value === null || value === '' ? Number.NaN : Number(value);
  }

  startNew(): void {
    this.errorKey.set(null);
    this.selectedId.set('new');
    this.draft.set(this.newTemplate());
  }

  edit(template: ExamTemplate): void {
    this.errorKey.set(null);
    this.selectedId.set(template.id ?? null);
    this.draft.set(template.inUse ? null : structuredClone(template));
  }

  close(): void {
    this.selectedId.set(null);
    this.draft.set(null);
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
        this.close();
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

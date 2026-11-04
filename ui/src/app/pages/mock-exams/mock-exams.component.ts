import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
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

/** Issue #693: öğretmenin denemeleri ve yeni deneme. */
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

      <mat-card class="me-card">
        <mat-card-content>
          <div class="me-grid">
            <mat-form-field appearance="outline">
              <mat-label>{{ t('list.template') }}</mat-label>
              <mat-select [value]="templateId()" (selectionChange)="templateId.set($event.value)">
                @for (template of templates(); track template.id) {
                  <mat-option [value]="template.id">{{ template.name }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ t('list.grade') }}</mat-label>
              <mat-select [value]="gradeId()" (selectionChange)="gradeId.set($event.value)">
                @for (grade of grades(); track grade.id) {
                  <mat-option [value]="grade.id">{{ grade.name }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ t('list.name') }}</mat-label>
              <input matInput maxlength="150" [ngModel]="name()" (ngModelChange)="name.set($event)" autocomplete="off" />
            </mat-form-field>
          </div>
          @if (createFailed()) {
            <div class="me-error" role="alert">{{ t('list.createFailed') }}</div>
          }
        </mat-card-content>
        <mat-card-actions class="me-actions">
          <button class="me-create" mat-raised-button color="primary" (click)="create()" [disabled]="!canCreate() || creating()">
            <mat-icon>add</mat-icon>
            {{ t('list.create') }}
          </button>
        </mat-card-actions>
      </mat-card>

      @if (loadFailed()) {
        <div class="me-error" role="alert">
          {{ t('list.loadFailed') }}
          <button mat-button (click)="load()">{{ t('retry') }}</button>
        </div>
      } @else if (!loading() && exams().length === 0) {
        <p class="me-note">{{ t('list.empty') }}</p>
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

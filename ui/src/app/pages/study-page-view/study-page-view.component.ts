import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { StudyPage, StudyPageBlockType } from '../../models/study-page';
import { PracticeSessionStartRequest } from '../../models/practice';
import { AuthService } from '../../services/auth.service';
import { PracticeService } from '../../services/practice.service';
import { StudyPageService } from '../../services/study-page.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StudyItemDisplay, describeStudyItem } from '../../shared/utils/study-item-display.util';
import { StudyPageBlocksViewComponent } from '../study-pages/study-page-blocks-view.component';

/**
 * Issue #933: çalışma sayfasının öğrenci görünümü — ana içerik (görsel seti / bağlantı / kitap aralığı), açıklama,
 * sonra bloklar sırayla. Soru bloğu sayfanın sorularından pratik oturumu açar (yalnız öğrenci rolü).
 */
@Component({
  selector: 'app-study-page-view',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    PageHeaderComponent,
    StudyPageBlocksViewComponent,
  ],
  providers: [provideTranslocoScope('study-pages')],
  templateUrl: './study-page-view.component.html',
  styleUrl: './study-page-view.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StudyPageViewComponent implements OnInit {
  private readonly studyPages = inject(StudyPageService);
  private readonly practice = inject(PracticeService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly pageId = Number(this.route.snapshot.paramMap.get('id'));

  readonly page = signal<StudyPage | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  /** Açılan oturumun sorusu; `0` tüm sayfa. */
  readonly solving = signal<number | null>(null);

  readonly isStudent = this.auth.hasRealmRole('Student');

  readonly display = computed<StudyItemDisplay | null>(() => {
    const page = this.page();
    return page ? describeStudyItem(page, (key, params) => this.transloco.translate<string>(key, params) ?? '') : null;
  });

  readonly questionCount = computed(
    () => (this.page()?.blocks ?? []).filter((b) => b.type === StudyPageBlockType.Question && b.questionId != null).length,
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    if (!Number.isFinite(this.pageId) || this.pageId <= 0) {
      this.loading.set(false);
      this.loadFailed.set(true);
      return;
    }
    this.loading.set(true);
    this.loadFailed.set(false);
    this.studyPages
      .getById(this.pageId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.page.set(page);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.loadFailed.set(true);
        },
      });
  }

  /** `questionId` null: sayfadaki tüm sorular, blok sırasıyla. */
  solve(questionId: number | null): void {
    if (!this.isStudent || this.solving() !== null) return;
    this.solving.set(questionId ?? 0);
    const request: PracticeSessionStartRequest = { studyItemId: this.pageId };
    if (questionId != null) request.studyQuestionId = questionId;
    this.practice
      .startSession(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => void this.router.navigate(['/practice'], { queryParams: { session: session.id } }),
        error: () => {
          this.solving.set(null);
          this.snackBar.open(this.transloco.translate('study-pages.blocks.page.solveFailed'), undefined, { duration: 3000 });
        },
      });
  }
}

import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { ASK_TEACHER_ROUTE, AskTeacherQuestion } from '../../../models/ask-teacher.model';
import { AskTeacherService } from '../../../services/ask-teacher.service';
import { AskTeacherImageComponent } from '../ask-teacher-image/ask-teacher-image.component';

/**
 * Issue #1111: yanlışlar defterinin "Öğretmene sorduklarım" bölümü — öğrencinin deftere kaydettiği cevaplanmış sorular (fotoğraf +
 * öğretmenin cevabı). Kayıt yoksa ya da yüklenemezse hiçbir şey çizilmez (defterin kendisini etkilemez).
 */
@Component({
  selector: 'app-ask-teacher-notebook',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoDirective, AskTeacherImageComponent],
  providers: [provideTranslocoScope('ask-teacher')],
  templateUrl: './ask-teacher-notebook.component.html',
  styleUrl: './ask-teacher-notebook.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AskTeacherNotebookComponent implements OnInit {
  private readonly service = inject(AskTeacherService);
  private readonly destroyRef = inject(DestroyRef);

  readonly route = ASK_TEACHER_ROUTE;
  readonly items = signal<AskTeacherQuestion[]>([]);

  ngOnInit(): void {
    this.service
      .notebook()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (items) => this.items.set(items), error: () => this.items.set([]) });
  }

  remove(question: AskTeacherQuestion): void {
    this.service
      .setNotebook(question.id, false)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.items.update((list) => list.filter((q) => q.id !== question.id)) });
  }
}

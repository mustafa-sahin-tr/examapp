import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { CoachStudent } from '../../models/coach.model';
import { CoachService } from '../../services/coach.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/** Issue #700: koçun öğrencileri (kendi okulu + onaylı özel ders). Sıra ve kapsam sunucudan gelir. */
@Component({
  selector: 'app-coach-students',
  standalone: true,
  imports: [
    PageHeaderComponent,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('coach')],
  templateUrl: './coach-students.component.html',
  styleUrl: './coach-students.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoachStudentsComponent implements OnInit {
  private readonly coach = inject(CoachService);
  private readonly destroyRef = inject(DestroyRef);

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly students = signal<CoachStudent[]>([]);
  readonly query = signal('');

  readonly filtered = computed(() => {
    const query = this.query().trim().toLocaleLowerCase();
    return query ? this.students().filter((s) => s.fullName.toLocaleLowerCase().includes(query)) : this.students();
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.coach
      .listStudents()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (students) => {
          this.students.set(students);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }
}

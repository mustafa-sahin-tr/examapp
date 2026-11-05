import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { OutcomeMasteryCardComponent } from '../../shared/components/outcome-mastery-card/outcome-mastery-card.component';

/**
 * Issue #696: öğretmenin bir öğrencinin kazanım haritasına baktığı sayfa. Yetki sunucuda (kendi okulu ya da onaylı özel ders).
 * Ad gezinme durumundan gelir, adrese yazılmaz; sayfa yenilenince başlık genel kalır.
 */
@Component({
  selector: 'app-teacher-student-outcomes',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, OutcomeMasteryCardComponent, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope('outcome-mastery')],
  template: `
    <section class="tso" *transloco="let t; prefix: 'outcome-mastery'">
      <a mat-button routerLink="/dashboard"><mat-icon aria-hidden="true">arrow_back</mat-icon>{{ t('page.back') }}</a>
      <h1 class="tso__title" data-test="teacher-outcomes-title">
        {{ studentName ? t('page.titleNamed', { name: studentName }) : t('page.title') }}
      </h1>
      <app-outcome-mastery-card [teacherStudentId]="studentId" />
    </section>
  `,
  styles: `
    .tso {
      max-width: 60rem;
      margin: 0 auto;
      padding: 1rem;
    }

    .tso__title {
      margin: 0.5rem 0 0;
      font-size: 1.5rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class TeacherStudentOutcomesComponent {
  protected readonly studentId = Number(inject(ActivatedRoute).snapshot.paramMap.get('studentId'));

  protected readonly studentName: string | null =
    (inject(Router).currentNavigation()?.extras.state?.['studentName'] as string | undefined) ?? null;
}

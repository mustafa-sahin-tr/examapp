import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import coachTr from '../../../../public/i18n/coach/tr.json';
import { CoachStudent } from '../../models/coach.model';
import { CoachService } from '../../services/coach.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { CoachStudentsComponent } from './coach-students.component';

const students: CoachStudent[] = [
  { id: 3, fullName: 'Ada Yılmaz', gradeId: 8, relationship: 'School' },
  { id: 5, fullName: 'Can Demir', gradeId: null, relationship: 'PrivateLesson' },
];

describe('CoachStudentsComponent (issue #700)', () => {
  function create(list = of(students)): ComponentFixture<CoachStudentsComponent> {
    TestBed.configureTestingModule({
      imports: [CoachStudentsComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'coach/tr': coachTr } })],
      providers: [provideRouter([]), { provide: CoachService, useValue: { listStudents: () => list } }],
    });
    const fixture = TestBed.createComponent(CoachStudentsComponent);
    fixture.detectChanges();
    return fixture;
  }

  const all = (fixture: ComponentFixture<CoachStudentsComponent>, test: string): HTMLElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll(`[data-test="${test}"]`));
  const text = (el: Element | undefined) => el?.textContent?.trim() ?? '';

  it('lists_StudentsWithRelationship_AndLinksToTheirPage', () => {
    const fixture = create();

    expect(all(fixture, 'coach-student-name').map(text)).toEqual(['Ada Yılmaz', 'Can Demir']);
    expect(all(fixture, 'coach-student-relationship').map(text)).toEqual(['Okul', 'Özel ders']);
    expect(all(fixture, 'coach-student-open')[1].getAttribute('href')).toBe('/teacher/coach/5');
  });

  it('search_FiltersByName_IgnoringCase', () => {
    const fixture = create();
    const input = all(fixture, 'coach-search')[0] as HTMLInputElement;

    input.value = 'can';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(all(fixture, 'coach-student-name').map(text)).toEqual(['Can Demir']);

    input.value = 'zz';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(all(fixture, 'coach-students-no-match').length).toBe(1);
  });

  it('empty_ShowsEmptyState', () => {
    expect(all(create(of([])), 'coach-students-empty').length).toBe(1);
  });

  it('error_ShowsRetry', () => {
    const fixture = create(throwError(() => new Error('down')));
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain('Öğrenciler yüklenemedi.');
  });
});

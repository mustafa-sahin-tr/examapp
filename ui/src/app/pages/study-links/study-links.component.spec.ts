import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRouteSnapshot, CanActivateFn, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { StudyLinksComponent } from './study-links.component';
import { SubjectService } from '../../services/subject.service';
import { GradesService } from '../../services/grades.service';
import { StudyLinkService } from '../../services/study-link.service';
import { AuthService } from '../../services/auth.service';
import { TopicStudyLinkManagerComponent } from '../../shared/components/topic-study-link-manager/topic-study-link-manager.component';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { routes } from '../../app.routes';
import { authGuard } from '../../shared/guards/auth.guard';
import { approvedTeacherGuard } from '../../shared/guards/approved-teacher.guard';
import studyLinksTr from '../../../../public/i18n/study-links/tr.json';
import { Topic } from '../../models/topic';

describe('StudyLinksComponent (issue #61, teacher page)', () => {
  const texts = studyLinksTr.page;
  let fixture: ComponentFixture<StudyLinksComponent>;
  let component: StudyLinksComponent;
  let subjectService: jasmine.SpyObj<SubjectService>;
  let studyLinkService: jasmine.SpyObj<StudyLinkService>;

  const topics: Topic[] = [
    { id: 21, name: 'Türev', subjectId: 1, gradeId: 11 },
    { id: 20, name: 'Kesirler', subjectId: 1, gradeId: 5 },
  ];

  function configure(): void {
    subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', [
      'loadCategories',
      'getTopicsBySubject',
      'getSubTopicsByTopic',
    ]);
    subjectService.loadCategories.and.returnValue(of([{ id: 1, name: 'Matematik' }, { id: 2, name: 'Fizik' }]));
    subjectService.getTopicsBySubject.and.returnValue(of(topics));
    subjectService.getSubTopicsByTopic.and.returnValue(of([{ id: 200, name: 'Basit Kesirler', topicId: 20 }]));

    studyLinkService = jasmine.createSpyObj<StudyLinkService>('StudyLinkService', ['list']);
    studyLinkService.list.and.returnValue(of({ success: true, items: [], totalCount: 0, activeCount: 0, maxActiveLinks: 7 }));

    TestBed.configureTestingModule({
      imports: [StudyLinksComponent, translocoTestingModule({ langs: { 'study-links/tr': studyLinksTr } })],
      providers: [
        provideNoopAnimations(),
        { provide: SubjectService, useValue: subjectService },
        {
          provide: GradesService,
          useValue: { getGrades: () => of([{ id: 5, name: '5. Sınıf' }, { id: 11, name: '11. Sınıf' }]) },
        },
        { provide: StudyLinkService, useValue: studyLinkService },
        // Çalışma linki paneli sahiplik kararı için AuthService okur (HTTP'ye çıkmasın).
        { provide: AuthService, useValue: { hasRealmRole: () => false, hasRole: () => false, user: signal({ id: 1 }) } },
      ],
    });
    fixture = TestBed.createComponent(StudyLinksComponent);
    component = fixture.componentInstance;
    render();
  }

  function render(): void {
    fixture.detectChanges();
    fixture.detectChanges();
  }

  function manager(): TopicStudyLinkManagerComponent {
    return fixture.debugElement.query(By.directive(TopicStudyLinkManagerComponent)).componentInstance;
  }

  it('init_LoadsSubjectsOnly_ManagerHasNoScope', () => {
    configure();

    expect(subjectService.loadCategories).toHaveBeenCalledTimes(1);
    expect(component.subjects().items?.map((s) => s.name)).toEqual(['Matematik', 'Fizik']);
    expect(subjectService.getTopicsBySubject).not.toHaveBeenCalled();
    expect(subjectService.getSubTopicsByTopic).not.toHaveBeenCalled();
    expect(manager().scope()).toBeNull();
    expect(studyLinkService.list).not.toHaveBeenCalled();
  });

  it('selectSubject_LoadsTopicsSortedByGradeThenName', () => {
    configure();
    component.selectSubject(1);
    render();

    expect(subjectService.getTopicsBySubject).toHaveBeenCalledOnceWith(1);
    expect(component.sortedTopics().map((t) => t.id)).toEqual([20, 21]);
    expect(component.gradeName(5)).toBe('5. Sınıf');
    expect(manager().scope()).toBeNull();
  });

  it('selectTopic_LoadsSubTopicsAndManagesTopicLevelLinks', () => {
    configure();
    component.selectSubject(1);
    render();
    component.selectTopic(20);
    render();

    expect(subjectService.getSubTopicsByTopic).toHaveBeenCalledOnceWith(20);
    expect(manager().scope()).toEqual({ topicId: 20 });
    expect(manager().scopeName()).toBe('Kesirler');
    expect(studyLinkService.list).toHaveBeenCalledWith(jasmine.objectContaining({ topicId: 20 }));
  });

  it('selectSubTopic_ManagesSubTopicLinks_AndClearingReturnsToTopic', () => {
    configure();
    component.selectSubject(1);
    render();
    component.selectTopic(20);
    render();
    component.selectSubTopic(200);
    render();

    expect(manager().scope()).toEqual({ subTopicId: 200 });
    expect(manager().scopeName()).toBe('Basit Kesirler');
    expect(studyLinkService.list).toHaveBeenCalledWith(jasmine.objectContaining({ subTopicId: 200 }));

    component.selectSubTopic(null);
    render();
    expect(manager().scope()).toEqual({ topicId: 20 });
  });

  it('changingSubject_ResetsTopicAndSubTopicAndReloadsTopics', () => {
    configure();
    component.selectSubject(1);
    render();
    component.selectTopic(20);
    render();
    component.selectSubTopic(200);
    render();

    component.selectSubject(2);
    render();

    expect(component.selectedTopicId()).toBeNull();
    expect(component.selectedSubTopicId()).toBeNull();
    expect(component.subTopics().items).toBeNull();
    expect(subjectService.getTopicsBySubject).toHaveBeenCalledWith(2);
    expect(manager().scope()).toBeNull();
  });

  it('topicsLoadError_ShowsErrorAndRetryRefetches', () => {
    configure();
    subjectService.getTopicsBySubject.and.returnValue(throwError(() => new Error('boom')));
    component.selectSubject(1);
    render();

    const error = fixture.nativeElement.querySelector('[data-testid="topics-error"]') as HTMLElement;
    expect(error.textContent).toContain(texts.topicsLoadFailed);

    subjectService.getTopicsBySubject.and.returnValue(of(topics));
    (error.querySelector('button') as HTMLButtonElement).click();
    render();

    expect(subjectService.getTopicsBySubject).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[data-testid="topics-error"]')).toBeNull();
    expect(component.sortedTopics().length).toBe(2);
  });

  it('subjectsLoadError_ShowsError', () => {
    configure();
    subjectService.loadCategories.and.returnValue(throwError(() => new Error('boom')));
    component.retrySubjects();
    render();

    expect(fixture.nativeElement.querySelector('[data-testid="subjects-error"]')?.textContent).toContain(
      texts.subjectsLoadFailed
    );
  });

  describe('route', () => {
    function studyLinksRoute() {
      const layoutRoute = routes.find((r) => Array.isArray(r.children));
      return layoutRoute?.children?.find((r) => r.path === 'study-links');
    }

    function runRoleGuard(roles: string[]): boolean | UrlTree {
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          { provide: AuthService, useValue: { hasRealmRole: (role: string) => roles.includes(role) } },
        ],
      });
      const guard = studyLinksRoute()!.canActivate![1] as CanActivateFn;
      return TestBed.runInInjectionContext(
        () => guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot) as boolean | UrlTree
      );
    }

    it('studyLinksRoute_IsLazyAndGuardedByAuthThenRoleThenApprovedTeacher', () => {
      const route = studyLinksRoute();
      expect(route).withContext('route tanımı bulunamadı').toBeDefined();
      expect(route?.loadComponent).toBeDefined();
      // Issue #287: onaysız öğretmen başvuru durumu sayfasına yönlendirilir.
      expect(route?.canActivate?.length).toBe(3);
      expect(route?.canActivate?.[0]).toBe(authGuard);
      expect(route?.canActivate?.[2]).toBe(approvedTeacherGuard);
    });

    it('studyLinksRoute_TeacherAllowed', () => {
      expect(runRoleGuard(['Teacher'])).toBeTrue();
    });

    it('studyLinksRoute_StudentRedirectedToDashboard', () => {
      const result = runRoleGuard(['Student']);
      expect(result instanceof UrlTree).toBeTrue();
      expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/dashboard');
    });
  });
});

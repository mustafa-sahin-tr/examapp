import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';

import { AdminDashboardComponent } from './admin-dashboard.component';
import { AdminService } from '../../../services/admin.service';
import { AdminDashboardSummary } from '../../../models/admin-dashboard.model';
import { routes } from '../../../app.routes';
import { authGuard } from '../../../shared/guards/auth.guard';
import { adminGuard } from '../../../shared/guards/admin.guard';

describe('AdminDashboardComponent', () => {
  let fixture: ComponentFixture<AdminDashboardComponent>;
  let component: AdminDashboardComponent;
  let adminService: jasmine.SpyObj<AdminService>;

  const summary: AdminDashboardSummary = {
    teacherCount: 12,
    studentCount: 340,
    worksheetCount: 87,
    questionCount: 500,
    aiClassifiedQuestionCount: 375,
    aiClassifiedRatio: 0.75,
  };

  function configure(): ComponentFixture<AdminDashboardComponent> {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['getDashboardSummary']);
    adminService.getDashboardSummary.and.returnValue(of(summary));

    TestBed.configureTestingModule({
      imports: [AdminDashboardComponent],
      providers: [{ provide: AdminService, useValue: adminService }, provideRouter([])],
    });

    return TestBed.createComponent(AdminDashboardComponent);
  }

  // ── Route config: admin dışı kullanıcı erişemez ──────────────────────────

  it('routes_AdminDashboardPath_IsGuardedByAuthAndAdminGuard', () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const dashboardRoute = layoutRoute?.children?.find((r) => r.path === 'admin/dashboard');

    expect(dashboardRoute).withContext('route tanımı bulunamadı').toBeDefined();
    expect(dashboardRoute?.canActivate).toEqual([authGuard, adminGuard]);
  });

  // ── Loading state ─────────────────────────────────────────────────────────

  it('loadSummary_WhileRequestPending_ShowsSkeleton', () => {
    fixture = configure();
    component = fixture.componentInstance;
    const pending$ = new Subject<AdminDashboardSummary>();
    adminService.getDashboardSummary.and.returnValue(pending$.asObservable());

    fixture.detectChanges(); // ngOnInit -> loadSummary, request stays pending

    expect(component.loading()).toBeTrue();

    const skeletonCards = fixture.nativeElement.querySelectorAll('.summary-card--skeleton');
    expect(skeletonCards.length).toBeGreaterThan(0);
    expect(fixture.nativeElement.querySelectorAll('.skeleton-line--label').length).toBeGreaterThan(0);
    expect(fixture.nativeElement.querySelectorAll('.skeleton-line--value').length).toBeGreaterThan(0);
  });

  // ── Success state: 5 sayaç kartı ─────────────────────────────────────────

  it('loadSummary_SuccessfulResponse_RendersFourCounterCardsAndAiCard', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges(); // ngOnInit -> loadSummary

    expect(component.loading()).toBeFalse();
    expect(component.error()).toBeNull();
    expect(component.summary()).toEqual(summary);

    const teacherCard = fixture.nativeElement.querySelector('[data-key="teachers"] .summary-value');
    const studentCard = fixture.nativeElement.querySelector('[data-key="students"] .summary-value');
    const worksheetCard = fixture.nativeElement.querySelector('[data-key="worksheets"] .summary-value');
    const questionCard = fixture.nativeElement.querySelector('[data-key="questions"] .summary-value');
    const aiCard = fixture.nativeElement.querySelector('[data-key="ai"]');

    expect(teacherCard.textContent).toContain('12');
    expect(studentCard.textContent).toContain('340');
    expect(worksheetCard.textContent).toContain('87');
    expect(questionCard.textContent).toContain('500');
    expect(aiCard).toBeTruthy();
    expect(aiCard.querySelector('.summary-badge').textContent).toContain('75');
  });

  it('aiPercent_QuestionCountAndAiClassifiedCountGiven_ComputesRatioAsPercentage', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    expect(component.aiPercent()).toBe(75);
  });

  it('cards_SuccessfulResponse_RendersExactlyFourFlatCounterCards', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    expect(component.cards().length).toBe(4);
    expect(component.cards().map((c) => c.key)).toEqual(['teachers', 'students', 'worksheets', 'questions']);
  });

  it('loadSummary_SuccessfulResponse_RendersAiProgressBarWidthMatchingRatio', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    const bar: HTMLElement = fixture.nativeElement.querySelector('.summary-progress__bar');
    expect(bar.style.width).toBe('75%');
  });

  // ── Error state ───────────────────────────────────────────────────────────

  it('loadSummary_RequestFails_ShowsErrorStateWithRetryButton', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getDashboardSummary.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    expect(component.loading()).toBeFalse();
    expect(component.error()).toBe('Özet bilgileri alınırken bir sorun oluştu.');
    expect(component.summary()).toBeNull();

    const errorBox = fixture.nativeElement.querySelector('.state-box--error');
    expect(errorBox).toBeTruthy();
    const retryButton = errorBox.querySelector('button');
    expect(retryButton).toBeTruthy();
  });

  it('retryButtonClick_AfterError_CallsLoadSummaryAgain', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getDashboardSummary.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();
    expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(1);

    adminService.getDashboardSummary.and.returnValue(of(summary));
    const retryButton: HTMLButtonElement = fixture.nativeElement.querySelector('.state-box--error button');
    retryButton.click();
    fixture.detectChanges();

    expect(adminService.getDashboardSummary).toHaveBeenCalledTimes(2);
    expect(component.error()).toBeNull();
    expect(component.summary()).toEqual(summary);
  });

  // ── Empty state ───────────────────────────────────────────────────────────

  it('isEmpty_AllCountersZero_ShowsEmptyStateBoxAndCardsShowZero', () => {
    fixture = configure();
    component = fixture.componentInstance;
    const emptySummary: AdminDashboardSummary = {
      teacherCount: 0,
      studentCount: 0,
      worksheetCount: 0,
      questionCount: 0,
      aiClassifiedQuestionCount: 0,
      aiClassifiedRatio: 0,
    };
    adminService.getDashboardSummary.and.returnValue(of(emptySummary));

    fixture.detectChanges();

    expect(component.isEmpty()).toBeTrue();

    const teacherCard = fixture.nativeElement.querySelector('[data-key="teachers"] .summary-value');
    const studentCard = fixture.nativeElement.querySelector('[data-key="students"] .summary-value');
    const worksheetCard = fixture.nativeElement.querySelector('[data-key="worksheets"] .summary-value');
    const questionCard = fixture.nativeElement.querySelector('[data-key="questions"] .summary-value');

    expect(teacherCard.textContent).toContain('0');
    expect(studentCard.textContent).toContain('0');
    expect(worksheetCard.textContent).toContain('0');
    expect(questionCard.textContent).toContain('0');

    const emptyBox = fixture.nativeElement.querySelector('.state-box--empty');
    expect(emptyBox).toBeTruthy();
  });

  it('isEmpty_SomeCountersNonZero_IsFalseAndNoEmptyStateBox', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    expect(component.isEmpty()).toBeFalse();
    expect(fixture.nativeElement.querySelector('.state-box--empty')).toBeFalsy();
  });

  // ── AI card: soru yoksa ayırt edici boş hâl ─────────────────────────────

  it('hasQuestions_QuestionCountZero_ShowsNoQuestionsHintInsteadOfRatio', () => {
    fixture = configure();
    component = fixture.componentInstance;
    const noQuestionsSummary: AdminDashboardSummary = {
      teacherCount: 5,
      studentCount: 20,
      worksheetCount: 3,
      questionCount: 0,
      aiClassifiedQuestionCount: 0,
      aiClassifiedRatio: 0,
    };
    adminService.getDashboardSummary.and.returnValue(of(noQuestionsSummary));

    fixture.detectChanges();

    expect(component.hasQuestions()).toBeFalse();

    const aiCard = fixture.nativeElement.querySelector('[data-key="ai"]');
    const hint = aiCard.querySelector('.summary-hint');
    expect(hint).toBeTruthy();
    expect(hint.textContent).toContain('Henüz soru yok');
    expect(aiCard.querySelector('.summary-badge')).toBeFalsy();
    expect(aiCard.querySelector('.summary-progress__bar')).toBeFalsy();
  });

  it('hasQuestions_QuestionCountPositive_IsTrue', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    expect(component.hasQuestions()).toBeTrue();
  });
});

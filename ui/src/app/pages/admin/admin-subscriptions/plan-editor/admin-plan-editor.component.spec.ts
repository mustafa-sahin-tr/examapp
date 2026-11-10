import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslocoScope } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import { AdminPlanEditorComponent } from './admin-plan-editor.component';
import { SubscriptionService } from '../../../../services/subscription.service';
import { EntitlementFeature, SubscriptionPlan } from '../../../../models/subscription';
import { translocoTestingModule } from '../../../../shared/testing/transloco-testing';
import subscriptionTr from '../../../../../../public/i18n/subscription/tr.json';

const FEATURES: EntitlementFeature[] = [
  { key: 'manual-labeling', audience: 'Teacher', kind: 'Toggle', unit: null },
  { key: 'book-import', audience: 'Teacher', kind: 'Metered', unit: 'book' },
  { key: 'mock-exam', audience: 'Student', kind: 'Metered', unit: 'exam' },
  { key: 'live-lesson', audience: 'Both', kind: 'Metered', unit: 'lesson' },
];

const plan = (key: string, audience: SubscriptionPlan['audience'], limits: Record<string, number | null>): SubscriptionPlan => ({
  key,
  name: key,
  displayNameKey: `subscription.plans.${key}`,
  audience,
  monthlyPrice: 149,
  currency: 'TRY',
  trialDays: key === 'trial' ? 14 : null,
  isActive: true,
  monthlyLimits: limits,
});

const PLANS = [
  plan('teacher-pro', 'Teacher', { 'manual-labeling': null, 'book-import': 3, 'live-lesson': 0 }),
  plan('trial', 'Any', {}),
];

/** Issue #1086: admin plan düzenleyicisi — kitleye göre satırlar, yok / sınırlı / sınırsız, kayıt gövdesi ve hata. */
describe('AdminPlanEditorComponent (issue #1086)', () => {
  let fixture: ComponentFixture<AdminPlanEditorComponent>;
  let updatePlan: ReturnType<typeof vi.fn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector(`[data-testid="${id}"]`) as HTMLElement | null;

  beforeEach(() => {
    updatePlan = vi.fn().mockImplementation((key: string) => of(PLANS.find((p) => p.key === key)!));
    TestBed.configureTestingModule({
      imports: [AdminPlanEditorComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [
        provideNoopAnimations(),
        provideTranslocoScope('subscription'),
        { provide: SubscriptionService, useValue: { features: vi.fn().mockReturnValue(of(FEATURES)), updatePlan } },
      ],
    });
    fixture = TestBed.createComponent(AdminPlanEditorComponent);
    fixture.componentRef.setInput('plans', PLANS);
    fixture.componentRef.setInput('label', (key: string) => key);
    fixture.detectChanges();
  });

  it('shows only the features of the plan audience, grouped, with their current mode', () => {
    const c = fixture.componentInstance;
    expect(c.planKey()).toBe('teacher-pro');
    expect(c.groups().map((g) => g.audience)).toEqual(['Teacher', 'Both']);
    expect(c.rows().map((r) => [r.feature.key, r.mode, r.limit])).toEqual([
      ['manual-labeling', 'unlimited', null],
      ['book-import', 'limited', 3],
      ['live-lesson', 'none', null],
    ]);
    expect(q('plan-editor-row-mock-exam')).toBeNull();
    expect(q('plan-editor-row-manual-labeling')!.textContent).toContain('Dahil');
    expect((q('plan-editor-limit-book-import') as HTMLInputElement | null)).not.toBeNull();
    expect(q('plan-editor-trial-days')).toBeNull();
  });

  it('saves limited, unlimited and omits features that are not in the plan', () => {
    const c = fixture.componentInstance;
    const [labeling, book, live] = c.rows();
    c.setMode(labeling, 'none');
    c.setLimit(book, 5);
    c.setMode(live, 'unlimited');
    c.monthlyPrice.set(199.5);
    c.save();
    fixture.detectChanges();

    expect(updatePlan).toHaveBeenCalledWith('teacher-pro', {
      name: 'teacher-pro',
      monthlyPrice: 199.5,
      trialDays: null,
      isActive: true,
      monthlyLimits: { 'book-import': 5, 'live-lesson': null },
    });
    expect(q('plan-editor-done')).not.toBeNull();
  });

  it('blocks saving an empty limit and edits trial days only on the trial plan', () => {
    const c = fixture.componentInstance;
    c.setLimit(c.rows()[1], null);
    expect(c.valid()).toBe(false);

    c.selectPlan('trial');
    fixture.detectChanges();
    expect(c.rows().map((r) => r.feature.key)).toEqual(['manual-labeling', 'book-import', 'mock-exam', 'live-lesson']);
    expect(q('plan-editor-trial-days')).not.toBeNull();
    c.trialDays.set(0);
    expect(c.valid()).toBe(false);
    c.trialDays.set(30);
    expect(c.valid()).toBe(true);
  });

  it('shows the server error code as a message', () => {
    updatePlan.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'subscription.planRequired' } })));
    fixture.componentInstance.save();
    fixture.detectChanges();

    expect(q('plan-editor-error')!.textContent).toContain('Ücretsiz ve deneme planları kapatılamaz.');
  });

  it('issue #1108: edits the student price grid, adds a grade row and sends only filled cells', async () => {
    const student: SubscriptionPlan = {
      ...plan('student-basic', 'Student', { 'mock-exam': 3 }),
      prices: [
        { band: 'Middle', gradeLevel: null, scope: 'AllSubjects', duration: 'Monthly', price: 199 },
        { band: 'Middle', gradeLevel: null, scope: 'SingleSubject', duration: 'Monthly', price: 119 },
      ],
    };
    fixture.componentRef.setInput('plans', [...PLANS, student]);
    const c = fixture.componentInstance;
    c.selectPlan('teacher-pro');
    fixture.detectChanges();
    expect(q('plan-editor-prices')).toBeNull();

    c.selectPlan('student-basic');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(q('plan-editor-prices')).not.toBeNull();
    // Her band satırı vardır (fiyatı olmasa da).
    expect(c.priceGrid()!.map((r) => r.band)).toEqual(['Primary', 'Middle', 'Lgs', 'HighSchool', 'Yks']);
    expect((q('price-Middle-AllSubjects.Monthly') as HTMLInputElement).value).toBe('199');

    const middle = c.priceGrid()![1];
    c.setPrice(middle, c.priceColumns[0], 210);
    c.setPrice(c.priceGrid()![1], c.priceColumns[4], '');
    c.addGradeRow(6);
    fixture.detectChanges();
    expect(c.priceGrid()!.map((r) => `${r.band}${r.gradeLevel ?? ''}`)).toEqual(['Primary', 'Middle', 'Middle6', 'Lgs', 'HighSchool', 'Yks']);
    expect(c.freeGrades()).not.toContain(6);
    c.setPrice(c.priceGrid()![2], c.priceColumns[0], 189.9);

    c.setPrice(c.priceGrid()![0], c.priceColumns[1], 0);
    expect(c.valid()).toBe(false);
    c.setPrice(c.priceGrid()![0], c.priceColumns[1], null);
    expect(c.valid()).toBe(true);

    c.save();
    expect(updatePlan).toHaveBeenCalledWith('student-basic', expect.objectContaining({
      prices: [
        { band: 'Middle', gradeLevel: null, scope: 'AllSubjects', duration: 'Monthly', price: 210 },
        { band: 'Middle', gradeLevel: 6, scope: 'AllSubjects', duration: 'Monthly', price: 189.9 },
      ],
    }));
  });

  it('issue #1108: a teacher plan save sends no price table', () => {
    fixture.componentInstance.save();
    expect(updatePlan.mock.calls[0][1]).not.toHaveProperty('prices');
  });
});


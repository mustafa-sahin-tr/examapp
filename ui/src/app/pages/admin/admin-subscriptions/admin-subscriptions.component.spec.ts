import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideNativeDateAdapter } from '@angular/material/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { AdminSubscriptionsComponent } from './admin-subscriptions.component';
import { SubscriptionService } from '../../../services/subscription.service';
import { SchoolService } from '../../../services/school.service';
import { AdminSubscriptionUserDetail, SubscriptionPlan } from '../../../models/subscription';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import subscriptionTr from '../../../../../public/i18n/subscription/tr.json';

const plan = (key: string, name: string, isActive = true, audience: SubscriptionPlan['audience'] = 'Any'): SubscriptionPlan => ({
  key,
  name,
  displayNameKey: `subscription.plans.${name}`,
  audience,
  monthlyPrice: 0,
  currency: 'TRY',
  trialDays: null,
  isActive,
  monthlyLimits: {},
});

// #1093: öğretmen planı adının önüne kitle yazılır ("Öğretmen · Pro").
const PLANS = [plan('free', 'free'), plan('teacher-pro', 'teacherPro', true, 'Teacher'), plan('old', 'studentPlus', false)];

const DETAIL: AdminSubscriptionUserDetail = {
  userId: 7,
  fullName: 'Ayşe Öğretmen',
  email: 'a***@example.com',
  role: 'Teacher',
  planKey: 'teacher-pro',
  planName: 'Teacher Pro',
  validToUtc: '2026-12-31T21:00:00Z',
  assignments: [
    { id: 1, planKey: 'teacher-pro', planName: 'Teacher Pro', validFromUtc: '2026-11-01T00:00:00Z', validToUtc: null, status: 'Active' },
  ],
  changes: [
    {
      occurredAtUtc: '2026-11-01T09:00:00Z',
      action: 'Assigned',
      fromPlanKey: null,
      toPlanKey: 'teacher-pro',
      validFromUtc: '2026-11-01T00:00:00Z',
      validToUtc: null,
      actorName: 'Admin Kişi',
      bySelf: false,
      schoolId: null,
    },
  ],
};

/** Issue #1030: admin plan ataması — arama, detay, atama / bitirme, okula toplu atama (onaylı). */
describe('AdminSubscriptionsComponent (issue #1030)', () => {
  let fixture: ComponentFixture<AdminSubscriptionsComponent>;
  let service: Record<string, ReturnType<typeof vi.fn>>;

  beforeEach(() => {
    service = {
      adminPlans: vi.fn().mockReturnValue(of(PLANS)),
      searchUsers: vi.fn().mockReturnValue(of([{ ...DETAIL }])),
      userDetail: vi.fn().mockReturnValue(of(DETAIL)),
      assignUser: vi.fn().mockReturnValue(of({})),
      endUserPlan: vi.fn().mockReturnValue(of({})),
      schoolMembers: vi.fn().mockReturnValue(of({ schoolId: 3, teachers: 2, students: 5 })),
      assignSchool: vi.fn().mockReturnValue(of({ assigned: 7 })),
    };
    TestBed.configureTestingModule({
      imports: [AdminSubscriptionsComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [
        provideNoopAnimations(),
        provideNativeDateAdapter(),
        { provide: SubscriptionService, useValue: service },
        { provide: SchoolService, useValue: { search: vi.fn().mockReturnValue(of([{ id: 3, name: 'Okul' }])), getById: vi.fn().mockReturnValue(of({ id: 3, name: 'Okul' })) } },
      ],
    });
    fixture = TestBed.createComponent(AdminSubscriptionsComponent);
    fixture.detectChanges();
  });

  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (id: string): string => el().querySelector(`[data-testid="${id}"]`)!.textContent!.replace(/\s+/g, ' ').trim();

  it('offers only active plans and needs two characters to search', () => {
    const c = fixture.componentInstance;
    expect(c.activePlans().map((p) => p.key)).toEqual(['free', 'teacher-pro']);

    c.query.set(' a ');
    expect(c.canSearch()).toBe(false);
    c.search();
    expect(service['searchUsers']).not.toHaveBeenCalled();

    c.query.set('ay');
    c.role.set('Teacher');
    c.search();
    expect(service['searchUsers']).toHaveBeenCalledWith('ay', 'Teacher');
  });

  it('shows the selected user plan, assignments and history', () => {
    const c = fixture.componentInstance;
    c.select(7);
    fixture.detectChanges();

    expect(text('admin-sub-current')).toBe('Öğretmen · Pro');
    expect(text('admin-sub-assignments')).toContain('Etkin');
    expect(text('admin-sub-history')).toContain('Ücretsiz → Öğretmen · Pro');
    expect(text('admin-sub-history')).toContain('Admin Kişi');
  });

  it('sends the end date as the last moment of the chosen local day and reloads the user', () => {
    const c = fixture.componentInstance;
    c.select(7);
    c.userPlanKey.set('teacher-pro');
    c.userFrom.set(new Date(2026, 10, 10, 15, 30));
    c.userTo.set(new Date(2026, 11, 31));
    c.assignUser();

    expect(service['assignUser']).toHaveBeenCalledWith(7, {
      planKey: 'teacher-pro',
      validFromUtc: new Date(2026, 10, 10).toISOString(),
      validToUtc: new Date(2026, 11, 31, 23, 59, 59, 999).toISOString(),
    });
    expect(service['userDetail']).toHaveBeenCalledTimes(2);
  });

  it('ends the plan only after the inline confirmation', () => {
    const c = fixture.componentInstance;
    c.select(7);
    fixture.detectChanges();

    (el().querySelector('[data-testid="admin-sub-end"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service['endUserPlan']).not.toHaveBeenCalled();

    (el().querySelector('[data-testid="admin-sub-end-confirm"]') as HTMLButtonElement).click();
    expect(service['endUserPlan']).toHaveBeenCalledWith(7);
  });

  it('assigns to a school after confirming the member count', () => {
    const c = fixture.componentInstance;
    c.selectSchool(3);
    c.schoolPlanKey.set('teacher-pro');
    c.audience.set('Teachers');
    expect(c.targetCount()).toBe(2);

    c.assignSchool();
    fixture.detectChanges();
    expect(service['assignSchool']).not.toHaveBeenCalled();

    c.assignSchool();
    expect(service['assignSchool']).toHaveBeenCalledWith(3, {
      planKey: 'teacher-pro',
      validFromUtc: null,
      validToUtc: null,
      audience: 'Teachers',
    });
    expect(c.schoolResult()).toBe(7);
  });

  it('maps server error codes to messages', () => {
    service['assignSchool'].mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'subscription.audienceMismatch' } })),
    );
    const c = fixture.componentInstance;
    c.selectSchool(3);
    c.schoolPlanKey.set('teacher-pro');
    c.assignSchool();
    c.assignSchool();
    fixture.detectChanges();

    expect(text('admin-sub-error')).toBe('Bu plan seçilen kitleye uygun değil.');
  });
});

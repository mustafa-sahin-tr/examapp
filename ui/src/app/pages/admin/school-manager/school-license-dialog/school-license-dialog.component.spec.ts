import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';
import type { MockedObject } from 'vitest';

import { SchoolLicenseDialogComponent } from './school-license-dialog.component';
import { SchoolLicense, SubscriptionPlan } from '../../../../models/subscription';
import { SubscriptionService } from '../../../../services/subscription.service';
import { translocoTestingModule } from '../../../../shared/testing/transloco-testing';
import subscriptionTr from '../../../../../../public/i18n/subscription/tr.json';

const plan = (key: string, audience: SubscriptionPlan['audience'], extra: Partial<SubscriptionPlan> = {}): SubscriptionPlan =>
  ({ key, audience, displayNameKey: `subscription.plans.${key.replace(/-(\w)/g, (_, c: string) => c.toUpperCase())}`, monthlyPrice: 100, trialDays: null, isActive: true, ...extra }) as SubscriptionPlan;

const PLANS = [
  plan('free', 'Any', { monthlyPrice: 0 }),
  plan('trial', 'Any', { monthlyPrice: 0, trialDays: 14 }),
  plan('teacher-basic', 'Teacher'),
  plan('teacher-pro', 'Teacher'),
  plan('student-basic', 'Student'),
  plan('student-pro', 'Student'),
  plan('student-plus', 'Student', { isActive: false }),
];

const NONE: SchoolLicense = {
  schoolId: 7, exists: false, active: false, teacherPlanKey: null, studentPlanKey: null,
  validFromUtc: null, validToUtc: null, note: null, history: [],
};

const ACTIVE: SchoolLicense = {
  ...NONE, exists: true, active: true, teacherPlanKey: 'teacher-pro', studentPlanKey: 'student-basic',
  validFromUtc: '2026-11-01T00:00:00Z', validToUtc: '2027-11-01T00:00:00Z', note: 'Sözleşme 42',
  history: [{ action: 'Created', actorUserId: 1, teacherPlanKey: 'teacher-pro', studentPlanKey: 'student-basic',
    validFromUtc: '2026-11-01T00:00:00Z', validToUtc: '2027-11-01T00:00:00Z', note: 'Sözleşme 42', occurredAtUtc: '2026-11-01T09:00:00Z' }],
};

describe('SchoolLicenseDialogComponent (issue #1092)', () => {
  let service: MockedObject<SubscriptionService>;

  function setup(license: SchoolLicense) {
    service = {
      schoolLicense: vi.fn().mockReturnValue(of(license)),
      adminPlans: vi.fn().mockReturnValue(of(PLANS)),
      saveSchoolLicense: vi.fn(),
      endSchoolLicense: vi.fn(),
    } as unknown as MockedObject<SubscriptionService>;
    TestBed.configureTestingModule({
      imports: [SchoolLicenseDialogComponent, translocoTestingModule({ langs: { 'subscription/tr': subscriptionTr } })],
      providers: [
        provideNoopAnimations(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { schoolId: 7, schoolName: 'Atatürk Ortaokulu' } },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: SubscriptionService, useValue: service },
      ],
    });
    const fixture = TestBed.createComponent(SchoolLicenseDialogComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }

  const text = (root: HTMLElement, test: string) => root.querySelector(`[data-test="${test}"]`)?.textContent?.trim();

  it('withoutLicense_DefaultsToProForBothAudiences_AndCreatesWithEmptyDates', () => {
    const { fixture, component, root } = setup(NONE);
    service.saveSchoolLicense.mockReturnValue(of(ACTIVE));

    expect(text(root, 'license-status')).toBe(subscriptionTr.admin.license.status.none);
    expect(text(root, 'license-save')).toBe(subscriptionTr.admin.license.create);
    expect(root.querySelector('[data-test="license-end"]')).toBeNull();
    // Lisans planı olabilecekler: ücretli, deneme değil, etkin; kitleye göre.
    expect(component.teacherPlans().map((p) => p.key)).toEqual(['teacher-basic', 'teacher-pro']);
    expect(component.studentPlans().map((p) => p.key)).toEqual(['student-basic', 'student-pro']);

    component.save();
    fixture.detectChanges();

    expect(service.saveSchoolLicense).toHaveBeenCalledWith(7, {
      teacherPlanKey: 'teacher-pro', studentPlanKey: 'student-pro', validFromUtc: null, validToUtc: null, note: null,
    });
    expect(text(root, 'license-saved')).toBe(subscriptionTr.admin.license.saved);
    expect(text(root, 'license-save')).toBe(subscriptionTr.admin.license.update);
  });

  it('activeLicense_FillsTheForm_EndDateIsInclusive_AndExtendSendsTheNextDayAsExclusiveEnd', () => {
    const { component, root } = setup(ACTIVE);
    service.saveSchoolLicense.mockReturnValue(of(ACTIVE));

    expect(component.status()).toBe('active');
    expect(component.studentPlanKey()).toBe('student-basic');
    expect(component.note()).toBe('Sözleşme 42');
    expect(root.querySelectorAll('[data-test="license-history"] li').length).toBe(1);
    // Plan adları `subscription` scope'undan (ham anahtar değil).
    expect(text(root, 'license-history')).toContain(`${subscriptionTr.plans.teacherPro} / ${subscriptionTr.plans.studentBasic}`);

    component.to.set(new Date(2028, 0, 31));
    component.save();

    expect(service.saveSchoolLicense.mock.calls[0][1].validToUtc).toBe(new Date(2028, 1, 1).toISOString());
  });

  it('end_AsksForConfirmationFirst_ThenEnds', () => {
    const { fixture, component, root } = setup(ACTIVE);
    service.endSchoolLicense.mockReturnValue(of({ ...ACTIVE, active: false, validToUtc: '2026-11-10T12:00:00Z' }));

    component.end();
    fixture.detectChanges();
    expect(service.endSchoolLicense).not.toHaveBeenCalled();
    expect(text(root, 'license-end-confirm')).toBe(subscriptionTr.admin.license.endConfirm);

    component.end();
    fixture.detectChanges();
    expect(service.endSchoolLicense).toHaveBeenCalledWith(7);
    expect(component.status()).toBe('ended');
    expect(text(root, 'license-saved')).toBe(subscriptionTr.admin.license.ended);
    // Bitmiş dönemin tarihleri formda kalmaz.
    expect(component.from()).toBeNull();
    expect(component.to()).toBeNull();
    expect(root.querySelector('[data-test="license-end"]')).toBeNull();
  });

  it('serverError_ShowsTheMatchingMessage', () => {
    const { fixture, component, root } = setup(NONE);
    service.saveSchoolLicense.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'subscription.invalidRange' } })));

    component.save();
    fixture.detectChanges();

    expect(text(root, 'license-error')).toBe(subscriptionTr.admin.license.errors.invalidRange);
  });
});

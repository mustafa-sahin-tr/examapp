import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';

import { SchoolPendingBannerComponent } from './school-pending-banner.component';
import { AuthService, UserProfile } from '../../../services/auth.service';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('SchoolPendingBannerComponent (issue #361)', () => {
  let user: WritableSignal<UserProfile | null>;

  function create(profile: UserProfile | null) {
    user = signal(profile);
    TestBed.configureTestingModule({
      imports: [SchoolPendingBannerComponent, translocoTestingModule()],
      providers: [{ provide: AuthService, useValue: { user } }],
    });
    const fixture = TestBed.createComponent(SchoolPendingBannerComponent);
    fixture.detectChanges();
    return fixture;
  }

  function student(extra: Record<string, unknown>): UserProfile {
    return {
      id: 1, email: 's@t', avatar: '', fullName: 'S', keycloakId: 'kc', profileId: 1, role: 'Student',
      student: { id: 1, userId: 1, studentNumber: '1', schoolName: '', grade: { id: 1, name: '8' }, ...extra },
    } as UserProfile;
  }

  it('renders the pending notice with the school name', () => {
    const fixture = create(student({ pendingSchoolId: 5, pendingSchoolName: 'Atatürk Ortaokulu' }));
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Okul onayı bekleniyor');
    expect(text).toContain('Atatürk Ortaokulu');
    expect(fixture.nativeElement.querySelector('[role="status"]')).not.toBeNull();
  });

  it('renders nothing for a verified or schoolless student, or when no profile', () => {
    expect(create(student({ schoolId: 5, pendingSchoolId: null })).nativeElement.textContent.trim()).toBe('');
    TestBed.resetTestingModule();
    expect(create(student({})).nativeElement.textContent.trim()).toBe('');
    TestBed.resetTestingModule();
    expect(create(null).nativeElement.textContent.trim()).toBe('');
  });

  it('disappears reactively once the refreshed profile is verified', () => {
    const fixture = create(student({ pendingSchoolId: 5, pendingSchoolName: 'Okul' }));
    expect(fixture.nativeElement.textContent).toContain('Okul onayı bekleniyor');

    user.set(student({ schoolId: 5, pendingSchoolId: null, pendingSchoolName: null }));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });

  it('falls back to generic text when the school name is missing', () => {
    const fixture = create(student({ pendingSchoolId: 5, pendingSchoolName: '  ' }));
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Okul onayı bekleniyor');
    expect(text).toContain('Okul üyeliğiniz okulunuzun öğretmeni');
    expect(text).not.toContain('—');
  });

  it('English text is available', () => {
    create(null);
    const transloco = TestBed.inject(TranslocoService);
    expect(transloco.translate('dashboard.schoolPending.title', {}, 'en')).toBe('School approval pending');
  });
});

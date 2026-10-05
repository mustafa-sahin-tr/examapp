import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MAT_SNACK_BAR_DATA, MatSnackBarRef } from '@angular/material/snack-bar';

import { BADGE_TOAST_ROUTE, BadgeEarnedToastComponent } from './badge-earned-toast.component';
import { BadgeEarnedPushPayload } from '../../../models/notification.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import trTranslations from '../../../../../public/i18n/tr.json';

describe('BadgeEarnedToastComponent (issue #149)', () => {
  let fixture: ComponentFixture<BadgeEarnedToastComponent>;
  let snackBarRef: jasmine.SpyObj<MatSnackBarRef<BadgeEarnedToastComponent>>;
  let router: jasmine.SpyObj<Router>;

  function create(data: BadgeEarnedPushPayload): HTMLElement {
    snackBarRef = jasmine.createSpyObj('MatSnackBarRef', ['dismiss', 'dismissWithAction']);
    router = jasmine.createSpyObj<Router>('Router', ['navigateByUrl']);
    router.navigateByUrl.and.resolveTo(true);
    TestBed.configureTestingModule({
      imports: [BadgeEarnedToastComponent, translocoTestingModule()],
      providers: [
        { provide: MAT_SNACK_BAR_DATA, useValue: data },
        { provide: MatSnackBarRef, useValue: snackBarRef },
        { provide: Router, useValue: router },
      ],
    });
    fixture = TestBed.createComponent(BadgeEarnedToastComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const payload: BadgeEarnedPushPayload = {
    badgeName: 'Soru Avcısı III',
    description: '250 soru çözdün.',
    icon: 'gps_fixed',
    iconUrl: null,
  };

  it('Render_EyebrowNameDescriptionAndNewMedallion_NoOwnLiveRegion', () => {
    const el = create(payload);

    // CR U6: duyuruyu snackbar'ın canlı bölgesi yapar (politeness: 'polite'); içerikte role="status" yok.
    expect(el.querySelector('[role="status"]')).toBeNull();
    expect(el.textContent).toContain(trTranslations.shared.badgeEarnedToast.eyebrow);
    expect(el.querySelector('[data-testid="badge-toast-name"]')?.textContent?.trim()).toBe('Soru Avcısı III');
    expect(el.querySelector('.bet__description')?.textContent?.trim()).toBe('250 soru çözdün.');
    const medallion = el.querySelector('app-badge-medallion');
    expect(medallion?.getAttribute('data-state')).toBe('new');
    expect(medallion?.querySelector('.bm__icon')?.textContent?.trim()).toBe('gps_fixed');
  });

  it('Render_NoEmoji', () => {
    const el = create(payload);
    expect(/\p{Extended_Pictographic}/u.test(el.textContent ?? '')).toBeFalse();
  });

  it('MissingNameAndDescription_ShowsFallbackNameAndNoDescription', () => {
    const el = create({ badgeName: '', description: '', icon: null, iconUrl: null });

    expect(el.querySelector('[data-testid="badge-toast-name"]')?.textContent?.trim()).toBe(
      trTranslations.shared.badgeEarnedToast.fallbackName
    );
    expect(el.querySelector('.bet__description')).toBeNull();
    expect(el.querySelector('app-badge-medallion .bm__icon')?.textContent?.trim()).toBe('military_tech');
  });

  it('MyBadgesAction_DismissesWithActionAndNavigatesToCertificates', () => {
    const el = create(payload);
    const action = el.querySelector<HTMLButtonElement>('[data-testid="badge-toast-open"]')!;

    expect(action.textContent?.trim()).toBe(trTranslations.shared.badgeEarnedToast.action);
    action.click();

    expect(snackBarRef.dismissWithAction).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith(BADGE_TOAST_ROUTE);
    expect(BADGE_TOAST_ROUTE).toBe('/certificates');
  });

  it('CloseButton_HasAccessibleNameAndDismisses', () => {
    const el = create(payload);
    const close = el.querySelector<HTMLButtonElement>('[data-testid="badge-toast-close"]')!;

    expect(close.getAttribute('aria-label')).toBe(trTranslations.shared.badgeEarnedToast.close);
    close.click();

    expect(snackBarRef.dismiss).toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});

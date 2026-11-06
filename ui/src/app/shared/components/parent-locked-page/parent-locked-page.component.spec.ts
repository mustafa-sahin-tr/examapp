import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { of } from 'rxjs';

import { ParentLockedFeature, ParentLockedPageComponent } from './parent-locked-page.component';
import { AuthService } from '../../../services/auth.service';
import { LocaleService } from '../../../services/locale.service';
import { ParentStatusStore } from '../../../services/parent-status.store';
import { StudentParentService } from '../../../services/student-parent.service';
import { awaitingStatusOf } from '../../testing/parent-status-testing';
import { translocoTestingModule } from '../../testing/transloco-testing';

/** Issue #845: veli onayı beklerken kapalı sayfanın kilit ekranı. */
describe('ParentLockedPageComponent', () => {
    function create(feature: ParentLockedFeature) {
        TestBed.configureTestingModule({
            imports: [ParentLockedPageComponent, translocoTestingModule()],
            providers: [
                provideRouter([]),
                { provide: StudentParentService, useValue: { resend: vi.fn(), reload: vi.fn().mockReturnValue(of(null)) } },
                { provide: AuthService, useValue: { user: signal({ id: 1 }) } },
                { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
            ],
        });
        TestBed.inject(ParentStatusStore).set(awaitingStatusOf());
        const fixture = TestBed.createComponent(ParentLockedPageComponent);
        fixture.componentRef.setInput('feature', feature);
        fixture.detectChanges();
        return fixture;
    }

    it('saysWhyAndOffersResendAndFixEmail', () => {
        const el: HTMLElement = create('messages').nativeElement;

        expect(el.querySelector('h2')?.textContent).toContain('Velin onaylayınca açılacak');
        expect(el.textContent).toContain('Öğretmenlerinle mesajlaşmak için');
        expect(el.textContent).toContain('çözmeye devam edebilirsin');
        expect(el.querySelector('[data-test="invitation-resend"]')).not.toBeNull();
        expect(el.querySelector('[data-test="invitation-fix-email"]')).not.toBeNull();
        // Kartın kısıt listesi tekrar etmez.
        expect(el.textContent).not.toContain('liderlik tablosu kapalı');
    });

    it('allFeatureKeysExistInBothLanguages', () => {
        create('tutors');
        const transloco = TestBed.inject(TranslocoService);
        for (const key of ['title', 'meanwhile', 'messages', 'tutors']) {
            for (const lang of ['tr', 'en']) {
                expect(transloco.translate(`parentRequirement.locked.${key}`, {}, lang)).not.toBe(`parentRequirement.locked.${key}`);
            }
        }
    });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';

import { LandingFooterComponent } from './landing-footer.component';
import { translocoTestingModule } from '../../../../shared/testing/transloco-testing';
import landingTr from '../../../../../../public/i18n/landing/tr.json';

/**
 * Landing komponentleri 'landing' scope'undaki gercek sozlugu kullanir (issue #182);
 * sahte ceviri verilmez, boylece bir anahtar bozulursa test kirilir. Marka adi kok sozlukteki
 * `brand.name` anahtarindan geldigi icin (issue #409) kok sozluk de yuklenir.
 */
describe('LandingFooterComponent', () => {
  let component: LandingFooterComponent;
  let fixture: ComponentFixture<LandingFooterComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LandingFooterComponent, translocoTestingModule({ langs: { 'landing/tr': landingTr } })],
    }).compileComponents();

    fixture = TestBed.createComponent(LandingFooterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('brandName_LogoAltAndCopyright_ComeFromBrandKey (issue #409)', () => {
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('.widget-logo img')?.getAttribute('alt')).toBe('Hedef Okul');
    expect(host.textContent).toContain('© Hedef Okul.');
  });
});

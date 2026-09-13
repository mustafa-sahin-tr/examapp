import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FunfactsComponent } from './funfacts.component';
import { TranslocoTestingModule } from '@jsverse/transloco';

import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../../../models/locale';
import landingTr from '../../../../../../public/i18n/landing/tr.json';

/**
 * Landing komponentleri 'landing' scope'undaki gercek sozlugu kullanir (issue #182);
 * sahte ceviri verilmez, boylece bir anahtar bozulursa test kirilir.
 */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { 'landing/tr': landingTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
  },
  preloadLangs: true,
});


describe('FunfactsComponent', () => {
  let component: FunfactsComponent;
  let fixture: ComponentFixture<FunfactsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FunfactsComponent, translocoTesting]
    })
    .compileComponents();

    fixture = TestBed.createComponent(FunfactsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

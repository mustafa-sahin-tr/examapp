import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionLiteViewComponent } from './question-lite-view.component';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import questionTr from '../../../../public/i18n/question/tr.json';

/** Gercek sozluk yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { 'question/tr': questionTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    // Uygulama config'i ile ayni: scope oneki klasor adiyla birebir ayni kalsin (issue #183).
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('QuestionLiteViewComponent', () => {
  let component: QuestionLiteViewComponent;
  let fixture: ComponentFixture<QuestionLiteViewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionLiteViewComponent, translocoTesting]
    })
    .compileComponents();

    fixture = TestBed.createComponent(QuestionLiteViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';

import { QuestionListComponent } from './question-list.component';

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

describe('QuestionListComponent', () => {
  let component: QuestionListComponent;
  let fixture: ComponentFixture<QuestionListComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionListComponent, translocoTesting],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    })
    .compileComponents();

    fixture = TestBed.createComponent(QuestionListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

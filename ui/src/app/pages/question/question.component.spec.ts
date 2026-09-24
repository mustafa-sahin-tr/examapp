import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';

import { QuestionComponent } from './question.component';

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

describe('QuestionComponent', () => {
  let component: QuestionComponent;
  let fixture: ComponentFixture<QuestionComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionComponent, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    })
    .compileComponents();

    fixture = TestBed.createComponent(QuestionComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

/** Issue #287 (review): soru uçları yetkisiz / sahibi olmayan kullanıcıya gövdesiz 403 döner. */
describe('QuestionComponent — plain 403 on question endpoints (issue #287)', () => {
  it('loadQuestion_403_ShowsForbiddenMessage_NoRedirect', () => {
    TestBed.configureTestingModule({
      imports: [QuestionComponent, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '7' }) } } },
      ],
    });
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate');
    const navigateByUrl = spyOn(router, 'navigateByUrl');
    const fixture = TestBed.createComponent(QuestionComponent);
    const snackOpen = spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');
    fixture.detectChanges();

    const httpMock = TestBed.inject(HttpTestingController);
    httpMock
      .match((req) => req.url.startsWith('/api/exam/questions/7'))
      .forEach((req) => req.flush(null, { status: 403, statusText: 'Forbidden' }));

    expect(snackOpen).toHaveBeenCalledWith(
      'Bu soru üzerinde işlem yapma yetkiniz yok.',
      jasmine.any(String),
      jasmine.any(Object),
    );
    expect(navigate).not.toHaveBeenCalled();
    expect(navigateByUrl).not.toHaveBeenCalled();
  });
});

import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { TutorProfileComponent } from './tutor-profile.component';
import { TeacherService } from '../../services/teacher.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tutorProfileTr from '../../../../public/i18n/tutor-profile/tr.json';
import tutorProfileEn from '../../../../public/i18n/tutor-profile/en.json';
import { TranslocoService } from '@jsverse/transloco';

/** Issue #384: bağımsız olmayan öğretmende sunucunun ham metni değil UI sözlüğündeki metin gösterilir. */
describe('TutorProfileComponent unavailable state (issue #384)', () => {
  function create(status: number) {
    const error = new HttpErrorResponse({
      status,
      error: { message: 'Tutor profili yalnızca bağımsız öğretmenler için kullanılabilir.' },
    });
    TestBed.configureTestingModule({
      imports: [
        TutorProfileComponent,
        translocoTestingModule({
          langs: { 'tutor-profile/tr': tutorProfileTr, 'tutor-profile/en': tutorProfileEn },
          translocoConfig: { scopes: { keepCasing: true }, reRenderOnLangChange: true },
        }),
      ],
      providers: [
        provideNoopAnimations(),
        { provide: TeacherService, useValue: { getTutorProfile: () => throwError(() => error) } },
        { provide: SubjectService, useValue: { loadCategories: () => of([]) } },
      ],
    });
    const fixture = TestBed.createComponent(TutorProfileComponent);
    fixture.detectChanges();
    return fixture;
  }

  for (const status of [400, 404]) {
    it(`load_${status}_ShowsDictionaryTextInsteadOfServerMessage`, () => {
      const fixture = create(status);
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

      expect(fixture.componentInstance.unavailable()).toBeTrue();
      expect(text).toContain(tutorProfileTr.messages.unavailable);
      expect(text).not.toContain('Tutor');
    });
  }

  it('unavailable_LanguageChange_ShowsEnglishText', fakeAsync(() => {
    const fixture = create(400);

    TestBed.inject(TranslocoService).setActiveLang('en');
    tick();
    fixture.detectChanges();
    tick();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(tutorProfileEn.messages.unavailable);
  }));
});

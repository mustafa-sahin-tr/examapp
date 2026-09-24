import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';

import { TutorSearchComponent } from './tutor-search.component';
import { TUTOR_SEARCH_SCOPE } from './tutor-search-scope';
import { SubjectService } from '../../services/subject.service';
import { TeacherService } from '../../services/teacher.service';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import tutorSearchTr from '../../../../public/i18n/tutor-search/tr.json';

/** Gerçek scope sözlüğü yüklenir; anahtar bozulursa test kırılır (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: {
    tr: { ...rootTr, [TUTOR_SEARCH_SCOPE]: tutorSearchTr },
    [`${TUTOR_SEARCH_SCOPE}/tr`]: tutorSearchTr,
  },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('TutorSearchComponent (issue #196 — kompakt filtre)', () => {
  let fixture: ComponentFixture<TutorSearchComponent>;
  let component: TutorSearchComponent;
  let teacherService: jasmine.SpyObj<TeacherService>;
  let subjectService: jasmine.SpyObj<SubjectService>;

  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;

  beforeEach(async () => {
    teacherService = jasmine.createSpyObj<TeacherService>('TeacherService', ['searchTutors']);
    teacherService.searchTutors.and.returnValue(of([]));
    subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', ['loadCategories']);
    subjectService.loadCategories.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [TutorSearchComponent, translocoTesting],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: TeacherService, useValue: teacherService },
        { provide: SubjectService, useValue: subjectService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TutorSearchComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('ücret aralığını tek bir role="group" altında, tek etiket ve tek ₺ ekiyle gösterir', () => {
    const groups = el().querySelectorAll('.ts__price');
    expect(groups.length).toBe(1);

    const group = groups[0] as HTMLElement;
    expect(group.getAttribute('role')).toBe('group');
    expect(group.getAttribute('aria-label')).toBe('Ücret aralığı');
    expect(group.querySelector('.ts__price-label')?.textContent?.trim()).toBe('Ücret');
    expect(group.querySelector('.ts__price-sep')?.textContent?.trim()).toBe('–');
    expect(group.querySelectorAll('.ts__price-suffix').length).toBe(1);
    expect(group.querySelector('.ts__price-suffix')?.textContent?.trim()).toBe('₺');
    expect(el().querySelectorAll('[matTextSuffix]').length).toBe(0);
  });

  it('min/max input\'ları placeholder, aria-label, type=number ve min=0 taşır', () => {
    const min = el().querySelector<HTMLInputElement>('input[formControlName="minPrice"]')!;
    const max = el().querySelector<HTMLInputElement>('input[formControlName="maxPrice"]')!;

    expect(min.closest('.ts__price')).not.toBeNull();
    expect(max.closest('.ts__price')).not.toBeNull();
    expect(min.getAttribute('placeholder')).toBe('Min');
    expect(max.getAttribute('placeholder')).toBe('Max');
    expect(min.getAttribute('aria-label')).toBe('Minimum ücret');
    expect(max.getAttribute('aria-label')).toBe('Maksimum ücret');
    for (const input of [min, max]) {
      expect(input.type).toBe('number');
      expect(input.getAttribute('min')).toBe('0');
    }
  });

  it('Filtrele mevcut form değerleriyle aynı payload\'u gönderir', () => {
    teacherService.searchTutors.calls.reset();
    component.filterForm.setValue({ subjectId: 3, minPrice: 100, maxPrice: 400, online: true, inPerson: false });

    const form = fixture.debugElement.query(By.css('form.ts__filters'));
    form.triggerEventHandler('ngSubmit', {});

    expect(teacherService.searchTutors).toHaveBeenCalledOnceWith({
      subjectId: 3,
      minPrice: 100,
      maxPrice: 400,
      online: true,
      inPerson: false,
    });
  });

  it('boş bırakılan ücret alanları null gider (filtre uygulanmaz)', () => {
    teacherService.searchTutors.calls.reset();
    component.search();

    expect(teacherService.searchTutors).toHaveBeenCalledOnceWith({
      subjectId: null,
      minPrice: null,
      maxPrice: null,
      online: false,
      inPerson: false,
    });
  });

  it('Temizle tüm alanları sıfırlar ve yeniden arar', () => {
    component.filterForm.setValue({ subjectId: 3, minPrice: 100, maxPrice: 400, online: true, inPerson: true });
    teacherService.searchTutors.calls.reset();

    const resetBtn = el().querySelector<HTMLButtonElement>('.ts__filter-actions button[type="button"]')!;
    resetBtn.click();

    expect(component.filterForm.getRawValue()).toEqual({
      subjectId: null,
      minPrice: null,
      maxPrice: null,
      online: false,
      inPerson: false,
    });
    expect(teacherService.searchTutors).toHaveBeenCalledOnceWith({
      subjectId: null,
      minPrice: null,
      maxPrice: null,
      online: false,
      inPerson: false,
    });
  });

  it('ders listesi yüklenemezse hata mesajını gösterir', () => {
    component.subjectsFailed.set(true);
    fixture.detectChanges();

    expect(el().querySelector('.ts__hint-error')?.textContent?.trim()).toBe('Ders listesi yüklenemedi.');
  });

  it('boş durum aria-live="polite" ile duyurulur', () => {
    const empty = el().querySelector('.ts__state');
    expect(empty?.getAttribute('aria-live')).toBe('polite');
    expect(empty?.textContent).toContain('Bu filtrelere uygun öğretmen bulunamadı');
  });
});

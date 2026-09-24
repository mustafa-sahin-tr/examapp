import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { OverlayContainer } from '@angular/cdk/overlay';
import { Observable, Subject, of, throwError } from 'rxjs';

import { SchoolSelectComponent, normalizeSchoolQuery } from './school-select.component';
import { SchoolService } from '../../../services/school.service';
import { School } from '../../../models/taxonomy';
import { translocoTestingModule } from '../../testing/transloco-testing';
import trTranslations from '../../../../../public/i18n/tr.json';
import enTranslations from '../../../../../public/i18n/en.json';

const school = (id: number, name: string, districtName: string | null = null, provinceName: string | null = null): School => ({
  id,
  name,
  provinceId: null,
  provinceName,
  districtId: null,
  districtName,
  addressLine: null,
});

const SCHOOLS = [
  school(1, 'Ankara Fen Lisesi', 'Çankaya', 'Ankara'),
  school(2, 'İstanbul Erkek Lisesi', 'Fatih', 'İstanbul'),
  school(3, 'Kadıköy Anadolu Lisesi', 'Kadıköy', 'İstanbul'),
];

describe('SchoolSelectComponent (issue #277)', () => {
  let fixture: ComponentFixture<SchoolSelectComponent>;
  let component: SchoolSelectComponent;
  let schoolService: jasmine.SpyObj<SchoolService>;

  function create(schools$: () => Observable<School[]> = () => of(SCHOOLS), inputs: Record<string, unknown> = {}): void {
    schoolService = jasmine.createSpyObj<SchoolService>('SchoolService', ['getSchools']);
    schoolService.getSchools.and.callFake(schools$);
    TestBed.configureTestingModule({
      imports: [SchoolSelectComponent, translocoTestingModule()],
      providers: [{ provide: SchoolService, useValue: schoolService }, provideNoopAnimations()],
    });
    fixture = TestBed.createComponent(SchoolSelectComponent);
    component = fixture.componentInstance;
    for (const [key, value] of Object.entries(inputs)) fixture.componentRef.setInput(key, value);
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement;
  const input = (): HTMLInputElement => el().querySelector('[data-testid="school-select-input"]') as HTMLInputElement;

  function type(text: string): void {
    input().value = text;
    input().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('loadsSchools_FromSchoolService', () => {
    create();
    expect(schoolService.getSchools).toHaveBeenCalledTimes(1);
    expect(component.schools().map((s) => s.id)).toEqual([1, 2, 3]);
    expect(input()).not.toBeNull();
  });

  it('loading_DisablesInputAndShowsLoadingHint', () => {
    create(() => new Subject<School[]>());
    expect(component.loading()).toBeTrue();
    expect(input().disabled).toBeTrue();
    expect(el().textContent).toContain(trTranslations.shared.schoolSelect.loading);
  });

  it('error_ShowsMessage_RetryReloads', () => {
    let calls = 0;
    create(() => (++calls === 1 ? throwError(() => new Error('boom')) : of(SCHOOLS)));
    expect(el().querySelector('[data-testid="school-select-error"]')?.textContent).toContain(
      trTranslations.shared.schoolSelect.loadFailed,
    );

    (el().querySelector('[data-testid="school-select-retry"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(schoolService.getSchools).toHaveBeenCalledTimes(2);
    expect(el().querySelector('[data-testid="school-select-error"]')).toBeNull();
    expect(input()).not.toBeNull();
  });

  it('emptyList_ShowsEmptyState', () => {
    create(() => of([]));
    expect(el().querySelector('[data-testid="school-select-empty"]')?.textContent).toContain(
      trTranslations.shared.schoolSelect.empty,
    );
  });

  it('typing_FiltersByNameDistrictOrProvince_CaseAndAccentInsensitive', () => {
    create();
    type('istanbul');
    expect(component.filtered().map((s) => s.id)).toEqual([2, 3]);
    type('KADIKÖY');
    expect(component.filtered().map((s) => s.id)).toEqual([3]);
    type('çankaya');
    expect(component.filtered().map((s) => s.id)).toEqual([1]);
  });

  it('selectingOption_SetsValueAndEmitsSchool', () => {
    create();
    const emitted: (School | null)[] = [];
    const values: (number | null)[] = [];
    component.selectionChange.subscribe((s) => emitted.push(s));
    component.value.subscribe((v) => values.push(v));

    input().dispatchEvent(new Event('focusin'));
    type('fen');
    const options = TestBed.inject(OverlayContainer).getContainerElement().querySelectorAll('mat-option');
    expect(options.length).toBe(1);
    (options[0] as HTMLElement).click();
    fixture.detectChanges();

    expect(component.value()).toBe(1);
    expect(values).toEqual([1]);
    expect(emitted.map((s) => s?.id)).toEqual([1]);
    expect(input().value).toBe('Ankara Fen Lisesi');
  });

  it('editingTextAfterSelection_DropsSelection_ShowsPickFromListHint', () => {
    create();
    component.select(2);
    fixture.detectChanges();
    const emitted: (School | null)[] = [];
    component.selectionChange.subscribe((s) => emitted.push(s));

    type('İstanbul Erkek');

    expect(component.value()).toBeNull();
    expect(emitted).toEqual([null]);
    expect(input().value).toBe('İstanbul Erkek');
    expect(el().querySelector('[data-testid="school-select-unmatched"]')?.textContent).toContain(
      trTranslations.shared.schoolSelect.pickFromList,
    );
  });

  it('externalValue_ShowsSchoolName', () => {
    create(undefined, { value: 3 });
    expect(component.query()).toBe('Kadıköy Anadolu Lisesi');
    expect(input().value).toBe('Kadıköy Anadolu Lisesi');
  });

  it('clear_ResetsValueAndText', () => {
    create();
    component.select(1);
    fixture.detectChanges();

    (el().querySelector('[data-testid="school-select-clear"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(component.value()).toBeNull();
    expect(input().value).toBe('');
  });

  it('excludeSchoolId_RemovesSchoolFromOptions', () => {
    create(undefined, { excludeSchoolId: 2 });
    expect(component.filtered().map((s) => s.id)).toEqual([1, 3]);
  });

  it('normalizeSchoolQuery_TurkishLetters', () => {
    expect(normalizeSchoolQuery('İSTANBUL')).toBe('istanbul');
    expect(normalizeSchoolQuery('Işık')).toBe('isik');
  });

  it('i18n_TrAndEnHaveSameKeys', () => {
    expect(Object.keys(enTranslations.shared.schoolSelect).sort()).toEqual(
      Object.keys(trTranslations.shared.schoolSelect).sort(),
    );
  });
});

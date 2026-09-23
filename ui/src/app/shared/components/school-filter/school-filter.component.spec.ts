import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatSelect } from '@angular/material/select';
import { OverlayContainer } from '@angular/cdk/overlay';
import { Observable, Subject, of } from 'rxjs';

import { SchoolFilterComponent } from './school-filter.component';
import { SchoolFilterValue, schoolFilterFromParams, schoolFilterToQuery } from './school-filter.model';
import { AdminService } from '../../../services/admin.service';
import { School } from '../../../models/taxonomy';
import { translocoTestingModule } from '../../testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('SchoolFilterComponent', () => {
  let fixture: ComponentFixture<SchoolFilterComponent>;
  let adminService: jasmine.SpyObj<AdminService>;
  let emitted: SchoolFilterValue[];

  const school = (id: number, name: string): School => ({
    id,
    name,
    provinceId: null,
    provinceName: null,
    districtId: null,
    districtName: null,
    addressLine: null,
  });

  function create(
    value: SchoolFilterValue,
    schools$: Observable<School[]> = of([school(5, 'Ankara Lisesi')]),
    unassignedLabel?: 'unassigned' | 'unassignedStudent',
  ): void {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['getSchools']);
    adminService.getSchools.and.returnValue(schools$);
    TestBed.configureTestingModule({
      imports: [
        SchoolFilterComponent,
        translocoTestingModule({ langs: { 'admin/tr': adminTr }, translocoConfig: { scopes: { keepCasing: true } } }),
      ],
      providers: [{ provide: AdminService, useValue: adminService }, provideNoopAnimations()],
    });
    fixture = TestBed.createComponent(SchoolFilterComponent);
    fixture.componentRef.setInput('value', value);
    if (unassignedLabel) fixture.componentRef.setInput('unassignedLabel', unassignedLabel);
    emitted = [];
    fixture.componentInstance.value.subscribe((v) => emitted.push(v));
    fixture.detectChanges();
  }

  it('unknownSchoolId_AfterSchoolsLoaded_ResetsToAllAndEmits', () => {
    create(999);

    expect(fixture.componentInstance.value()).toBe('all');
    expect(emitted).toEqual(['all']);
  });

  it('knownSchoolId_AfterSchoolsLoaded_KeepsValueWithoutEmitting', () => {
    create(5);

    expect(fixture.componentInstance.value()).toBe(5);
    expect(emitted).toEqual([]);
  });

  it('unknownSchoolId_WhileSchoolsLoading_KeepsValue', () => {
    create(999, new Subject<School[]>());

    expect(fixture.componentInstance.value()).toBe(999);
    expect(emitted).toEqual([]);
  });

  it('schoolFilterToQuery_MapsEachValue', () => {
    expect(schoolFilterToQuery('all')).toEqual({ schoolId: null, unassigned: false });
    expect(schoolFilterToQuery('unassigned')).toEqual({ schoolId: null, unassigned: true });
    expect(schoolFilterToQuery(5)).toEqual({ schoolId: 5, unassigned: false });
  });

  it('schoolFilterFromParams_InvalidValues_FallBackToAll', () => {
    expect(schoolFilterFromParams(null, null)).toBe('all');
    expect(schoolFilterFromParams('abc', null)).toBe('all');
    expect(schoolFilterFromParams('-3', null)).toBe('all');
    expect(schoolFilterFromParams('5', null)).toBe(5);
    expect(schoolFilterFromParams('5', 'true')).toBe('unassigned');
  });

  function optionTexts(): string[] {
    (fixture.debugElement.query(By.directive(MatSelect)).componentInstance as MatSelect).open();
    fixture.detectChanges();
    const container = TestBed.inject(OverlayContainer).getContainerElement();
    return Array.from(container.querySelectorAll('mat-option')).map((o) => (o.textContent ?? '').trim());
  }

  it('unassignedLabel_Default_ShowsIndependentOrNoSchool', () => {
    create('all');
    expect(optionTexts()).toEqual([adminTr.schoolFilter.all, adminTr.schoolFilter.unassigned, 'Ankara Lisesi']);
  });

  it('unassignedLabel_Student_ShowsNoSchool', () => {
    create('all', undefined, 'unassignedStudent');
    expect(optionTexts()).toEqual([adminTr.schoolFilter.all, adminTr.schoolFilter.unassignedStudent, 'Ankara Lisesi']);
  });
});

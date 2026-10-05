import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatTabGroup } from '@angular/material/tabs';
import { ActivatedRoute, Params, convertToParamMap } from '@angular/router';

import { ADMIN_HOME_TABS, AdminHomeComponent } from './admin-home.component';
import { TaxonomyManagerComponent } from '../taxonomy-manager/taxonomy-manager.component';
import { SchoolManagerComponent } from '../school-manager/school-manager.component';
import { ClassifierCacheComponent } from '../classifier-cache/classifier-cache.component';
import { TeacherApprovalsComponent } from '../teacher-approvals/teacher-approvals.component';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

// Issue #281: URL'de okul filtresi varsa admin-home Okullar sekmesiyle açılır.
describe('AdminHomeComponent', () => {
  function create(queryParams: Params = {}) {
    TestBed.configureTestingModule({
      imports: [
        AdminHomeComponent,
        translocoTestingModule({
          langs: { 'admin/tr': adminTr },
          translocoConfig: { scopes: { keepCasing: true } },
        }),
      ],
      providers: [
        provideNoopAnimations(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } } },
      ],
    });
    // Sekme içerikleri bu testin konusu değil; ağır bağımlılıklarıyla birlikte çıkarılır.
    TestBed.overrideComponent(AdminHomeComponent, {
      remove: {
        imports: [TaxonomyManagerComponent, SchoolManagerComponent, ClassifierCacheComponent, TeacherApprovalsComponent],
      },
      add: { schemas: [CUSTOM_ELEMENTS_SCHEMA] },
    });
    const fixture = TestBed.createComponent(AdminHomeComponent);
    fixture.detectChanges();
    const group = fixture.debugElement.query(By.directive(MatTabGroup)).componentInstance as MatTabGroup;
    return { fixture, group };
  }

  const schoolsIndex = ADMIN_HOME_TABS.indexOf('schools');

  it('noQueryParams_SelectsDefaultFirstTab', () => {
    const { group } = create();

    expect(group.selectedIndex).toBe(0);
  });

  it('schoolSearchParam_SelectsSchoolsTab', () => {
    const { group, fixture } = create({ q: 'x' });

    expect(group.selectedIndex).toBe(schoolsIndex);
    const labels = fixture.debugElement.queryAll(By.css('[role="tab"]')).map((d) => d.nativeElement.textContent.trim());
    expect(labels[schoolsIndex]).toBe(adminTr.home.tabs.schools);
  });

  it('provinceParam_SelectsSchoolsTab', () => {
    const { group } = create({ provinceId: '6' });

    expect(group.selectedIndex).toBe(schoolsIndex);
  });

  it('invalidOrUnrelatedParams_KeepDefaultTab', () => {
    expect(create({ provinceId: 'abc', page: '2' }).group.selectedIndex).toBe(0);
  });

  it('userChangesTab_IsNotForcedBackToSchools', () => {
    const { group, fixture } = create({ q: 'x' });

    group.selectedIndex = 0;
    fixture.detectChanges();

    expect(group.selectedIndex).toBe(0);
  });

  it('tabs_RenderOneTabPerDeclaredEntry', () => {
    const { fixture } = create();

    expect(fixture.debugElement.queryAll(By.css('[role="tab"]')).length).toBe(ADMIN_HOME_TABS.length);
  });
});

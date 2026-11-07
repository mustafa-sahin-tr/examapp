import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRouteSnapshot, Params, Router, UrlTree, convertToParamMap, provideRouter } from '@angular/router';

import { ADMIN_HOME_TABS, AdminHomeComponent } from './admin-home.component';
import { TaxonomyManagerComponent } from '../taxonomy-manager/taxonomy-manager.component';
import { ClassifierStatusComponent } from '../classifier-status/classifier-status.component';
import { adminSchoolFilterRedirectGuard } from '../../../shared/guards/admin-school-filter-redirect.guard';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

// Issue #897: Yönetim yalnız Taksonomi ve Sınıflandırıcı; okullar ve başvurular menüde.
describe('AdminHomeComponent', () => {
    function create() {
        TestBed.configureTestingModule({
            imports: [
                AdminHomeComponent,
                translocoTestingModule({
                    langs: { 'admin/tr': adminTr },
                    translocoConfig: { scopes: { keepCasing: true } },
                }),
            ],
            providers: [provideNoopAnimations()],
        });
        // Sekme içerikleri bu testin konusu değil; ağır bağımlılıklarıyla birlikte çıkarılır.
        TestBed.overrideComponent(AdminHomeComponent, {
            remove: { imports: [TaxonomyManagerComponent, ClassifierStatusComponent] },
            add: { schemas: [CUSTOM_ELEMENTS_SCHEMA] },
        });
        const fixture = TestBed.createComponent(AdminHomeComponent);
        fixture.detectChanges();
        return fixture;
    }

    it('tabs_AreTaxonomyAndClassifierOnly_issue897', () => {
        const fixture = create();
        const labels = fixture.debugElement.queryAll(By.css('[role="tab"]')).map((d) => d.nativeElement.textContent.trim());

        expect(ADMIN_HOME_TABS).toEqual(['taxonomy', 'classifier']);
        expect(labels).toEqual([adminTr.home.tabs.taxonomy, adminTr.home.tabs.classifier]);
        expect(fixture.nativeElement.querySelector('h1').textContent.trim()).toBe('Taksonomi');
    });

    describe('adminSchoolFilterRedirectGuard (#281 bağlantıları)', () => {
        function run(queryParams: Params): boolean | UrlTree {
            TestBed.configureTestingModule({ providers: [provideRouter([])] });
            const route = { queryParamMap: convertToParamMap(queryParams), queryParams } as unknown as ActivatedRouteSnapshot;
            return TestBed.runInInjectionContext(() => adminSchoolFilterRedirectGuard(route, {} as never)) as boolean | UrlTree;
        }

        it('schoolFilter_GoesToTheSchoolsPage_WithTheSameFilter', () => {
            const result = run({ q: 'ata', provinceId: '6' });

            expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/admin/schools?q=ata&provinceId=6');
        });

        it('noOrInvalidFilter_OpensTheAdminPage', () => {
            expect(run({})).toBe(true);
            TestBed.resetTestingModule();
            expect(run({ provinceId: 'abc', page: '2' })).toBe(true);
        });
    });
});

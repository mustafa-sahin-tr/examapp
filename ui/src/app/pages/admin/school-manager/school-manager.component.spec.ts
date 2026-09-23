import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { SchoolManagerComponent } from './school-manager.component';
import { AdminService } from '../../../services/admin.service';
import { ApiResult, School } from '../../../models/taxonomy';
import { ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import { routes } from '../../../app.routes';
import { authGuard } from '../../../shared/guards/auth.guard';
import { adminGuard } from '../../../shared/guards/admin.guard';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

// Issue #150: okul testleri taxonomy-manager.component.spec.ts'ten taşındı.
describe('SchoolManagerComponent', () => {
  let fixture: ComponentFixture<SchoolManagerComponent>;
  let component: SchoolManagerComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const okResult: ApiResult = { success: true, message: 'İşlem başarılı' };

  const schools: School[] = [
    {
      id: 1,
      name: 'Atatürk İlkokulu',
      provinceId: 6,
      provinceName: 'Ankara',
      districtId: 601,
      districtName: 'Çankaya',
      addressLine: null,
    },
  ];

  function dialogRef(result: boolean): MatDialogRef<unknown, boolean> {
    return { afterClosed: () => of(result) } as MatDialogRef<unknown, boolean>;
  }

  function configure(): ComponentFixture<SchoolManagerComponent> {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', [
      'getSchools',
      'getProvinces',
      'getDistricts',
      'createSchool',
      'updateSchool',
      'deleteSchool',
    ]);
    adminService.getSchools.and.returnValue(of(schools));
    adminService.getProvinces.and.returnValue(of([{ id: 6, name: 'Ankara' }, { id: 35, name: 'İzmir' }]));
    adminService.getDistricts.and.returnValue(of([{ id: 601, name: 'Çankaya', provinceId: 6 }]));

    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue(dialogRef(true));

    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [
        SchoolManagerComponent,
        translocoTestingModule({
          langs: { 'admin/tr': adminTr },
          // app.config.ts ile aynı: tireli scope önekleri camelCase'e çevrilmez.
          translocoConfig: { scopes: { keepCasing: true } },
        }),
      ],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        provideNoopAnimations(),
      ],
    });

    // Komponent MatDialogModule/MatSnackBarModule import ettiği için standalone injector gerçek
    // servisleri sağlar ve root-level mock'lar gölgelenir; mock'ları komponent seviyesinde ver.
    TestBed.overrideComponent(SchoolManagerComponent, {
      add: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snackBar },
        ],
      },
    });

    return TestBed.createComponent(SchoolManagerComponent);
  }

  function init(): void {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  // ── Route ────────────────────────────────────────────────────────────────

  it('routes_AdminSchoolsPath_IsGuardedByAuthAndAdminGuard', () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const route = layoutRoute?.children?.find((r) => r.path === 'admin/schools');
    const adminRoute = layoutRoute?.children?.find((r) => r.path === 'admin');

    expect(route).withContext('route tanımı bulunamadı').toBeDefined();
    expect(route?.canActivate).toEqual([authGuard, adminGuard]);
    expect(route?.canActivate).toEqual(adminRoute?.canActivate);
  });

  it('routes_AdminSchoolsPath_LazyLoadsSchoolManagerComponent', async () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const route = layoutRoute?.children?.find((r) => r.path === 'admin/schools');

    expect(route?.loadComponent).toBeDefined();
    expect(await route!.loadComponent!()).toBe(SchoolManagerComponent);
  });

  // ── İlk yükleme / liste ───────────────────────────────────────────────────

  it('init_LoadsSchoolsAndProvinces', () => {
    init();

    expect(adminService.getSchools).toHaveBeenCalledTimes(1);
    expect(adminService.getProvinces).toHaveBeenCalledTimes(1);
    expect(component.schools()).toEqual(schools);
  });

  it('schoolRow_WithProvinceAndDistrict_ShowsCombinedLocation', () => {
    init();

    const meta: HTMLElement = fixture.nativeElement.querySelector('.schools-list .meta');
    expect(meta.textContent?.trim()).toBe('Ankara / Çankaya');
  });

  it('schools_EmptyList_ShowsEmptyState', () => {
    fixture = configure();
    adminService.getSchools.and.returnValue(of([]));
    fixture.detectChanges();

    const empty: HTMLElement = fixture.nativeElement.querySelector('.schools-section .empty-state');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('Henüz okul eklenmemiş');
  });

  // ── Ekle ──────────────────────────────────────────────────────────────────

  it('addSchool_WithProvinceDistrictAddress_CallsCreateSchoolWithTrimmedFields', async () => {
    init();

    adminService.createSchool.and.returnValue(of(okResult));
    component.newSchoolName = '  Cumhuriyet Ortaokulu ';
    component.onNewSchoolProvinceChange(6);
    component.newSchoolDistrictId.set(601);
    component.newSchoolAddressLine = ' Atatürk Bulvarı No:1 ';

    await component.addSchool();

    expect(adminService.getDistricts).toHaveBeenCalledOnceWith(6);
    expect(adminService.createSchool).toHaveBeenCalledOnceWith({
      name: 'Cumhuriyet Ortaokulu',
      provinceId: 6,
      districtId: 601,
      addressLine: 'Atatürk Bulvarı No:1',
    });
    expect(component.newSchoolName).toBe('');
    expect(component.newSchoolProvinceId()).toBeNull();
    expect(component.newSchoolDistrictId()).toBeNull();
    expect(component.newSchoolAddressLine).toBe('');
    // Başarılı işlem sonrası liste yenilenir.
    expect(adminService.getSchools).toHaveBeenCalledTimes(2);
  });

  it('addSchool_NoLocation_SendsNulls', async () => {
    init();

    adminService.createSchool.and.returnValue(of(okResult));
    component.newSchoolName = 'Yeni Okul';

    await component.addSchool();

    expect(adminService.createSchool).toHaveBeenCalledOnceWith({
      name: 'Yeni Okul',
      provinceId: null,
      districtId: null,
      addressLine: null,
    });
  });

  it('addSchool_BlankName_DoesNotCallCreateSchool', async () => {
    init();
    component.newSchoolName = '   ';

    await component.addSchool();

    expect(adminService.createSchool).not.toHaveBeenCalled();
  });

  it('onNewSchoolProvinceChange_ProvinceChanges_ResetsDistrictSelection', () => {
    init();

    component.onNewSchoolProvinceChange(6);
    component.newSchoolDistrictId.set(601);
    component.onNewSchoolProvinceChange(35);

    expect(component.newSchoolDistrictId()).toBeNull();
    expect(adminService.getDistricts).toHaveBeenCalledWith(35);
  });

  it('addSchool_BackendRejects_KeepsFormValues', async () => {
    init();

    adminService.createSchool.and.returnValue(
      throwError(() => ({ error: { message: 'İlçe seçildiğinde il de seçilmelidir.' } }))
    );
    component.newSchoolName = 'Yeni Okul';
    component.newSchoolAddressLine = 'Adres';

    await component.addSchool();

    expect(component.newSchoolName).toBe('Yeni Okul');
    expect(component.newSchoolAddressLine).toBe('Adres');
    expect(snackBar.open).toHaveBeenCalledWith(
      'İlçe seçildiğinde il de seçilmelidir.',
      'Kapat',
      jasmine.anything()
    );
  });

  // ── Düzenle ───────────────────────────────────────────────────────────────

  it('saveEdit_CallsUpdateSchoolWithLocationFields', async () => {
    init();
    adminService.updateSchool.and.returnValue(of(okResult));

    component.startEdit(schools[0]);
    expect(component.editProvinceId()).toBe(6);
    expect(component.editDistrictId()).toBe(601);
    expect(adminService.getDistricts).toHaveBeenCalledOnceWith(6);

    component.editName = 'Atatürk İlkokulu 2';
    component.editAddressLine = ' Yeni adres ';

    await component.saveEdit(schools[0]);

    expect(adminService.updateSchool).toHaveBeenCalledOnceWith(1, {
      name: 'Atatürk İlkokulu 2',
      provinceId: 6,
      districtId: 601,
      addressLine: 'Yeni adres',
    });
    expect(component.editingId()).toBeNull();
  });

  it('saveEdit_BackendRejects_KeepsEditRowOpen', async () => {
    init();
    adminService.updateSchool.and.returnValue(throwError(() => ({ error: { message: 'Geçersiz ilçe' } })));

    component.startEdit(schools[0]);
    await component.saveEdit(schools[0]);

    expect(component.isEditing(1)).toBeTrue();
  });

  it('saveEdit_BlankName_DoesNotCallUpdateSchool', async () => {
    init();

    component.startEdit(schools[0]);
    component.editName = '   ';
    await component.saveEdit(schools[0]);

    expect(adminService.updateSchool).not.toHaveBeenCalled();
    expect(component.isEditing(1)).toBeTrue();
  });

  it('startEdit_RendersInlineEditRowForThatSchool', () => {
    init();

    component.startEdit(schools[0]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.schools-list .edit-row')).toBeTruthy();
  });

  // ── Sil ───────────────────────────────────────────────────────────────────

  it('remove_ConfirmDialogAccepted_CallsDeleteSchool', async () => {
    init();
    adminService.deleteSchool.and.returnValue(of(okResult));

    await component.remove(schools[0]);

    const data = dialog.open.calls.mostRecent().args[1]?.data as ConfirmDialogData;
    expect(data.title).toBe('okul sil');
    expect(data.message).toBe('"Atatürk İlkokulu" okulunu silmek istediğine emin misin?');
    expect(data.confirmText).toBe('Sil');
    expect(adminService.deleteSchool).toHaveBeenCalledOnceWith(1);
  });

  it('remove_ConfirmDialogRejected_DoesNotCallDeleteSchool', async () => {
    init();
    dialog.open.and.returnValue(dialogRef(false));

    await component.remove(schools[0]);

    expect(adminService.deleteSchool).not.toHaveBeenCalled();
  });

  // ── schoolLocation ────────────────────────────────────────────────────────

  it('schoolLocation_ProvinceAndDistrictPresent_JoinsBothWithSlash', () => {
    init();

    expect(component.schoolLocation(schools[0])).toBe('Ankara / Çankaya');
  });

  it('schoolLocation_OnlyProvincePresent_ReturnsProvinceNameOnly', () => {
    init();

    const school: School = { id: 2, name: 'İl Olan Okul', provinceId: 6, provinceName: 'Ankara', districtId: null, districtName: null, addressLine: null };

    expect(component.schoolLocation(school)).toBe('Ankara');
  });

  it('schoolLocation_NoProvinceOrDistrict_ReturnsEmptyString', () => {
    init();

    const school: School = { id: 3, name: 'Adressiz Okul', provinceId: null, provinceName: null, districtId: null, districtName: null, addressLine: null };

    expect(component.schoolLocation(school)).toBe('');
  });

  // ── Hata durumu ───────────────────────────────────────────────────────────

  it('loadSchools_RequestFails_ShowsSchoolsErrorBanner', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getSchools.and.returnValue(throwError(() => new Error('boom')));

    fixture.detectChanges();

    expect(component.schoolsError()).toBe('Okullar yüklenemedi');
    const errorBox: HTMLElement = fixture.nativeElement.querySelector('.schools-section .state-box--error');
    expect(errorBox).toBeTruthy();
    expect(errorBox.querySelector('button')).toBeTruthy();
  });

  it('schoolsRetryButtonClick_AfterLoadSchoolsError_CallsLoadSchoolsAgain', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getSchools.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    expect(adminService.getSchools).toHaveBeenCalledTimes(1);

    adminService.getSchools.and.returnValue(of(schools));
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.schools-section .state-box--error button');
    retry.click();
    fixture.detectChanges();

    expect(adminService.getSchools).toHaveBeenCalledTimes(2);
    expect(component.schoolsError()).toBeNull();
  });

  it('ensureDistricts_RequestFails_ShowsDistrictsSnackbar', () => {
    init();
    adminService.getDistricts.and.returnValue(throwError(() => new Error('boom')));

    component.onNewSchoolProvinceChange(35);

    expect(snackBar.open).toHaveBeenCalledWith('İlçeler yüklenemedi', 'Kapat', jasmine.anything());
    expect(component.newSchoolDistrictsLoading()).toBeFalse();
  });

  it('loadProvinces_RequestFails_ShowsLocationErrorWithRetry', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getProvinces.and.returnValue(throwError(() => new Error('boom')));

    fixture.detectChanges();

    expect(component.provincesError()).toBe('İller yüklenemedi');
    const box: HTMLElement = fixture.nativeElement.querySelector('.location-error');
    expect(box).toBeTruthy();
    expect(box.querySelector('button')).toBeTruthy();
  });

  // ── Sayfa başlığı (doğrudan route vs admin-home sekmesi) ──────────────────

  it('render_StandaloneRoute_ShowsPageHeaderWithPadding', () => {
    init();

    const root: HTMLElement = fixture.nativeElement.querySelector('.schools');
    expect(root.classList).toContain('al');
    const h1: HTMLElement = fixture.nativeElement.querySelector('.al__head h1');
    expect(h1.textContent?.trim()).toBe('Okul Yönetimi');
    expect(fixture.nativeElement.querySelector('.al__head p').textContent).toContain('okulları');
  });

  it('render_EmbeddedInAdminHome_HidesPageHeader', () => {
    fixture = configure();
    fixture.componentRef.setInput('embedded', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.al__head')).toBeNull();
    expect(fixture.nativeElement.querySelector('h1')).toBeNull();
    expect(fixture.nativeElement.querySelector('.schools').classList).not.toContain('al');
    // Liste kartı gömülüyken de görünür.
    expect(fixture.nativeElement.querySelector('.schools-section')).toBeTruthy();
  });
});

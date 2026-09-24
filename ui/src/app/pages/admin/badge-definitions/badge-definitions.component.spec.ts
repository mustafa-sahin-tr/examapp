import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { BadgeDefinitionsComponent } from './badge-definitions.component';
import { BadgeDefinitionAdminService } from '../../../services/badge-definition-admin.service';
import { BadgeDefinitionAdmin } from '../../../models/badge-definition-admin.model';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import { BadgeDefinitionDialogComponent } from './badge-definition-dialog/badge-definition-dialog.component';
import { routes } from '../../../app.routes';
import { authGuard } from '../../../shared/guards/auth.guard';
import { adminGuard } from '../../../shared/guards/admin.guard';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

function dto(overrides: Partial<BadgeDefinitionAdmin> = {}): BadgeDefinitionAdmin {
  return {
    id: 'b1',
    code: 'question-hunter-1',
    name: 'Soru Avcısı 1',
    description: '',
    iconUrl: 'achievements/q.svg',
    category: 'Çözüm',
    ruleType: 'AnswerCount',
    ruleConfigJson: '{"target":10}',
    pathKey: null,
    pathName: null,
    pathOrder: null,
    isActive: true,
    createdBy: 'sub-1',
    createdByName: 'admin1',
    createdAtUtc: '2026-09-01T10:00:00Z',
    updatedBy: null,
    updatedByName: null,
    updatedAtUtc: null,
    ...overrides,
  };
}

describe('BadgeDefinitionsComponent', () => {
  let fixture: ComponentFixture<BadgeDefinitionsComponent>;
  let component: BadgeDefinitionsComponent;
  let service: jasmine.SpyObj<BadgeDefinitionAdminService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snack: jasmine.SpyObj<MatSnackBar>;

  function dialogRef<T>(result: T): MatDialogRef<unknown, T> {
    return { afterClosed: () => of(result) } as MatDialogRef<unknown, T>;
  }

  function init(items: BadgeDefinitionAdmin[] = [dto()]): void {
    service = jasmine.createSpyObj<BadgeDefinitionAdminService>('BadgeDefinitionAdminService', [
      'list',
      'activate',
      'deactivate',
    ]);
    service.list.and.returnValue(of({ items, totalCount: items.length }));
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    snack = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [BadgeDefinitionsComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [{ provide: BadgeDefinitionAdminService, useValue: service }, provideNoopAnimations()],
    });
    // MatDialogModule/MatSnackBarModule komponentte import edildiği için mock'lar komponent seviyesinde verilir.
    TestBed.overrideComponent(BadgeDefinitionsComponent, {
      add: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snack },
        ],
      },
    });
    fixture = TestBed.createComponent(BadgeDefinitionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function el(testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  // ── route ───────────────────────────────────────────────────────────────

  it('route_IsGuardedLikeOtherAdminPages_AndLazyLoads', async () => {
    const layout = routes.find((r) => Array.isArray(r.children));
    const route = layout?.children?.find((r) => r.path === 'admin/badge-definitions');
    expect(route?.canActivate).toEqual([authGuard, adminGuard]);
    expect(await route!.loadComponent!()).toBe(BadgeDefinitionsComponent);
  });

  // ── liste / filtre ──────────────────────────────────────────────────────

  it('init_LoadsActiveOnlyFirstPage_RendersRuleSummaryAndStatus', () => {
    init([dto(), dto({ id: 'b2', ruleType: 'SubjectAnswerCount', ruleConfigJson: '{"subjectName":"Matematik","target":100}' })]);

    expect(service.list).toHaveBeenCalledOnceWith(false, 0, 50);
    const summaries = [...fixture.nativeElement.querySelectorAll('[data-testid="rule-summary"]')].map((e: Element) =>
      e.textContent?.trim()
    );
    expect(summaries).toEqual(['10 soru çöz', 'Matematik dersinde 100 soru çöz']);
    expect(el('status-chip')?.textContent?.trim()).toBe('Aktif');
    expect(fixture.nativeElement.textContent).toContain('admin1');
  });

  it('filterAll_ReloadsWithIncludeInactive_FromFirstPage', () => {
    init();
    component.onPage({ pageIndex: 1, pageSize: 50, length: 120 });
    service.list.calls.reset();

    component.onFilterChange('all');

    expect(service.list).toHaveBeenCalledOnceWith(true, 0, 50);
  });

  it('onPage_SendsSkipTake', () => {
    init();
    component.onPage({ pageIndex: 2, pageSize: 25, length: 100 });
    expect(service.list.calls.mostRecent().args).toEqual([false, 50, 25]);
  });

  it('empty_ShowsEmptyState', () => {
    init([]);
    expect(el('empty')?.textContent).toContain(adminTr.badgeDefinitions.empty);
  });

  it('loadError_ShowsErrorWithRetry', () => {
    init();
    service.list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.load();
    fixture.detectChanges();

    expect(el('error')?.textContent).toContain(adminTr.badgeDefinitions.errors.loadFailed);
  });

  // ── aktiflik ────────────────────────────────────────────────────────────

  it('deactivate_AsksConfirmationWithKeptBadgesMessage_ThenRemovesFromActiveList', () => {
    init();
    dialog.open.and.returnValue(dialogRef(true));
    service.deactivate.and.returnValue(of(dto({ isActive: false })));

    component.toggleActive(component.rows()[0]);

    const [cmp, config] = dialog.open.calls.mostRecent().args;
    expect(cmp).toBe(ConfirmDialogComponent);
    expect((config?.data as ConfirmDialogData).message).toContain('kazanılmış rozetler kalır');
    expect(service.deactivate).toHaveBeenCalledOnceWith('b1');
    expect(component.rows().length).toBe(0);
    expect(component.totalCount()).toBe(0);
  });

  it('deactivate_Cancelled_DoesNotCallApi', () => {
    init();
    dialog.open.and.returnValue(dialogRef(false));

    component.toggleActive(component.rows()[0]);

    expect(service.deactivate).not.toHaveBeenCalled();
  });

  it('deactivate_InAllFilter_KeepsRowAsInactive', () => {
    init();
    component.onFilterChange('all');
    dialog.open.and.returnValue(dialogRef(true));
    service.deactivate.and.returnValue(of(dto({ isActive: false })));

    component.toggleActive(component.rows()[0]);
    fixture.detectChanges();

    expect(component.rows()[0].isActive).toBeFalse();
    expect(el('status-chip')?.textContent?.trim()).toBe('Devre dışı');
  });

  it('activate_NoConfirm_UpdatesRow', () => {
    init([dto({ isActive: false })]);
    component.onFilterChange('all');
    service.activate.and.returnValue(of(dto({ isActive: true })));

    component.toggleActive(component.rows()[0]);

    expect(dialog.open).not.toHaveBeenCalled();
    expect(service.activate).toHaveBeenCalledOnceWith('b1');
    expect(component.rows()[0].isActive).toBeTrue();
  });

  it('activate_409ActiveCap_ShowsServerMessage', () => {
    init([dto({ isActive: false })]);
    component.onFilterChange('all');
    service.activate.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { errors: { isActive: ['Üst sınır doldu.'] } } }))
    );

    component.toggleActive(component.rows()[0]);

    expect(snack.open.calls.mostRecent().args[0]).toBe('Üst sınır doldu.');
    expect(component.rows()[0].isActive).toBeFalse();
    expect(component.busyId()).toBeNull();
  });

  // ── oluştur / düzenle ───────────────────────────────────────────────────

  it('create_OpensDialog_PrependsSavedRow', () => {
    init();
    const created = dto({ id: 'b9', name: 'Yeni' });
    dialog.open.and.returnValue(dialogRef(created));

    component.openCreate();

    const [cmp, config] = dialog.open.calls.mostRecent().args;
    expect(cmp).toBe(BadgeDefinitionDialogComponent);
    expect(config?.data).toEqual({ categories: ['Çözüm'] });
    expect(component.rows().map((r) => r.id)).toEqual(['b9', 'b1']);
    expect(component.totalCount()).toBe(2);
  });

  it('edit_PassesDefinition_ReplacesRow', () => {
    init();
    dialog.open.and.returnValue(dialogRef(dto({ name: 'Değişti' })));

    component.openEdit(component.rows()[0]);

    expect(dialog.open.calls.mostRecent().args[1]?.data).toEqual(jasmine.objectContaining({ definition: dto() }));
    expect(component.rows()[0].name).toBe('Değişti');
  });
});

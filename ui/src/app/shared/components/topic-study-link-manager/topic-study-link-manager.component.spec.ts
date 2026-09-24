import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import { TopicStudyLinkManagerComponent } from './topic-study-link-manager.component';
import { StudyLinkService } from '../../../services/study-link.service';
import { AuthService } from '../../../services/auth.service';
import { StudyLink, StudyLinkListResponse } from '../../../models/study-link';
import { StudyLinkDialogComponent } from '../study-link-dialog/study-link-dialog.component';
import { translocoTestingModule } from '../../testing/transloco-testing';
import studyLinksTr from '../../../../../public/i18n/study-links/tr.json';

function makeLink(id: number, sortOrder: number, isActive = true, createdByUserId = 1): StudyLink {
  return {
    id,
    topicId: 1,
    subTopicId: 2,
    title: `Link ${id}`,
    url: `https://example.com/${id}`,
    sourceType: id % 2 ? 'YouTube' : 'Other',
    sortOrder,
    isActive,
    createdByUserId,
    createdByName: 'Admin',
    createdByRole: 'Admin',
    createTime: '2026-01-01T00:00:00Z',
    updatedByUserId: null,
    updatedByName: null,
    updateTime: null,
  };
}

function listOf(items: StudyLink[], activeCount = items.filter((l) => l.isActive).length): StudyLinkListResponse {
  return { success: true, items, totalCount: items.length, activeCount, maxActiveLinks: 7 };
}

describe('TopicStudyLinkManagerComponent (issue #61)', () => {
  const texts = studyLinksTr.manager;
  let fixture: ComponentFixture<TopicStudyLinkManagerComponent>;
  let component: TopicStudyLinkManagerComponent;
  let service: jasmine.SpyObj<StudyLinkService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snack: jasmine.SpyObj<MatSnackBar>;

  /** Varsayılan kullanıcı Admin (id 1); öğretmen sahiplik testleri `{ admin: false, userId }` verir. */
  function configure(
    initial: StudyLinkListResponse | (() => ReturnType<StudyLinkService['list']>),
    scope: { topicId?: number; subTopicId?: number } = { subTopicId: 2 },
    user: { admin: boolean; userId: number } = { admin: true, userId: 1 }
  ) {
    service = jasmine.createSpyObj<StudyLinkService>('StudyLinkService', ['list', 'update', 'delete', 'reorder', 'create']);
    if (typeof initial === 'function') service.list.and.callFake(initial);
    else service.list.and.returnValue(of(initial));
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    snack = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [TopicStudyLinkManagerComponent, translocoTestingModule({ langs: { 'study-links/tr': studyLinksTr } })],
      providers: [
        provideNoopAnimations(),
        { provide: StudyLinkService, useValue: service },
        {
          provide: AuthService,
          useValue: {
            hasRealmRole: (role: string) => role === (user.admin ? 'Admin' : 'Teacher'),
            hasRole: (role: string) => role === (user.admin ? 'Admin' : 'Teacher'),
            user: signal({ id: user.userId }),
          },
        },
      ],
    });
    // Komponent MatDialogModule/MatSnackBarModule import ettiği için mock'lar komponent seviyesinde verilir.
    TestBed.overrideComponent(TopicStudyLinkManagerComponent, {
      add: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snack },
        ],
      },
    });
    fixture = TestBed.createComponent(TopicStudyLinkManagerComponent);
    component = fixture.componentInstance;
    if (scope.topicId != null) fixture.componentRef.setInput('topicId', scope.topicId);
    if (scope.subTopicId != null) fixture.componentRef.setInput('subTopicId', scope.subTopicId);
    fixture.componentRef.setInput('scopeName', 'Kesirler');
    fixture.detectChanges();
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function q<T extends HTMLElement = HTMLElement>(selector: string): T | null {
    return el().querySelector<T>(selector);
  }

  function rows(): HTMLElement[] {
    return Array.from(el().querySelectorAll<HTMLElement>('[data-testid="link-row"]'));
  }

  it('noScope_ShowsHintAndSendsNoRequest', () => {
    configure(listOf([]), {});
    expect(service.list).not.toHaveBeenCalled();
    expect(el().textContent).toContain(texts.noScopeTitle);
    expect(q('[data-testid="add-link"]')).toBeNull();
  });

  it('subTopicScope_LoadsSortedLinksAndShowsActiveCounter', () => {
    configure(listOf([makeLink(1, 2), makeLink(2, 0), makeLink(3, 1, false)]));

    expect(service.list).toHaveBeenCalledWith({ subTopicId: 2, includeInactive: true, skip: 0, take: 50 });
    expect(rows().map((r) => r.querySelector('.sl-title')?.textContent?.trim())).toEqual(['Link 2', 'Link 3', 'Link 1']);
    expect(q('[data-testid="active-counter"]')?.textContent).toContain('2 / 7 aktif');
    expect(q<HTMLButtonElement>('[data-testid="add-link"]')?.disabled).toBeFalse();
    expect(q('[data-testid="limit-warning"]')?.textContent?.trim()).toBe('');
  });

  it('topicOnlyScope_RequestsTopicLevelLinks', () => {
    configure(listOf([]), { topicId: 9 });
    expect(service.list).toHaveBeenCalledWith({ topicId: 9, includeInactive: true, skip: 0, take: 50 });
    expect(el().textContent).toContain(texts.topicLevelHint);
    expect(q('[data-testid="empty"]')).toBeTruthy();
  });

  it('limitReached_DisablesAddAndShowsPersistentWarning', () => {
    const links = Array.from({ length: 7 }, (_, i) => makeLink(i + 1, i)).concat(makeLink(8, 7, false));
    configure(listOf(links));

    expect(q<HTMLButtonElement>('[data-testid="add-link"]')?.disabled).toBeTrue();
    const warning = q('[data-testid="limit-warning"]')!;
    expect(warning.getAttribute('role')).toBe('status');
    expect(warning.getAttribute('aria-live')).toBe('polite');
    expect(warning.textContent).toContain('Aktif link sınırına ulaşıldı (7/7)');
    expect(q('[data-testid="active-counter"]')?.textContent).toContain('7 / 7 aktif');

    component.openCreate();
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('openCreate_OpensDialogWithScopeAndReloadsOnSave', async () => {
    configure(listOf([makeLink(1, 0)]));
    dialog.open.and.returnValue({ afterClosed: () => of(makeLink(2, 1)) } as never);

    await component.openCreate();
    fixture.detectChanges();

    expect(dialog.open).toHaveBeenCalledWith(
      StudyLinkDialogComponent,
      jasmine.objectContaining({
        data: jasmine.objectContaining({ scope: { subTopicId: 2 }, scopeName: 'Kesirler', activeLimitReached: false, maxActiveLinks: 7 }),
      })
    );
    expect(service.list).toHaveBeenCalledTimes(2);
    expect(snack.open).toHaveBeenCalledWith(texts.created, texts.close, jasmine.any(Object));
  });

  it('toggle_Deactivate_SendsUpdateWithIsActiveFalseAndReloads', () => {
    const link = makeLink(1, 0);
    configure(listOf([link]));
    service.update.and.returnValue(of({ ...link, isActive: false }));
    service.list.and.returnValue(of(listOf([{ ...link, isActive: false }])));

    q<HTMLButtonElement>('[data-testid="active-toggle"] button')!.click();
    fixture.detectChanges();

    expect(service.update).toHaveBeenCalledWith(1, {
      title: link.title,
      url: link.url,
      sourceType: link.sourceType,
      isActive: false,
    });
    expect(service.list).toHaveBeenCalledTimes(2);
    expect(q('[data-testid="active-counter"]')?.textContent).toContain('0 / 7 aktif');
    expect(snack.open).toHaveBeenCalledWith(texts.deactivated, texts.close, jasmine.any(Object));
  });

  it('toggle_Activate409_ShowsServerMessage', () => {
    const inactive = makeLink(1, 0, false);
    configure(listOf([inactive]));
    service.update.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { success: false, conflict: true, errorCode: 'ActiveLimitReached', message: 'En fazla 7 aktif link olabilir.' },
          })
      )
    );

    q<HTMLButtonElement>('[data-testid="active-toggle"] button')!.click();
    fixture.detectChanges();

    expect(service.update).toHaveBeenCalledWith(1, jasmine.objectContaining({ isActive: true }));
    expect(snack.open).toHaveBeenCalledWith('En fazla 7 aktif link olabilir.', texts.close, jasmine.any(Object));
    expect(q<HTMLButtonElement>('[data-testid="active-toggle"] button')!.getAttribute('aria-checked')).toBe('false');
    expect(component.busy()).toBeFalse();
  });

  it('inactiveToggle_LimitReached_IsDisabled', () => {
    const links = Array.from({ length: 7 }, (_, i) => makeLink(i + 1, i)).concat(makeLink(8, 7, false));
    configure(listOf(links));
    const toggles = el().querySelectorAll<HTMLButtonElement>('[data-testid="active-toggle"] button');
    expect(toggles[7].disabled).toBeTrue();
    expect(toggles[0].disabled).toBeFalse();
  });

  it('moveDown_CallsReorderWithFullScopeOrderAndAppliesResponse', () => {
    const [a, b, c] = [makeLink(1, 0), makeLink(2, 1), makeLink(3, 2)];
    configure(listOf([a, b, c]));
    service.reorder.and.returnValue(of(listOf([{ ...b, sortOrder: 0 }, { ...a, sortOrder: 1 }, c])));

    q<HTMLButtonElement>('[data-testid="move-down"]')!.click();
    fixture.detectChanges();

    expect(service.reorder).toHaveBeenCalledWith({
      subTopicId: 2,
      items: [
        { id: 2, sortOrder: 0 },
        { id: 1, sortOrder: 1 },
        { id: 3, sortOrder: 2 },
      ],
    });
    expect(rows().map((r) => r.querySelector('.sl-title')?.textContent?.trim())).toEqual(['Link 2', 'Link 1', 'Link 3']);
  });

  it('reorder_Error_RevertsOrderAndShowsMessage', () => {
    configure(listOf([makeLink(1, 0), makeLink(2, 1)]));
    service.reorder.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500, error: null })));

    component.moveUp(1);
    fixture.detectChanges();

    expect(rows().map((r) => r.querySelector('.sl-title')?.textContent?.trim())).toEqual(['Link 1', 'Link 2']);
    expect(snack.open).toHaveBeenCalledWith(texts.actionFailed, texts.close, jasmine.any(Object));
  });

  it('moveButtons_FirstUpAndLastDownDisabled', () => {
    configure(listOf([makeLink(1, 0), makeLink(2, 1)]));
    const ups = el().querySelectorAll<HTMLButtonElement>('[data-testid="move-up"]');
    const downs = el().querySelectorAll<HTMLButtonElement>('[data-testid="move-down"]');
    expect(ups[0].disabled).toBeTrue();
    expect(downs[1].disabled).toBeTrue();
    expect(ups[1].disabled).toBeFalse();
  });

  it('delete_Confirmed_DeletesAndReloads', async () => {
    configure(listOf([makeLink(1, 0)]));
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as never);
    service.delete.and.returnValue(of(void 0));

    await component.remove(component.links()[0]);
    fixture.detectChanges();

    expect(service.delete).toHaveBeenCalledWith(1);
    expect(service.list).toHaveBeenCalledTimes(2);
  });

  it('loadError_ShowsBannerAndRetryReloads', () => {
    configure(listOf([]));
    service.list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500, error: null })));
    component.reload();
    fixture.detectChanges();

    expect(q('[data-testid="load-error"]')?.textContent).toContain(texts.loadFailed);
    expect(q<HTMLButtonElement>('[data-testid="add-link"]')?.disabled).toBeTrue();

    service.list.and.returnValue(of(listOf([makeLink(1, 0)])));
    q<HTMLButtonElement>('[data-testid="load-error"] button')!.click();
    fixture.detectChanges();

    expect(q('[data-testid="load-error"]')).toBeNull();
    expect(rows().length).toBe(1);
  });

  // ── Güvenlik (onaylı öğretmen, sahiplik, toplam sınır, rate limit, denetim bilgisi) ──────────────

  function forbidden(errorCode: string, message?: string): HttpErrorResponse {
    return new HttpErrorResponse({ status: 403, error: { success: false, forbidden: true, errorCode, message } });
  }

  it('list403TeacherNotApproved_ShowsServerNoticeInsteadOfErrorAndHidesControls', () => {
    configure(() => throwError(() => forbidden('TeacherNotApproved', 'Öğretmen başvurunuz onay bekliyor.')));

    const notice = q('[data-testid="not-approved"]')!;
    expect(notice.textContent).toContain('Öğretmen başvurunuz onay bekliyor.');
    expect(notice.getAttribute('role')).toBe('status');
    expect(q('[data-testid="load-error"]')).toBeNull();
    expect(q('[data-testid="add-link"]')).toBeNull();
    expect(q('[data-testid="empty"]')).toBeNull();
    expect(q('[data-testid="active-counter"]')).toBeNull();
  });

  it('list403TeacherNotApproved_NoMessage_UsesFallbackText', () => {
    configure(() => throwError(() => forbidden('TeacherNotApproved')), { subTopicId: 2 }, { admin: false, userId: 5 });
    expect(q('[data-testid="not-approved"]')?.textContent).toContain(texts.notApproved);
  });

  it('teacher_NonOwnedLinks_HideEditToggleDeleteButKeepReorder', () => {
    configure(listOf([makeLink(1, 0, true, 5), makeLink(2, 1, true, 9)]), { subTopicId: 2 }, { admin: false, userId: 5 });

    const [own, other] = rows();
    expect(own.querySelector('[data-testid="edit"]')).toBeTruthy();
    expect(own.querySelector('[data-testid="delete"]')).toBeTruthy();
    expect(own.querySelector('[data-testid="active-toggle"]')).toBeTruthy();
    expect(own.querySelector('[data-testid="not-owned"]')).toBeNull();

    expect(other.querySelector('[data-testid="edit"]')).toBeNull();
    expect(other.querySelector('[data-testid="delete"]')).toBeNull();
    expect(other.querySelector('[data-testid="active-toggle"]')).toBeNull();
    expect(other.querySelector('[data-testid="not-owned"]')).toBeTruthy();
    expect(other.querySelector('[data-testid="move-up"]')).toBeTruthy();

    expect(component.canModify(component.links()[1])).toBeFalse();
  });

  it('admin_CanModifyAnyLink', () => {
    configure(listOf([makeLink(1, 0, true, 99)]));
    expect(rows()[0].querySelector('[data-testid="edit"]')).toBeTruthy();
    expect(rows()[0].querySelector('[data-testid="not-owned"]')).toBeNull();
  });

  it('delete403NotOwner_ShowsServerMessage', async () => {
    configure(listOf([makeLink(1, 0, true, 5)]), { subTopicId: 2 }, { admin: false, userId: 5 });
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as never);
    service.delete.and.returnValue(throwError(() => forbidden('NotOwner', 'Bu link size ait değil.')));

    await component.remove(component.links()[0]);

    expect(snack.open).toHaveBeenCalledWith('Bu link size ait değil.', texts.close, jasmine.any(Object));
  });

  it('totalLimitReached_DisablesAddAndShowsTotalWarning', () => {
    const links = Array.from({ length: 30 }, (_, i) => makeLink(i + 1, i, i < 3));
    configure(listOf(links));

    expect(q('[data-testid="total-counter"]')?.textContent).toContain('Toplam 30 / 30');
    expect(q<HTMLButtonElement>('[data-testid="add-link"]')?.disabled).toBeTrue();
    expect(q('[data-testid="limit-warning"]')?.textContent).toContain('en fazla 30 link');

    component.openCreate();
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('toggle429EmptyBody_ShowsRetryAfterFallbackInSnackbar', () => {
    const link = makeLink(1, 0);
    configure(listOf([link]));
    service.update.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 429, error: '', headers: new HttpHeaders({ 'Retry-After': '12' }) }))
    );

    q<HTMLButtonElement>('[data-testid="active-toggle"] button')!.click();
    fixture.detectChanges();

    expect(snack.open).toHaveBeenCalledWith(
      'Çok fazla işlem yapıldı. 12 saniye sonra tekrar deneyin.',
      texts.close,
      jasmine.any(Object)
    );
  });

  it('reorder429PlainText_ShowsServerTextInSnackbar', () => {
    configure(listOf([makeLink(1, 0), makeLink(2, 1)]));
    service.reorder.and.returnValue(throwError(() => new HttpErrorResponse({ status: 429, error: 'Çok fazla istek.' })));

    component.moveDown(0);

    expect(snack.open).toHaveBeenCalledWith('Çok fazla istek.', texts.close, jasmine.any(Object));
  });

  it('row_ShowsLastUpdatedByWhenPresentElseCreatedBy', () => {
    const updated: StudyLink = {
      ...makeLink(1, 0),
      updatedByUserId: 7,
      updatedByName: 'Ayşe Öğretmen',
      updateTime: '2026-02-03T10:00:00Z',
    };
    configure(listOf([updated, makeLink(2, 1)]));

    const metas = Array.from(el().querySelectorAll('[data-testid="link-meta"]')).map((m) => m.textContent ?? '');
    expect(metas[0]).toContain('Son güncelleyen: Ayşe Öğretmen');
    expect(metas[1]).toContain('Ekleyen: Admin');
  });

  // ── Kapsam değişimi (code review: bayat satırlarla işlem gönderilmesin) ─────────────────────────

  it('scopeSwitch_BeforeNewListArrives_RowsLockedAndNoReorderWithStaleIds', () => {
    configure(listOf([makeLink(1, 0), makeLink(2, 1)]));
    const pending = new Subject<StudyLinkListResponse>();
    service.list.and.returnValue(pending.asObservable());

    // Girdi değişti, liste efekti henüz çalışmadı: eski satırlar kilitli olmalı.
    fixture.componentRef.setInput('subTopicId', 3);
    expect(component.locked()).toBeTrue();
    component.moveUp(1);
    component.moveDown(0);
    expect(service.reorder).not.toHaveBeenCalled();

    // Yeni kapsamın yüklemesi başladı: önceki satırlar/sayaçlar temizlendi, butonlar da kilitli.
    fixture.detectChanges();
    fixture.detectChanges();
    expect(service.list).toHaveBeenCalledWith(jasmine.objectContaining({ subTopicId: 3 }));
    expect(component.links()).toEqual([]);
    expect(component.totalCount()).toBe(0);
    expect(component.activeCount()).toBe(0);
    expect(rows().length).toBe(0);

    pending.next(listOf([makeLink(5, 0), makeLink(6, 1)]));
    pending.complete();
    fixture.detectChanges();

    service.reorder.and.returnValue(of(listOf([makeLink(6, 0), makeLink(5, 1)])));
    q<HTMLButtonElement>('[data-testid="move-down"]')!.click();
    expect(service.reorder).toHaveBeenCalledOnceWith({
      subTopicId: 3,
      items: [
        { id: 6, sortOrder: 0 },
        { id: 5, sortOrder: 1 },
      ],
    });
  });

  it('whileReloadingSameScope_RowActionsDisabled', () => {
    configure(listOf([makeLink(1, 0), makeLink(2, 1)]));
    const pending = new Subject<StudyLinkListResponse>();
    service.list.and.returnValue(pending.asObservable());

    component.reload();
    fixture.detectChanges();
    fixture.detectChanges();

    expect(component.loading()).toBeTrue();
    expect(rows().length).toBe(2);
    expect(q<HTMLButtonElement>('[data-testid="move-down"]')!.disabled).toBeTrue();
    expect(q<HTMLButtonElement>('[data-testid="edit"]')!.disabled).toBeTrue();
    expect(q<HTMLButtonElement>('[data-testid="delete"]')!.disabled).toBeTrue();
    component.moveDown(0);
    expect(service.reorder).not.toHaveBeenCalled();
  });

  it('scopeCleared_ResetsCounts', () => {
    configure(listOf([makeLink(1, 0), makeLink(2, 1)]));
    expect(component.totalCount()).toBe(2);

    fixture.componentRef.setInput('subTopicId', null);
    fixture.detectChanges();
    fixture.detectChanges();

    expect(component.scope()).toBeNull();
    expect(component.totalCount()).toBe(0);
    expect(component.activeCount()).toBe(0);
    expect(component.links()).toEqual([]);
  });
});

// #545 — Angular 21 control-flow migrasyonu (@if / @else if / @else, @if satır aksiyonu) sonrası davranış sözleşmesi.
import { HttpErrorResponse } from '@angular/common/http';
import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Observable, Subject, of, tap, throwError } from 'rxjs';

import { AssignmentPermissionRequestsComponent } from './assignment-permission-requests.component';
import { WorksheetAccessRequestService } from '../../services/worksheet-access-request.service';
import { LocaleService } from '../../services/locale.service';
import { WorksheetAccessRequest } from '../../models/worksheet-access-request.model';
import { localeDefinitionOf } from '../../models/locale';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import rootTr from '../../../../public/i18n/tr.json';
import requestsTr from '../../../../public/i18n/assignment-requests/tr.json';

const T = requestsTr;

function request(overrides: Partial<WorksheetAccessRequest> = {}): WorksheetAccessRequest {
  return {
    id: 1,
    worksheetName: 'Matematik Sınavı',
    requesterUserId: 5,
    requesterName: 'Ayşe Öğretmen',
    note: null,
    status: 'Pending',
    createTime: '2026-01-15T10:00:00Z',
    ...overrides,
  } as WorksheetAccessRequest;
}

describe('AssignmentPermissionRequestsComponent (#545)', () => {
  let fixture: ComponentFixture<AssignmentPermissionRequestsComponent>;
  let service: {
    incomingRequests: WritableSignal<WorksheetAccessRequest[]>;
    loadIncoming: jasmine.Spy;
    refreshPendingCount: jasmine.Spy;
    approve: jasmine.Spy;
    reject: jasmine.Spy;
  };
  let snack: jasmine.SpyObj<MatSnackBar>;

  const el = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(el().querySelectorAll('tbody tr'));

  // Gerçek servis gibi: `loadIncoming` sonucu `incomingRequests` signal'ına yazılır.
  function setup(loadResult: () => Observable<WorksheetAccessRequest[]>): void {
    const incomingRequests = signal<WorksheetAccessRequest[]>([]);
    service = {
      incomingRequests,
      loadIncoming: jasmine
        .createSpy('loadIncoming')
        .and.callFake(() => loadResult().pipe(tap((rows) => incomingRequests.set(rows)))),
      refreshPendingCount: jasmine.createSpy('refreshPendingCount').and.returnValue(of(0)),
      approve: jasmine.createSpy('approve').and.returnValue(of({ success: true, message: '' })),
      reject: jasmine.createSpy('reject').and.returnValue(of({ success: true, message: '' })),
    };
    snack = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [
        AssignmentPermissionRequestsComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { tr: { ...rootTr, 'assignment-requests': requestsTr }, 'assignment-requests/tr': requestsTr },
        }),
      ],
      providers: [
        { provide: WorksheetAccessRequestService, useValue: service },
        { provide: MatSnackBar, useValue: snack },
        {
          provide: LocaleService,
          useValue: { locale: signal('tr').asReadonly(), localeDefinition: signal(localeDefinitionOf('tr')).asReadonly() },
        },
      ],
    });
    fixture = TestBed.createComponent(AssignmentPermissionRequestsComponent);
    fixture.detectChanges();
  }

  it('template_LoadingInFlight_ShowsSpinnerAndNoTable', () => {
    const pending$ = new Subject<WorksheetAccessRequest[]>();
    setup(() => pending$.asObservable());

    expect(el().querySelector('mat-spinner')).not.toBeNull();
    expect(el().textContent).toContain(T.state.loading);
    expect(el().querySelector('table')).toBeNull();
  });

  it('template_EmptyList_ShowsEmptyStateAndNoTable', () => {
    setup(() => of([]));

    expect(el().textContent).toContain(T.state.empty);
    expect(el().querySelector('table')).toBeNull();
    expect(el().querySelector('mat-spinner')).toBeNull();
  });

  it('template_LoadFails_ShowsServerMessageAndRetryButton', () => {
    setup(() =>
      throwError(() => new HttpErrorResponse({ status: 500, error: { success: false, message: 'Sunucu hatası' } }))
    );

    expect(el().querySelector('.apr__state--error')?.textContent).toContain('Sunucu hatası');
    expect(el().querySelector('table')).toBeNull();
    const retry = Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.includes(rootTr.common.retry));
    expect(retry).toBeDefined();
  });

  it('template_LoadFailsWithoutMessage_ShowsFallbackLoadError', () => {
    setup(() => throwError(() => new HttpErrorResponse({ status: 500, error: null })));

    expect(el().querySelector('.apr__state--error')?.textContent).toContain(T.messages.loadFailed);
  });

  it('template_RequestsLoaded_RendersOneRowPerRequestWithStatusLabel', () => {
    setup(() =>
      of([
        request({ id: 1, status: 'Pending', note: 'Acil' }),
        request({ id: 2, status: 'Approved', requesterName: 'Mehmet' }),
      ])
    );

    expect(rows().length).toBe(2);
    const cells = rows().map((r) => r.textContent ?? '');
    expect(cells[0]).toContain('Acil');
    expect(cells[1]).toContain(T.status.approved);
    expect(cells[1]).toContain('Mehmet');
  });

  it('template_PendingRow_ShowsApproveAndRejectButtons', () => {
    setup(() => of([request({ status: 'Pending' })]));

    const actions = rows()[0].querySelector('.apr__actions');
    expect(actions).not.toBeNull();
    expect(actions?.textContent).toContain(T.actions.approve);
    expect(actions?.textContent).toContain(T.actions.reject);
  });

  it('template_DecidedRow_HidesActionButtonsAndShowsStatusClass', () => {
    setup(() => of([request({ status: 'Rejected' }), request({ id: 2, status: 'Approved' })]));

    expect(rows()[0].querySelector('.apr__actions')).toBeNull();
    expect(rows()[1].querySelector('.apr__actions')).toBeNull();
    expect(rows()[0].querySelector('.apr__chip')?.classList.contains('is-rejected')).toBeTrue();
    expect(rows()[1].querySelector('.apr__chip')?.classList.contains('is-approved')).toBeTrue();
  });

  it('template_MissingNoteAndInvalidDate_ShowsDashPlaceholders', () => {
    setup(() => of([request({ note: null, createTime: 'not-a-date' })]));

    const cells = Array.from(rows()[0].querySelectorAll('td')).map((td) => td.textContent?.trim());
    expect(cells).toContain('—');
    expect(cells.filter((c) => c === '—').length).toBe(2);
  });

  it('approve_PendingRow_CallsServiceAndShowsSuccessSnack', () => {
    setup(() => of([request({ id: 7, status: 'Pending' })]));

    fixture.componentInstance['approve'](request({ id: 7 }));

    expect(service.approve).toHaveBeenCalledWith(7);
    expect(snack.open).toHaveBeenCalledWith(T.messages.approved, T.messages.ok, jasmine.any(Object));
  });
});

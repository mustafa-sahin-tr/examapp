import { TestBed } from '@angular/core/testing';
import { MatSnackBar, MatSnackBarRef, TextOnlySnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';

import { SignalRService } from './signalr.service';
import { AuthService } from './auth.service';
import {
  TeacherApplicationSubmittedPayload,
  TeacherSchoolRequestSubmittedPayload,
} from '../models/teacher-application.model';
import { translocoTestingModule } from '../shared/testing/transloco-testing';
import trTranslations from '../../../public/i18n/tr.json';
import enTranslations from '../../../public/i18n/en.json';

type Handler = (data: unknown) => void;

describe('SignalRService — admin öğretmen bildirimleri', () => {
  let service: SignalRService;
  let handlers: Map<string, Handler>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let router: jasmine.SpyObj<Router>;
  let authService: jasmine.SpyObj<AuthService>;
  let snackAction$: Subject<void>;

  const schoolRequest: TeacherSchoolRequestSubmittedPayload = {
    notificationId: 11,
    teacherId: 4,
    userId: 9,
    requestedSchoolId: 8,
    applicantName: 'Ayşe Yılmaz',
    schoolName: 'İzmir Lisesi',
    title: 'Yeni okul talebi',
    body: 'Ayşe Yılmaz, İzmir Lisesi',
  };

  function setup(isAdmin: boolean): void {
    handlers = new Map<string, Handler>();
    const fakeConnection = {
      on: (name: string, handler: Handler) => handlers.set(name, handler),
      start: () => Promise.resolve(),
    } as unknown as signalR.HubConnection;
    spyOn(signalR.HubConnectionBuilder.prototype, 'build').and.returnValue(fakeConnection);

    snackAction$ = new Subject<void>();
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    snackBar.open.and.returnValue({ onAction: () => snackAction$.asObservable() } as MatSnackBarRef<TextOnlySnackBar>);
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['hasRole']);
    authService.hasRole.and.returnValue(isAdmin);

    TestBed.configureTestingModule({
      imports: [translocoTestingModule()],
      providers: [
        { provide: MatSnackBar, useValue: snackBar },
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: authService },
      ],
    });
    service = TestBed.inject(SignalRService);
    service.startConnection();
  }

  it('teacherSchoolRequestSubmitted_Admin_EmitsStreamAndShowsToastThatNavigatesToApprovals', () => {
    setup(true);
    const received: TeacherSchoolRequestSubmittedPayload[] = [];
    service.teacherSchoolRequestSubmitted$.subscribe((p) => received.push(p));

    handlers.get('TeacherSchoolRequestSubmitted')!(schoolRequest);

    expect(authService.hasRole).toHaveBeenCalledWith('Admin');
    expect(received).toEqual([schoolRequest]);
    expect(snackBar.open).toHaveBeenCalledOnceWith(
      'Yeni okul bağlantısı talebi: Ayşe Yılmaz → İzmir Lisesi',
      trTranslations.common.notifications.goToApplications,
      { duration: 8000 },
    );
    expect(router.navigate).not.toHaveBeenCalled();

    snackAction$.next();

    expect(router.navigate).toHaveBeenCalledOnceWith(['/admin/teacher-approvals']);
  });

  it('teacherSchoolRequestSubmitted_NonAdmin_IsIgnored', () => {
    setup(false);
    const received: TeacherSchoolRequestSubmittedPayload[] = [];
    service.teacherSchoolRequestSubmitted$.subscribe((p) => received.push(p));

    handlers.get('TeacherSchoolRequestSubmitted')!(schoolRequest);

    expect(received).toEqual([]);
    expect(snackBar.open).not.toHaveBeenCalled();
  });

  it('teacherApplicationSubmitted_DoesNotEmitOnSchoolRequestStream', () => {
    setup(true);
    const schoolReceived: unknown[] = [];
    const appReceived: unknown[] = [];
    service.teacherSchoolRequestSubmitted$.subscribe((p) => schoolReceived.push(p));
    service.teacherApplicationSubmitted$.subscribe((p) => appReceived.push(p));
    const application: TeacherApplicationSubmittedPayload = {
      notificationId: 1,
      teacherId: 4,
      userId: 9,
      applicantName: 'Ayşe Yılmaz',
      title: 't',
      body: 'b',
    };

    handlers.get('TeacherApplicationSubmitted')!(application);

    expect(appReceived).toEqual([application]);
    expect(schoolReceived).toEqual([]);
  });

  it('i18n_TeacherSchoolRequest_TrAndEnHaveNameAndSchoolParams', () => {
    for (const text of [
      trTranslations.common.notifications.teacherSchoolRequest,
      enTranslations.common.notifications.teacherSchoolRequest,
    ]) {
      expect(text).toContain('{{name}}');
      expect(text).toContain('{{school}}');
    }
  });
});

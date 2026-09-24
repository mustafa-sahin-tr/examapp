import { inject, Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { AccessRequestUpdate } from '../models/worksheet-access-request.model';
import {
  TeacherApplicationSubmittedPayload,
  TeacherApplicationDecidedPayload,
  TeacherSchoolRequestSubmittedPayload,
} from '../models/teacher-application.model';
import { TranslocoService } from '@jsverse/transloco';
import { AuthService } from './auth.service';

export interface ReminderDuePayload {
  notificationId: number;
  worksheetId: number;
  worksheetName: string;
  scheduledFor: string;
  title: string;
  body: string;
}

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private hubConnection!: signalR.HubConnection;
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly transloco = inject(TranslocoService);

  /** BadgeService `AccessRequestUpdate` event akışı (atama izni request/approve — issue #13). */
  private readonly accessRequestUpdatesSubject = new Subject<AccessRequestUpdate>();
  public readonly accessRequestUpdates$ = this.accessRequestUpdatesSubject.asObservable();

  /** BadgeService `TeacherApplicationSubmitted` event akışı (bağımsız öğretmen başvurusu — issue #94, yalnızca Admin). */
  private readonly teacherApplicationSubmittedSubject = new Subject<TeacherApplicationSubmittedPayload>();
  public readonly teacherApplicationSubmitted$ = this.teacherApplicationSubmittedSubject.asObservable();

  /** BadgeService `TeacherSchoolRequestSubmitted` event akışı (öğretmen okul bağlantısı talebi — issue #277, yalnızca Admin). */
  private readonly teacherSchoolRequestSubmittedSubject = new Subject<TeacherSchoolRequestSubmittedPayload>();
  public readonly teacherSchoolRequestSubmitted$ = this.teacherSchoolRequestSubmittedSubject.asObservable();

  /**
   * BadgeService `TeacherApplicationDecided` event akışı (öğretmen başvuru kararı — issue #157).
   * Backend zaten Clients.User(sub) ile yalnızca başvuru sahibine gönderir.
   */
  private readonly teacherApplicationDecidedSubject = new Subject<TeacherApplicationDecidedPayload>();
  public readonly teacherApplicationDecided$ = this.teacherApplicationDecidedSubject.asObservable();

  public startConnection() {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/badges', {
        accessTokenFactory: () => {
          const token = localStorage.getItem('auth_token');
          return token ? token : '';
        },
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      }) // BadgeService adresi
      .build();

    this.hubConnection
      .start()
      .then(() => console.log('SignalR bağlantısı kuruldu'))
      .catch((err) => console.error('SignalR bağlantı hatası:', err));

    this.hubConnection.on('BadgeEarned', (data: any) => {
      this.snackBar.open(`🎉 ${data.badgeName}: ${data.description}`, this.t('common.close'), {
        duration: 4000,
      });
    });

    this.hubConnection.on('AccessRequestUpdate', (data: AccessRequestUpdate) => {
      this.accessRequestUpdatesSubject.next(data);
      if (data.kind === 'approved' || data.kind === 'rejected') {
        this.snackBar.open(data.title || data.body, this.t('common.ok'), { duration: 6000 });
      }
    });

    this.hubConnection.on('ReminderDue', (data: ReminderDuePayload) => {
      const ref = this.snackBar.open(`⏰ ${data.title}`, this.t('common.notifications.goToExam'), {
        duration: 8000,
      });
      ref.onAction().subscribe(() => {
        this.router.navigate(['/test', data.worksheetId]);
      });
    });

    this.hubConnection.on('TeacherApplicationSubmitted', (data: TeacherApplicationSubmittedPayload) => {
      // Backend zaten role:Admin grubuna gönderir; istemci tarafı kontrol savunma amaçlı.
      if (!this.authService.hasRole('Admin')) {
        return;
      }
      this.teacherApplicationSubmittedSubject.next(data);
      const ref = this.snackBar.open(
        this.t('common.notifications.teacherApplication', { name: data.applicantName }),
        this.t('common.notifications.goToApplications'),
        { duration: 8000 },
      );
      ref.onAction().subscribe(() => {
        this.router.navigate(['/admin/teacher-approvals']);
      });
    });

    this.hubConnection.on('TeacherSchoolRequestSubmitted', (data: TeacherSchoolRequestSubmittedPayload) => {
      // Backend zaten role:Admin grubuna gönderir; istemci tarafı kontrol savunma amaçlı (TeacherApplicationSubmitted ile aynı).
      if (!this.authService.hasRole('Admin')) {
        return;
      }
      this.teacherSchoolRequestSubmittedSubject.next(data);
      const ref = this.snackBar.open(
        this.t('common.notifications.teacherSchoolRequest', { name: data.applicantName, school: data.schoolName }),
        this.t('common.notifications.goToApplications'),
        { duration: 8000 },
      );
      ref.onAction().subscribe(() => {
        this.router.navigate(['/admin/teacher-approvals']);
      });
    });

    this.hubConnection.on('TeacherApplicationDecided', (data: TeacherApplicationDecidedPayload) => {
      this.teacherApplicationDecidedSubject.next(data);
      // Güvenlik kararı (issue #157): gerekçe/admin kimliği taşınmaz, backend'in ürettiği sabit metin gösterilir.
      // issue #157 review: yalnızca bağımsız öğretmen başvurusunda (/tutor-profile) bir hedef sayfa var;
      // okul bağlantısı talebinde (isIndependentTutor=false) öğretmenin özel bir profil sayfası yok —
      // bu durumda aksiyon BUTONU gösterilmez, sadece bilgilendirme mesajı.
      if (data.isIndependentTutor) {
        const ref = this.snackBar.open(data.body || data.title, this.t('common.notifications.goToProfile'), {
          duration: 8000,
        });
        ref.onAction().subscribe(() => {
          this.router.navigate(['/tutor-profile']);
        });
      } else {
        this.snackBar.open(data.body || data.title, this.t('common.close'), { duration: 8000 });
      }
    });
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(key, params) ?? '';
  }
}

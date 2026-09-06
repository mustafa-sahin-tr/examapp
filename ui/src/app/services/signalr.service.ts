import { inject, Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { AccessRequestUpdate } from '../models/worksheet-access-request.model';

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

  /** BadgeService `AccessRequestUpdate` event akışı (atama izni request/approve — issue #13). */
  private readonly accessRequestUpdatesSubject = new Subject<AccessRequestUpdate>();
  public readonly accessRequestUpdates$ = this.accessRequestUpdatesSubject.asObservable();

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
      this.snackBar.open(`🎉 ${data.badgeName}: ${data.description}`, 'Kapat', {
        duration: 4000,
      });
    });

    this.hubConnection.on('AccessRequestUpdate', (data: AccessRequestUpdate) => {
      this.accessRequestUpdatesSubject.next(data);
      if (data.kind === 'approved' || data.kind === 'rejected') {
        this.snackBar.open(data.title || data.body, 'Tamam', { duration: 6000 });
      }
    });

    this.hubConnection.on('ReminderDue', (data: ReminderDuePayload) => {
      const ref = this.snackBar.open(`⏰ ${data.title}`, 'Sınava Git', { duration: 8000 });
      ref.onAction().subscribe(() => {
        this.router.navigate(['/test', data.worksheetId]);
      });
    });
  }
}

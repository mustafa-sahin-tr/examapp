import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private hubConnection!: signalR.HubConnection;

  constructor(private snackBar: MatSnackBar) {}

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
  }
}

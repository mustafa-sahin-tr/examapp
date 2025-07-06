import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { BehaviorSubject, Observable } from 'rxjs';

export interface PriceUpdate {
  assetId: string;
  type: number;
  symbol: string;
  name: string;
  currentPrice: number;
  previousPrice: number;
  change: number;
  changePercent: number;
  unit: string;
  lastUpdated: Date;
  isSuccess: boolean;
}

@Injectable({
  providedIn: 'root',
})
export class SignalRService {
  private hubConnection: HubConnection | null = null;
  private priceUpdatesSubject = new BehaviorSubject<PriceUpdate[]>([]);
  private connectionStatusSubject = new BehaviorSubject<boolean>(false);

  public priceUpdates$ = this.priceUpdatesSubject.asObservable();
  public connectionStatus$ = this.connectionStatusSubject.asObservable();

  constructor() {
    this.initializeConnection();
  }

  private initializeConnection(): void {
    this.hubConnection = new HubConnectionBuilder().withUrl('/api/finance/priceUpdateHub').build();

    this.hubConnection
      .start()
      .then(() => {
        console.log('SignalR connection established');
        this.connectionStatusSubject.next(true);
        this.setupEventHandlers();
      })
      .catch((err) => {
        console.error('Error establishing SignalR connection:', err);
        this.connectionStatusSubject.next(false);
        // Yeniden bağlanmayı dene
        setTimeout(() => this.initializeConnection(), 5000);
      });

    this.hubConnection.onclose(() => {
      console.log('SignalR connection closed');
      this.connectionStatusSubject.next(false);
      // Yeniden bağlanmayı dene
      setTimeout(() => this.initializeConnection(), 5000);
    });
  }

  private setupEventHandlers(): void {
    if (!this.hubConnection) return;

    this.hubConnection.on('PriceUpdated', (priceUpdates: PriceUpdate[]) => {
      console.log('Received price updates:', priceUpdates);
      this.priceUpdatesSubject.next(priceUpdates);
    });
  }

  public joinUserGroup(userId: string): void {
    if (this.hubConnection?.state === 'Connected') {
      this.hubConnection
        .invoke('JoinGroup', userId)
        .then(() => console.log(`Joined user group: ${userId}`))
        .catch((err) => console.error('Error joining user group:', err));
    }
  }

  public leaveUserGroup(userId: string): void {
    if (this.hubConnection?.state === 'Connected') {
      this.hubConnection
        .invoke('LeaveGroup', userId)
        .then(() => console.log(`Left user group: ${userId}`))
        .catch((err) => console.error('Error leaving user group:', err));
    }
  }

  public disconnect(): void {
    if (this.hubConnection) {
      this.hubConnection
        .stop()
        .then(() => console.log('SignalR connection stopped'))
        .catch((err) => console.error('Error stopping SignalR connection:', err));
    }
  }
}

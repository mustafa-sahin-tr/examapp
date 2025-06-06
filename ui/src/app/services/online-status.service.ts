import { Injectable } from '@angular/core';
import { BehaviorSubject, fromEvent, merge, Observable } from 'rxjs';
import { map } from 'rxjs/operators';

/**
 * Service for monitoring the online/offline status of the application
 */
@Injectable({
  providedIn: 'root',
})
export class OnlineStatusService {
  private online$ = new BehaviorSubject<boolean>(navigator.onLine);

  constructor() {
    // Listen to the online/offline events
    merge(
      fromEvent(window, 'online').pipe(map(() => true)),
      fromEvent(window, 'offline').pipe(map(() => false))
    ).subscribe((online) => {
      this.online$.next(online);
    });
  }

  /**
   * Get the current online status
   */
  public isOnline(): boolean {
    return this.online$.getValue();
  }

  /**
   * Get an observable of the online status
   */
  public onlineStatus(): Observable<boolean> {
    return this.online$.asObservable();
  }
}

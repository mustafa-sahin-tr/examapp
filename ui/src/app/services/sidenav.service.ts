import { Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class SidenavService {
  isSidenavOpen = signal(true);  // Global olarak yönetilecek değişken

  toggleSidenav() {
    this.isSidenavOpen.set(!this.isSidenavOpen());
  }

  setSidenavState(state: boolean) {
    this.isSidenavOpen.set(state);
  }
}

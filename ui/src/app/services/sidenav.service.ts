import { Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class SidenavService {
  isSidenavOpen = signal(true);  // Sidebar her zaman açık
  isSidenavCollapsed = signal(false);  // Collapsed/mini mode için
  isFullScreen = signal(false);  // Global olarak yönetilecek değişken

  toggleSidenav() {
    // Artık collapse/expand yapıyoruz, tamamen gizlemiyoruz
    this.isSidenavCollapsed.set(!this.isSidenavCollapsed());
  }

  setSidenavState(state: boolean) {
    this.isSidenavOpen.set(state);
  }

  setSidenavCollapsed(collapsed: boolean) {
    this.isSidenavCollapsed.set(collapsed);
  }

  toggleSidenavState() {
    this.isSidenavOpen.set(!this.isSidenavOpen());
    console.log('sidenav state: ',this.isSidenavOpen());
  }

  toggleFullScreen() {
    this.isFullScreen.set(!this.isFullScreen());
    console.log('full screen state: ',this.isFullScreen());
  }

  setFullScreen(state: boolean) {
    this.isFullScreen.set(state);
    console.log('full screen state: ',this.isFullScreen());
  }
}

import { Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class SidenavService {
  isSidenavOpen = signal(true);  // Global olarak yönetilecek değişken
  isFullScreen = signal(false);  // Global olarak yönetilecek değişken

  toggleSidenav() {
    this.isSidenavOpen.set(!this.isSidenavOpen());
  }

  setSidenavState(state: boolean) {
    this.isSidenavOpen.set(state);
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

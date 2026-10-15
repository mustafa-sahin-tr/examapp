import { ApplicationRef, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';

import { ParentFinalWarningComponent } from './parent-final-warning.component';
import { ParentStatusStore } from '../../../services/parent-status.store';
import { parentStatusOf, awaitingStatusOf } from '../../testing/parent-status-testing';
import { translocoTestingModule } from '../../testing/transloco-testing';

@Component({ standalone: true, template: '' })
class BlankPageComponent {}

/** Issue #441: hoşgörünün son günlerinde kapatılamaz modal. */
describe('ParentFinalWarningComponent (issue #441)', () => {
  let store: ParentStatusStore;
  let router: Router;
  let dialog: MatDialog;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [ParentFinalWarningComponent, translocoTestingModule(), NoopAnimationsModule],
      providers: [
        provideRouter([
          { path: 'dashboard', component: BlankPageComponent },
          { path: 'parent-required', component: BlankPageComponent },
        ]),
      ],
    });
    store = TestBed.inject(ParentStatusStore);
    router = TestBed.inject(Router);
    dialog = TestBed.inject(MatDialog);
    await router.navigateByUrl('/dashboard');
  });

  afterEach(() => dialog.closeAll());

  /** Effect modalı change detection sırasında açar; içeriği bir sonraki turda çizilir. */
  async function create() {
    const fixture = TestBed.createComponent(ParentFinalWarningComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    // Diyalog overlay'de, uygulama görünüm ağacında: içerik ApplicationRef turunda çizilir.
    TestBed.inject(ApplicationRef).tick();
    return fixture;
  }

  function dialogText(): string {
    return document.querySelector('app-parent-final-warning-dialog')?.textContent ?? '';
  }

  it('finalWarning_OpensNonDismissibleModalWithDays', async () => {
    store.set(parentStatusOf({ mode: 'Grace', daysLeft: 4, isFinalWarning: true }));
    await create();

    expect(dialog.openDialogs.length).toBe(1);
    expect(dialog.openDialogs[0].disableClose).toBeTrue();
    expect(dialogText()).toContain('4 gün kaldı');
    expect(document.querySelector('[data-test="final-warning-action"]')).not.toBeNull();
  });

  it('notFinalWarning_NoModal', async () => {
    store.set(parentStatusOf({ mode: 'Grace', daysLeft: 20 }));
    await create();
    expect(dialog.openDialogs.length).toBe(0);
  });

  it('onParentRequiredPage_NoModal', async () => {
    await router.navigateByUrl('/parent-required');
    store.set(parentStatusOf({ mode: 'Grace', daysLeft: 4, isFinalWarning: true }));
    await create();
    expect(dialog.openDialogs.length).toBe(0);
  });

  it('actionNavigatesToParentRequired_AndModalCloses_ThenReturnsElsewhere', async () => {
    store.set(parentStatusOf({ mode: 'Grace', daysLeft: 4, isFinalWarning: true }));
    const fixture = await create();

    (document.querySelector('[data-test="final-warning-action"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/parent-required');
    expect(dialog.openDialogs.length).toBe(0);

    await router.navigateByUrl('/dashboard');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(dialog.openDialogs.length).toBe(1);
  });

  it('statusChangesToAwaiting_ModalCloses', async () => {
    store.set(parentStatusOf({ mode: 'Grace', daysLeft: 4, isFinalWarning: true }));
    const fixture = await create();
    expect(dialog.openDialogs.length).toBe(1);

    store.set(awaitingStatusOf());
    fixture.detectChanges();
    await fixture.whenStable();

    expect(dialog.openDialogs.length).toBe(0);
  });
});

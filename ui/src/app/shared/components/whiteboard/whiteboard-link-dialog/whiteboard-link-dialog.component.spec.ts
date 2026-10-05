import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import whiteboardTr from '../../../../../../public/i18n/whiteboard/tr.json';
import { translocoTestingModule } from '../../../testing/transloco-testing';
import {
  WHITEBOARD_LINK_DIALOG_DESCRIPTION_ID,
  WhiteboardLinkDialogComponent,
  openWhiteboardLinkDialog,
} from './whiteboard-link-dialog.component';

const translocoTesting = translocoTestingModule({ langs: { 'whiteboard/tr': whiteboardTr } });

describe('WhiteboardLinkDialogComponent (issue #332)', () => {
  const URL = 'https://evil.example.org/login?next=%2F';
  let ref: MatDialogRef<WhiteboardLinkDialogComponent, boolean>;
  let result: boolean | undefined | 'pending';

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [WhiteboardLinkDialogComponent, translocoTesting, NoopAnimationsModule] });
    ref = openWhiteboardLinkDialog(TestBed.inject(MatDialog), { url: URL, host: 'evil.example.org' });
    result = 'pending';
    ref.afterClosed().subscribe((value) => (result = value));
    // Sözlük yüklemesi (transloco) + dialog açılış animasyonu tamamlansın; ardından görünüm güncellenir.
    const appRef = TestBed.inject(ApplicationRef);
    appRef.tick();
    await new Promise((resolve) => setTimeout(resolve, 0));
    appRef.tick();
    await appRef.whenStable();
  });

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  function overlay(): HTMLElement {
    return document.querySelector('.cdk-overlay-container') as HTMLElement;
  }

  function button(testId: string): HTMLButtonElement {
    return overlay().querySelector(`[data-testid="${testId}"]`) as HTMLButtonElement;
  }

  async function closed(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }

  it('ShowsFullTargetUrlHostAndTexts', () => {
    expect(overlay().querySelector('[data-testid="wb-link-url"]')?.textContent?.trim()).toBe(URL);
    const text = overlay().textContent ?? '';
    expect(text).toContain(whiteboardTr.linkDialog.title);
    expect(text).toContain(whiteboardTr.linkDialog.intro.replace('{{host}}', 'evil.example.org'));
    expect(button('wb-link-cancel').textContent?.trim()).toBe(whiteboardTr.linkDialog.cancel);
    expect(button('wb-link-proceed').textContent?.trim()).toBe(whiteboardTr.linkDialog.proceed);
  });

  it('Container_IsAlertDialogDescribedByContent', () => {
    const container = overlay().querySelector('.mat-mdc-dialog-container') as HTMLElement;
    expect(container.getAttribute('role')).toBe('alertdialog');
    expect(container.getAttribute('aria-describedby')).toBe(WHITEBOARD_LINK_DIALOG_DESCRIPTION_ID);
    const description = document.getElementById(WHITEBOARD_LINK_DIALOG_DESCRIPTION_ID);
    expect(description?.textContent).toContain(URL);
  });

  it('DefaultFocus_IsCancel_SoEnterDoesNotOpen', () => {
    expect(document.activeElement).toBe(button('wb-link-cancel'));
  });

  it('Proceed_ClosesWithTrue', async () => {
    button('wb-link-proceed').click();
    await closed();
    expect(result).toBeTrue();
  });

  it('Cancel_ClosesWithFalse', async () => {
    button('wb-link-cancel').click();
    await closed();
    expect(result).toBeFalse();
  });

  it('Escape_ClosesWithoutProceeding', async () => {
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true });
    Object.defineProperty(event, 'keyCode', { get: () => 27 });
    (overlay().querySelector('mat-dialog-container, .mat-mdc-dialog-container') as HTMLElement).dispatchEvent(event);
    await closed();
    expect(result).toBeUndefined();
  });
});

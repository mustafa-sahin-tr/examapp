import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheet, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { Subject } from 'rxjs';

import {
  SelectionSummaryComponent,
  SelectionSummaryItem,
  SelectionSummaryLabels,
} from './selection-summary.component';

const LABELS: SelectionSummaryLabels = {
  title: 'Seçimlerin',
  pending: 'Seçim bekleniyor',
  remaining: 'Kalan sorular: 3',
  edit: 'Düzenle',
  all: 'Tümü',
};

const ITEMS: SelectionSummaryItem[] = [
  { stepId: 1, icon: 'timer', caption: 'Süreli mi?', value: 'Süreli Çalışma', extraCount: 0 },
  { stepId: 6, icon: 'looks_one', caption: 'Gün?', value: 'Pazartesi, Salı', extraCount: 1 },
];

describe('SelectionSummaryComponent (issue #135)', () => {
  let fixture: ComponentFixture<SelectionSummaryComponent>;
  let el: HTMLElement;
  let edits: number[];

  async function setup(providers: unknown[] = []): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [SelectionSummaryComponent],
      providers: [provideNoopAnimations(), ...(providers as never[])],
    }).compileComponents();
    fixture = TestBed.createComponent(SelectionSummaryComponent);
    el = fixture.nativeElement;
    edits = [];
    fixture.componentInstance.edit.subscribe((id) => edits.push(id));
    fixture.componentRef.setInput('items', ITEMS);
    fixture.componentRef.setInput('labels', LABELS);
  }

  function render(inputs: Record<string, unknown> = {}): void {
    Object.entries(inputs).forEach(([k, v]) => fixture.componentRef.setInput(k, v));
    fixture.detectChanges();
  }

  it('rail_RendersAsideRowsPendingAndRemaining', async () => {
    await setup();
    render({ variant: 'rail', pendingCaption: 'Kaç ders?', remainingCount: 3 });

    const aside = el.querySelector('aside')!;
    expect(aside.getAttribute('aria-label')).toBe('Seçimlerin');
    const rows = el.querySelectorAll<HTMLButtonElement>('button.ss-row');
    expect(rows.length).toBe(2);
    expect(rows[1].getAttribute('aria-label')).toBe('Gün?: Pazartesi, Salı. Düzenle');
    expect(rows[1].querySelector('.ss-row__extra')?.textContent?.trim()).toBe('+1');
    expect(el.querySelector('.ss-row--pending')?.textContent).toContain('Seçim bekleniyor');
    expect(el.querySelector('.ss-row--remaining')?.textContent).toContain('Kalan sorular: 3');
  });

  it('row_Click_EmitsEditStepId', async () => {
    await setup();
    render({ variant: 'rail' });
    el.querySelectorAll<HTMLButtonElement>('button.ss-row')[1].click();
    expect(edits).toEqual([6]);
  });

  it('review_HidesPendingAndRemainingRows', async () => {
    await setup();
    render({ variant: 'review', pendingCaption: 'Kaç ders?', remainingCount: 3 });
    expect(el.querySelectorAll('button.ss-row').length).toBe(2);
    expect(el.querySelector('.ss-row--pending')).toBeNull();
    expect(el.querySelector('.ss-row--remaining')).toBeNull();
  });

  it('chips_RendersListOfButtonsAndAllChip', async () => {
    await setup();
    render({ variant: 'chips' });
    const list = el.querySelector('ul.ss-chips')!;
    expect(list.getAttribute('role')).toBe('list');
    const chips = list.querySelectorAll<HTMLButtonElement>('button.ss-chip');
    expect(chips.length).toBe(3);
    expect(chips[0].getAttribute('aria-label')).toBe('Süreli mi?: Süreli Çalışma. Düzenle');
    expect(chips[2].textContent).toContain('Tümü');

    chips[0].click();
    expect(edits).toEqual([1]);
  });

  it('allChip_OpensBottomSheetAndEmitsDismissedStepId', async () => {
    const dismissed = new Subject<number | undefined>();
    const sheet = jasmine.createSpyObj<MatBottomSheet>('MatBottomSheet', ['open']);
    sheet.open.and.returnValue({ afterDismissed: () => dismissed.asObservable() } as unknown as MatBottomSheetRef);
    await setup([{ provide: MatBottomSheet, useValue: sheet }]);
    render({ variant: 'chips' });

    el.querySelector<HTMLButtonElement>('button.ss-chip--all')!.click();
    expect(sheet.open).toHaveBeenCalledTimes(1);
    const config = sheet.open.calls.mostRecent().args[1]!;
    expect(config.data).toEqual({ items: ITEMS, labels: LABELS });

    dismissed.next(6);
    expect(edits).toEqual([6]);
  });

  it('allChip_DismissedWithoutChoice_DoesNotEmit', async () => {
    const dismissed = new Subject<number | undefined>();
    const sheet = jasmine.createSpyObj<MatBottomSheet>('MatBottomSheet', ['open']);
    sheet.open.and.returnValue({ afterDismissed: () => dismissed.asObservable() } as unknown as MatBottomSheetRef);
    await setup([{ provide: MatBottomSheet, useValue: sheet }]);
    render({ variant: 'chips' });
    el.querySelector<HTMLButtonElement>('button.ss-chip--all')!.click();
    dismissed.next(undefined);
    expect(edits).toEqual([]);
  });

  it('insideSheet_RendersReviewFromDataAndDismissesWithStepId', async () => {
    const ref = jasmine.createSpyObj<MatBottomSheetRef>('MatBottomSheetRef', ['dismiss']);
    await setup([
      { provide: MAT_BOTTOM_SHEET_DATA, useValue: { items: ITEMS, labels: LABELS } },
      { provide: MatBottomSheetRef, useValue: ref },
    ]);
    fixture.componentRef.setInput('items', []);
    render({ variant: 'chips' });

    expect(el.querySelector('ul.ss-chips')).toBeNull();
    const rows = el.querySelectorAll<HTMLButtonElement>('button.ss-row');
    expect(rows.length).toBe(2);
    rows[0].click();
    expect(ref.dismiss).toHaveBeenCalledWith(1);
    expect(edits).toEqual([]);
  });
});

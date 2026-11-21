import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { BookImportSharingBarComponent } from './book-import-sharing-bar.component';
import { WorksheetTeacherSharing } from '../../../models/test-instance';
import { AdminService } from '../../../services/admin.service';
import { BookImportService } from '../../../services/book-import.service';
import { SchoolService } from '../../../services/school.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import tr from '../../../../../public/i18n/book-imports/tr.json';

describe('BookImportSharingBarComponent (#1266)', () => {
  let fixture: ComponentFixture<BookImportSharingBarComponent>;
  let api: MockedObject<BookImportService>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector<HTMLElement>(`[data-testid="${id}"]`);

  function setup(sharing: WorksheetTeacherSharing, canChange: boolean): void {
    api = { setSharing: vi.fn().mockReturnValue(of({ updatedDrafts: 2 })) } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportSharingBarComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [
        provideNoopAnimations(),
        { provide: BookImportService, useValue: api },
        { provide: AdminService, useValue: { getTeachers: vi.fn().mockReturnValue(of([])) } },
        { provide: SchoolService, useValue: { search: vi.fn().mockReturnValue(of([])), getById: vi.fn(), provinces: vi.fn().mockReturnValue(of([])), districts: vi.fn().mockReturnValue(of([])) } },
      ],
    });
    fixture = TestBed.createComponent(BookImportSharingBarComponent);
    fixture.componentRef.setInput('jobId', 'a1');
    fixture.componentRef.setInput('sharing', sharing);
    fixture.componentRef.setInput('canChange', canChange);
    fixture.detectChanges();
  }

  it('shows the sharing picked at confirmation and changes it for the whole import', () => {
    setup(WorksheetTeacherSharing.Private, true);
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);

    expect(q('sharing-value')?.textContent?.trim()).toBe('Özel');
    expect(el().textContent).toContain('onaylanmamış testlere ve sonra alınacaklara uygulanır');

    q('sharing-edit')!.click();
    fixture.detectChanges();
    fixture.componentInstance.draft.set(WorksheetTeacherSharing.PublicAssignable);
    fixture.detectChanges();
    q('sharing-save')!.click();
    fixture.detectChanges();

    expect(api.setSharing).toHaveBeenCalledWith('a1', WorksheetTeacherSharing.PublicAssignable, undefined);
    expect(changed).toHaveBeenCalled();
    expect(q('sharing-save')).toBeNull();
  });

  it('is read-only once the import cannot change it', () => {
    setup(WorksheetTeacherSharing.PublicView, false);
    expect(q('sharing-value')?.textContent?.trim()).toBe('Herkese açık (görüntüleme)');
    expect(q('sharing-edit')).toBeNull();
    expect(el().textContent).toContain('Aktarım bitti');
  });

  it('shows the server message when saving fails and keeps the editor open', () => {
    setup(WorksheetTeacherSharing.Private, true);
    api.setSharing.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Paylaşım seçimi geçersiz.' } })),
    );
    q('sharing-edit')!.click();
    fixture.detectChanges();
    q('sharing-save')!.click();
    fixture.detectChanges();

    expect(el().querySelector('.bisb-error')?.textContent).toContain('Paylaşım seçimi geçersiz.');
    expect(q('sharing-save')).not.toBeNull();
  });
});

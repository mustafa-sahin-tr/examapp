import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { TargetImportPreview } from '../../models/target-goal.model';
import { TargetGoalService } from '../../services/target-goal.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { TargetProgramsComponent } from './target-programs.component';
import goalsTr from '../../../../public/i18n/target-goals/tr.json';
import goalsEn from '../../../../public/i18n/target-goals/en.json';

const preview: TargetImportPreview = {
  newCount: 1, changedCount: 1, unchangedCount: 0, invalidCount: 1,
  new: [{ line: 2, scoreType: 'lgs', name: 'Yeni Lisesi', institution: '', city: 'Ankara', district: '', minimumScore: 450, percentile: null }],
  changed: [{ line: 3, scoreType: 'lgs', name: 'Değişen Lisesi', institution: '', city: 'Ankara', district: '', minimumScore: 460, percentile: null, previousMinimumScore: 455, previousPercentile: null }],
  invalid: [{ line: 4, code: 'MinimumScoreInvalid', name: 'Bozuk Lisesi' }],
};

describe('TargetProgramsComponent (#695)', () => {
  let service: MockedObject<TargetGoalService>;
  let fixture: ComponentFixture<TargetProgramsComponent>;
  let el: HTMLElement;

  const button = (text: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes(text))!;

  beforeEach(async () => {
    service = {
      adminList: vi.fn().mockReturnValue(of({
        total: 2,
        items: [
          { id: 1, scoreType: 'lgs', name: 'Seçili Lisesi', institution: '', city: 'Ankara', district: '', year: 2026, minimumScore: 450, percentile: null, sourceReference: 's', inUse: true },
          { id: 2, scoreType: 'lgs', name: 'Boş Lisesi', institution: '', city: 'Ankara', district: '', year: 2026, minimumScore: 440, percentile: null, sourceReference: 's', inUse: false },
        ],
      })),
      adminSave: vi.fn(),
      adminDelete: vi.fn().mockReturnValue(of(undefined)),
      previewImport: vi.fn().mockReturnValue(of(preview)),
      confirmImport: vi.fn().mockReturnValue(of({ created: 1, updated: 1, unchanged: 0, skipped: 1 })),
    } as unknown as MockedObject<TargetGoalService>;

    TestBed.configureTestingModule({
      imports: [TargetProgramsComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'target-goals/tr': goalsTr, 'target-goals/en': goalsEn } })],
      providers: [{ provide: TargetGoalService, useValue: service }],
    });
    fixture = TestBed.createComponent(TargetProgramsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    el = fixture.nativeElement;
  });

  async function chooseFile(): Promise<void> {
    const component = fixture.componentInstance as unknown as { pickFile(files: FileList): void; changeImportSource(value: string): void };
    component.changeImportSource('Resmî belge');
    component.pickFile([new File(['x'], 'puanlar.csv')] as unknown as FileList);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('preview shows the three groups and writes nothing until confirmed', async () => {
    await chooseFile();
    button(goalsTr.admin.import.preview).click();
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="import-counts"]')?.textContent).toContain('Yeni 1, değişen 1, aynı 0, hatalı 1');
    expect(el.textContent).toContain('Yeni Lisesi');
    expect(el.textContent).toContain('455 → 460');
    expect(el.textContent).toContain(goalsTr.admin.errors.MinimumScoreInvalid);
    expect(service.confirmImport).not.toHaveBeenCalled();

    button(goalsTr.admin.import.confirm).click();
    fixture.detectChanges();

    expect(service.confirmImport).toHaveBeenCalledTimes(1);
    expect(service.confirmImport.mock.calls[0][0].source).toBe('Resmî belge');
    expect(el.textContent).toContain('Yazıldı: 1 yeni, 1 güncellendi, 0 aynı, 1 atlandı.');
  });

  it('changing the source drops the preview so confirm cannot use other values', async () => {
    await chooseFile();
    button(goalsTr.admin.import.preview).click();
    fixture.detectChanges();

    (fixture.componentInstance as unknown as { changeImportSource(value: string): void }).changeImportSource('Başka belge');
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="import-counts"]')).toBeNull();
  });

  it('translates a file error from the server', async () => {
    service.previewImport.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'CsvHeaderMissing' } })));
    await chooseFile();
    button(goalsTr.admin.import.preview).click();
    fixture.detectChanges();

    expect(el.textContent).toContain(goalsTr.admin.errors.CsvHeaderMissing);
  });

  it('a chosen programme offers correction but no delete', () => {
    const rows = Array.from(el.querySelectorAll('.me-row'));

    expect(rows[0].textContent).toContain(goalsTr.admin.inUse);
    expect(rows[0].textContent).not.toContain(goalsTr.admin.delete);
    expect(rows[1].textContent).toContain(goalsTr.admin.delete);
  });
});

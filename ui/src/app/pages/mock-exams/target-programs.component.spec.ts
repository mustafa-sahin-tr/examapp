import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
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

  const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;
  const button = (text: string, root: HTMLElement = el) => Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes(text))!;
  const dialogButton = (text: string) => button(text, overlay());
  // Diyalog görünümü fixture'a değil uygulamaya bağlı: ikisi de yenilenir.
  const render = () => {
    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();
  };

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
  });

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
      adminSummary: vi.fn().mockReturnValue(of([
        { exam: 'lgs', total: 3, latestYear: 2026, latestYearCount: 2 },
        { exam: 'yks', total: 0, latestYear: null, latestYearCount: 0 },
      ])),
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
    button(goalsTr.admin.import.title).click();
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance as unknown as { pickFile(files: FileList): void; changeImportSource(value: string): void };
    component.changeImportSource('Resmî belge');
    component.pickFile([new File(['x'], 'puanlar.csv')] as unknown as FileList);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('preview shows the three groups and writes nothing until confirmed', async () => {
    await chooseFile();
    expect(overlay().textContent).toContain('CSV yükle · 1 / 3');
    dialogButton(goalsTr.admin.import.preview).click();
    render();

    expect(overlay().textContent).toContain('CSV yükle · 2 / 3');
    expect(overlay().querySelector('[data-testid="import-counts"]')?.textContent).toContain('Yeni 1, değişen 1, aynı 0, hatalı 1');
    expect(overlay().textContent).toContain('Yeni Lisesi');
    expect(overlay().textContent).toContain('455 → 460');
    expect(overlay().textContent).toContain(goalsTr.admin.errors.MinimumScoreInvalid);
    expect(service.confirmImport).not.toHaveBeenCalled();

    dialogButton('2 satırı yaz').click();
    render();

    expect(service.confirmImport).toHaveBeenCalledTimes(1);
    expect(service.confirmImport.mock.calls[0][0].source).toBe('Resmî belge');
    expect(overlay().textContent).toContain('CSV yükle · 3 / 3');
    expect(overlay().textContent).toContain('Yazıldı: 1 yeni, 1 güncellendi, 0 aynı, 1 atlandı.');
    expect(service.adminSummary).toHaveBeenCalledTimes(2);
  });

  it('back returns to the file step and drops the preview', async () => {
    await chooseFile();
    dialogButton(goalsTr.admin.import.preview).click();
    render();

    dialogButton(goalsTr.admin.import.back).click();
    render();

    expect(overlay().querySelector('[data-testid="import-counts"]')).toBeNull();
    expect(overlay().textContent).toContain('CSV yükle · 1 / 3');
  });

  it('changing the source drops the preview so confirm cannot use other values', async () => {
    await chooseFile();
    dialogButton(goalsTr.admin.import.preview).click();
    render();

    (fixture.componentInstance as unknown as { changeImportSource(value: string): void }).changeImportSource('Başka belge');
    render();

    expect(overlay().querySelector('[data-testid="import-counts"]')).toBeNull();
  });

  it('translates a file error from the server', async () => {
    service.previewImport.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'CsvHeaderMissing' } })));
    await chooseFile();
    dialogButton(goalsTr.admin.import.preview).click();
    render();

    expect(overlay().textContent).toContain(goalsTr.admin.errors.CsvHeaderMissing);
  });

  it('status strip says which exams have data and warns when students get no results', () => {
    const lgs = el.querySelector('[data-exam="lgs"]')!;
    const yks = el.querySelector('[data-exam="yks"]')!;

    expect(lgs.textContent).toContain('LGS 2026');
    expect(lgs.textContent).toContain('2 okul');
    expect(lgs.textContent).toContain('Tüm yıllar: 3');
    expect(yks.textContent).toContain(goalsTr.admin.status.none);
    expect(yks.textContent).toContain(goalsTr.admin.status.studentsBlocked);
    expect(lgs.textContent).not.toContain(goalsTr.admin.status.studentsBlocked);
  });

  it('search filters while typing without a separate button', async () => {
    vi.useFakeTimers();
    try {
      (fixture.componentInstance as unknown as { search(value: string): void }).search('Seç');
      expect(service.adminList).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(300);

      expect(service.adminList).toHaveBeenCalledTimes(2);
      expect(service.adminList.mock.calls[1][0].search).toBe('Seç');
    } finally {
      vi.useRealTimers();
    }
  });

  it('a chosen programme offers correction but no delete', () => {
    const rows = Array.from(el.querySelectorAll('.me-row'));

    expect(rows[0].textContent).toContain(goalsTr.admin.inUse);
    expect(rows[0].textContent).not.toContain(goalsTr.admin.delete);
    expect(rows[1].textContent).toContain(goalsTr.admin.delete);
  });
});
